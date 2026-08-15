using System.IO.Compression;
using System.Text;
using DfE.CheckPerformanceData.Application.ContentStaging;

namespace DfE.CheckPerformanceData.Application.UnitTests.ContentStaging;

// Bundles are JSON, and JSON compresses roughly 8-10x, so shipping the export zipped is most of
// a transfer-size problem solved for very little code. The read side has to keep accepting the
// plain .json bundles already in circulation, which means sniffing rather than trusting the file
// extension — and it is an untrusted upload, so the decompressed size has to be bounded too.
public class ContentBundleArchiveTests
{
    private const long Cap = 1024 * 1024;

    private static string SampleJson() =>
        ContentStagingJson.Serialize(new ContentBundle
        {
            PageNodes = [new() { Id = Guid.NewGuid(), Segment = "help", Title = "Help", PageType = "folder" }]
        });

    [Fact]
    public void ToZip_ThenRead_RoundTripsTheJsonExactly()
    {
        var json = SampleJson();

        var read = ContentBundleArchive.ReadBundleJson(ContentBundleArchive.ToZip(json), Cap);

        Assert.Equal(json, read);
    }

    [Fact]
    public void ToZip_ProducesSomethingThatStartsWithTheZipMagicBytes()
    {
        var zip = ContentBundleArchive.ToZip(SampleJson());

        Assert.Equal([0x50, 0x4B, 0x03, 0x04], zip.Take(4));
    }

    // Repetitive JSON is exactly what deflate is good at; if this ever stopped being a clear win
    // the feature would not be worth the branch on the read side.
    [Fact]
    public void ToZip_MeaningfullyShrinksARealisticBundle()
    {
        var json = ContentStagingJson.Serialize(new ContentBundle
        {
            PageNodes = Enumerable.Range(0, 200)
                .Select(i => new PageNodeBundleItem
                {
                    Id = Guid.NewGuid(),
                    Segment = $"page-{i}",
                    Title = $"Page {i}",
                    PageType = "content",
                    Versions = [new() { VersionId = 1, Content = "<p>Some repetitive body content.</p>" }]
                })
                .ToList()
        });

        var zip = ContentBundleArchive.ToZip(json);

        Assert.True(zip.Length * 4 < Encoding.UTF8.GetByteCount(json),
            $"expected at least 4x compression, got {Encoding.UTF8.GetByteCount(json)} -> {zip.Length}");
    }

    // The bundles already downloaded from previous releases are plain JSON and must keep working.
    [Fact]
    public void Read_PlainJson_IsReturnedUnchanged()
    {
        var json = SampleJson();

        var read = ContentBundleArchive.ReadBundleJson(Encoding.UTF8.GetBytes(json), Cap);

        Assert.Equal(json, read);
    }

    // A UTF-8 BOM is what a Windows text editor leaves behind on a hand-edited bundle. It is not
    // JSON, so it has to come off rather than be handed to the parser.
    [Fact]
    public void Read_PlainJsonWithByteOrderMark_StripsIt()
    {
        var json = SampleJson();
        var withBom = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(json)).ToArray();

        Assert.Equal(json, ContentBundleArchive.ReadBundleJson(withBom, Cap));
    }

    [Fact]
    public void Read_InvalidUtf8_Throws_SoTheOperatorGetsTheEncodingBanner()
    {
        byte[] invalid = [0x7B, 0xFF, 0xFE, 0x7D];   // { <lone continuation bytes> }

        Assert.Throws<DecoderFallbackException>(() => ContentBundleArchive.ReadBundleJson(invalid, Cap));
    }

    [Fact]
    public void Read_InvalidUtf8_InsideAZip_ThrowsTheSameWay()
    {
        var zip = ZipContaining("bundle.json", [0x7B, 0xFF, 0xFE, 0x7D]);

        Assert.Throws<DecoderFallbackException>(() => ContentBundleArchive.ReadBundleJson(zip, Cap));
    }

    [Fact]
    public void Read_EmptyZip_ThrowsAFormatError()
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true)) { }

        Assert.Throws<ContentBundleFormatException>(
            () => ContentBundleArchive.ReadBundleJson(buffer.ToArray(), Cap));
    }

    [Fact]
    public void Read_CorruptZip_ThrowsAFormatError()
    {
        byte[] corrupt = [0x50, 0x4B, 0x03, 0x04, 0x00, 0x01, 0x02, 0x03];

        Assert.Throws<ContentBundleFormatException>(
            () => ContentBundleArchive.ReadBundleJson(corrupt, Cap));
    }

    // The whole point of a bounded read: an entry declaring a modest size can still decompress
    // to gigabytes, so the limit has to be enforced as the bytes arrive rather than trusted from
    // the header. One megabyte of zeros compresses to about a kilobyte, which sails through any
    // check made against the uploaded size.
    [Fact]
    public void Read_ZipThatDecompressesPastTheCap_ThrowsBeforeExhaustingMemory()
    {
        var zip = ZipContaining("bundle.json", new byte[4 * 1024 * 1024]);

        var ex = Assert.Throws<ContentBundleFormatException>(
            () => ContentBundleArchive.ReadBundleJson(zip, maxDecompressedBytes: 1024 * 1024));
        Assert.Contains("too large", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // Nothing here writes to disk, so a traversal name is harmless — but taking the first entry
    // blindly would let a stray directory entry or a macOS resource fork shadow the real bundle.
    [Fact]
    public void Read_ZipWithSeveralEntries_PrefersTheJsonOne()
    {
        var json = SampleJson();
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var noise = new StreamWriter(archive.CreateEntry("__MACOSX/._bundle.json").Open()))
            {
                noise.Write("not the bundle");
            }
            using var entry = new StreamWriter(archive.CreateEntry("bundle.json").Open());
            entry.Write(json);
        }

        Assert.Equal(json, ContentBundleArchive.ReadBundleJson(buffer.ToArray(), Cap));
    }

    // ZipArchiveEntry.Name is the leaf filename, so matching on it would let a nested
    // payload/bundle.json win over the root one an operator sees when they open the archive.
    // What gets imported has to be what the file appears to contain.
    [Fact]
    public void Read_ZipWithANestedBundleJson_PrefersTheRootEntry()
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            // Written first so it would be found first by any FirstOrDefault over Entries.
            using (var nested = new StreamWriter(archive.CreateEntry("payload/bundle.json").Open()))
            {
                nested.Write("{\"which\":\"nested\"}");
            }
            using var root = new StreamWriter(archive.CreateEntry("bundle.json").Open());
            root.Write("{\"which\":\"root\"}");
        }

        Assert.Equal("{\"which\":\"root\"}", ContentBundleArchive.ReadBundleJson(buffer.ToArray(), Cap));
    }

    // The central directory is parsed lazily, when Entries is first touched — not by the
    // ZipArchive constructor. Corruption in that region therefore surfaces later than the
    // obvious place to guard, and an InvalidDataException escaping here reaches the operator
    // as a 500 rather than the "not a readable zip" banner.
    [Fact]
    public void Read_ZipWithCorruptCentralDirectory_ThrowsAFormatError()
    {
        var zip = ContentBundleArchive.ToZip(SampleJson());

        // The End Of Central Directory record is the last 22 bytes of an archive with no
        // comment; bytes 10-11 are its entry count. Claiming more entries than the central
        // directory holds is rejected when Entries is enumerated, not when the archive is
        // constructed.
        zip[^12] = 0xFF;
        zip[^11] = 0xFF;

        Assert.Throws<ContentBundleFormatException>(
            () => ContentBundleArchive.ReadBundleJson(zip, Cap));
    }

    // Whole-file sweep: no single-byte corruption anywhere in a bundle zip may produce anything
    // other than a bundle, a format error, or an encoding error. Anything else is a 500.
    [Fact]
    public void Read_AnySingleByteCorruption_NeverEscapesAsAnUnexpectedException()
    {
        var original = ContentBundleArchive.ToZip(SampleJson());
        var escapes = new List<string>();

        for (var i = 0; i < original.Length; i++)
        {
            var mutated = (byte[])original.Clone();
            mutated[i] ^= 0xFF;
            try
            {
                ContentBundleArchive.ReadBundleJson(mutated, Cap);
            }
            catch (ContentBundleFormatException) { }
            catch (DecoderFallbackException) { }
            catch (Exception ex)
            {
                escapes.Add($"byte {i}: {ex.GetType().Name}");
            }
        }

        Assert.True(escapes.Count == 0,
            $"{escapes.Count} corruptions escaped as unexpected exceptions: {string.Join("; ", escapes.Take(5))}");
    }

    private static byte[] ZipContaining(string entryName, byte[] content)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var entry = archive.CreateEntry(entryName).Open();
            entry.Write(content);
        }
        return buffer.ToArray();
    }
}
