using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using DfE.CheckPerformanceData.Application.ContentStaging;
using DfE.CheckPerformanceData.Application.Media;
using DfE.CheckPerformanceData.Application.Media.Upload;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DfE.CheckPerformanceData.Application.UnitTests.ContentStaging;

// Media in content staging bundles: the export carries every library image the content uses,
// each file once, and the import brings each image in under its own id or reuses the copy the
// library already holds. Nothing about an image stops the import; problems are warnings.
public class ContentMediaStagingTests
{
    private static readonly Guid Photo = new("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Chart = new("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Missing = new("33333333-3333-4333-8333-333333333333");

    private readonly FakeMediaLibrary _library = new();
    private readonly FakeCleaner _cleaner = new();

    private ContentMediaStaging Sut() => new(
        _library, _library, _cleaner,
        new MediaProcessingGate(Options.Create(new MediaProcessingOptions())),
        NullLogger<ContentMediaStaging>.Instance);

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    private static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static string ImageWidget(Guid id) => new JsonArray(new JsonObject
    {
        ["kind"] = "widget",
        ["type"] = "image",
        ["props"] = new JsonObject { ["mediaId"] = id.ToString() }
    }).ToJsonString();

    private static string RichTextWidget(Guid id) => new JsonArray(new JsonObject
    {
        ["kind"] = "widget",
        ["type"] = "richtext",
        ["props"] = new JsonObject { ["html"] = $"<p><img data-media-id=\"{id}\" src=\"/media/{id}/x.png\" alt=\"a\"></p>" }
    }).ToJsonString();

    private static PageNodeBundleItem Page(string title, params PageNodeVersionBundleItem[] versions) => new()
    {
        Id = Guid.NewGuid(), Segment = title.ToLowerInvariant(), Title = title, PageType = "content", Versions = [.. versions]
    };

    private static PageNodeVersionBundleItem Version(int id, string content, params MediaPinBundleItem[] pins) =>
        new() { VersionId = id, Content = content, Media = [.. pins] };

    private static ContentBlockBundleItem Block(string type, Guid id) => new()
    {
        Id = Guid.NewGuid(), Key = "k-" + id.ToString("N")[..6], BlockType = type,
        Value = $"<img data-media-id=\"{id}\" src=\"/media/{id}/a.png\" alt=\"a\">"
    };

    private static MediaAssetBundleItem BundleAsset(Guid id, params (int Version, byte[] Bytes)[] versions) => new()
    {
        Id = id,
        FileName = "photo.png",
        DefaultAltText = "A photo",
        Caption = "Caption",
        Credit = "Credit",
        Tags = ["one", "two"],
        CurrentVersion = versions.Max(v => v.Version),
        Versions = versions.Select(v => new MediaAssetVersionBundleItem
        {
            Version = v.Version, Sha256 = Sha(v.Bytes), ContentType = "image/png", Width = 2, Height = 1, SizeBytes = v.Bytes.Length
        }).ToList()
    };

    private static List<MediaFileBundleItem> Files(params byte[][] files) =>
        files.Select(f => new MediaFileBundleItem { Sha256 = Sha(f), Data = f }).ToList();

    // ── export ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Collect_carries_images_from_widgets_rich_text_and_content_blocks_with_their_metadata()
    {
        var photo = _library.Seed(Photo, Bytes("photo v1"));
        var chart = _library.Seed(Chart, Bytes("chart v1"));
        var page = Page("Home", Version(1, ImageWidget(Photo)));
        var block = Block("Content", Chart);

        var media = await Sut().CollectAsync([page], [block]);

        Assert.Equal([Photo, Chart], media.Assets.Select(a => a.Id).Order());
        var exported = media.Assets.Single(a => a.Id == Photo);
        Assert.Equal(photo.FileName, exported.FileName);
        Assert.Equal("Alt text", exported.DefaultAltText);
        Assert.Equal("A caption", exported.Caption);
        Assert.Equal("A credit", exported.Credit);
        Assert.Equal(["tag"], exported.Tags);
        Assert.Equal(1, exported.CurrentVersion);
        var version = Assert.Single(exported.Versions);
        Assert.Equal(Sha(Bytes("photo v1")), version.Sha256);
        Assert.Equal("image/png", version.ContentType);
        Assert.Equal(20, version.Width);
        Assert.Equal(10, version.Height);
        Assert.Equal(Bytes("chart v1"), media.Files.Single(f => f.Sha256 == chart.Latest.Sha256).Data);
    }

    [Fact]
    public async Task Collect_reads_rich_text_images_in_content_pages()
    {
        _library.Seed(Photo, Bytes("photo v1"));

        var media = await Sut().CollectAsync([Page("Home", Version(1, RichTextWidget(Photo)))], []);

        Assert.Equal(Photo, Assert.Single(media.Assets).Id);
    }

    [Fact]
    public async Task Collect_carries_the_current_version_and_every_pinned_older_version()
    {
        _library.Seed(Photo, Bytes("photo v1"));
        _library.AddVersion(Photo, Bytes("photo v2"));
        _library.AddVersion(Photo, Bytes("photo v3"));
        var page = Page("Home",
            Version(1, ImageWidget(Photo), new MediaPinBundleItem { MediaId = Photo, Version = 1 }),
            Version(2, ImageWidget(Photo), new MediaPinBundleItem { MediaId = Photo, Version = 3 }));

        var media = await Sut().CollectAsync([page], []);

        var asset = Assert.Single(media.Assets);
        Assert.Equal(3, asset.CurrentVersion);
        Assert.Equal([1, 3], asset.Versions.Select(v => v.Version));
        Assert.Equal(2, media.Files.Count);
    }

    [Fact]
    public async Task Collect_carries_each_file_once_however_many_versions_share_it()
    {
        var same = Bytes("same file");
        _library.Seed(Photo, same);
        _library.Seed(Chart, same);
        _library.AddVersion(Chart, Bytes("chart v2"));
        _library.AddVersion(Chart, same);
        var page = Page("Home",
            Version(1, ImageWidget(Photo) + "", new MediaPinBundleItem { MediaId = Chart, Version = 1 }),
            Version(2, RichTextWidget(Chart)));

        var media = await Sut().CollectAsync([page], [Block("Content", Photo)]);

        Assert.Equal(2, media.Assets.Count);
        var file = Assert.Single(media.Files);
        Assert.Equal(Sha(same), file.Sha256);
    }

    [Fact]
    public async Task Collect_leaves_out_an_image_that_is_not_in_the_library_and_a_block_that_is_not_rich_text()
    {
        _library.Seed(Photo, Bytes("photo v1"));

        var media = await Sut().CollectAsync(
            [Page("Home", Version(1, ImageWidget(Missing)))], [Block("Title", Photo)]);

        Assert.Empty(media.Assets);
        Assert.Empty(media.Files);
    }

    [Fact]
    public async Task Pins_are_the_versions_recorded_for_the_page_version()
    {
        var pageVersionId = Guid.NewGuid();
        _library.Seed(Photo, Bytes("photo v1"));
        _library.AddVersion(Photo, Bytes("photo v2"));
        _library.Pins[(pageVersionId, Photo)] = 1;

        var pins = await Sut().PinsForAsync(pageVersionId, ImageWidget(Photo));

        var pin = Assert.Single(pins);
        Assert.Equal(Photo, pin.MediaId);
        Assert.Equal(1, pin.Version);
        Assert.Empty(await Sut().PinsForAsync(Guid.NewGuid(), "no images here"));
    }

    // ── import ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Import_adds_a_new_image_under_its_own_id_with_every_version_and_its_metadata()
    {
        var v1 = Bytes("photo v1");
        var v4 = Bytes("photo v4");
        var bundle = new ContentBundle
        {
            MediaAssets = [BundleAsset(Photo, (1, v1), (4, v4))],
            MediaFiles = Files(v1, v4)
        };
        var result = new ContentImportResult();

        var map = await Sut().ImportAsync(bundle, result);

        var created = _library.Assets[Photo];
        Assert.Equal("photo.png", created.FileName);
        Assert.Equal("A photo", created.DefaultAltText);
        Assert.Equal("Caption", created.Caption);
        Assert.Equal("Credit", created.Credit);
        Assert.Equal(["one", "two"], created.Tags);
        Assert.Equal(2, created.CurrentVersion);
        Assert.Equal(v1, _library.Blobs[MediaBlobKeys.Original(Photo, 1)]);
        Assert.Equal(v4, _library.Blobs[MediaBlobKeys.Original(Photo, 2)]);
        Assert.Equal(1, map.TargetVersion(Photo, 1));
        Assert.Equal(2, map.TargetVersion(Photo, 4));
        Assert.Equal(1, result.MediaAssetsCreated);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task Import_reuses_an_image_whose_id_already_holds_the_same_file()
    {
        var v1 = Bytes("photo v1");
        _library.Seed(Photo, v1);
        _library.Assets[Photo] = _library.Assets[Photo] with { DefaultAltText = "Target alt" };
        var blobsBefore = _library.Blobs.Count;
        var bundle = new ContentBundle { MediaAssets = [BundleAsset(Photo, (7, v1))], MediaFiles = Files(v1) };
        var result = new ContentImportResult();

        var map = await Sut().ImportAsync(bundle, result);

        Assert.Equal(1, map.TargetVersion(Photo, 7));
        Assert.Equal(blobsBefore, _library.Blobs.Count);
        Assert.Equal(1, _library.Assets[Photo].CurrentVersion);
        Assert.Equal("Target alt", _library.Assets[Photo].DefaultAltText);
        Assert.Equal(1, result.MediaAssetsReused);
        Assert.Equal(0, _cleaner.Calls);
    }

    [Fact]
    public async Task Import_adds_a_new_version_when_the_id_exists_with_a_different_file()
    {
        _library.Seed(Photo, Bytes("target file"));
        var incoming = Bytes("bundle file");
        var bundle = new ContentBundle { MediaAssets = [BundleAsset(Photo, (1, incoming))], MediaFiles = Files(incoming) };
        var result = new ContentImportResult();

        var map = await Sut().ImportAsync(bundle, result);

        Assert.Equal(2, _library.Assets[Photo].CurrentVersion);
        Assert.Equal(2, map.TargetVersion(Photo, 1));
        Assert.Equal(incoming, _library.Blobs[MediaBlobKeys.Original(Photo, 2)]);
        Assert.Equal(1, result.MediaAssetsUpdated);
    }

    [Fact]
    public async Task Import_reuses_an_older_matching_version_but_makes_the_bundle_current_file_current()
    {
        var old = Bytes("old");
        var current = Bytes("current");
        _library.Seed(Photo, old);
        _library.AddVersion(Photo, current);
        _library.AddVersion(Photo, Bytes("target edit"));
        var bundle = new ContentBundle
        {
            MediaAssets = [BundleAsset(Photo, (1, old), (2, current))],
            MediaFiles = Files(old, current)
        };

        var map = await Sut().ImportAsync(bundle, new ContentImportResult());

        Assert.Equal(1, map.TargetVersion(Photo, 1));
        Assert.Equal(4, map.TargetVersion(Photo, 2));
        Assert.Equal(4, _library.Assets[Photo].CurrentVersion);
        Assert.Equal(current, _library.Blobs[MediaBlobKeys.Original(Photo, 4)]);
    }

    [Fact]
    public async Task Import_stores_the_cleaned_file_under_its_own_hash_and_a_second_import_reuses_it()
    {
        // A JPEG re-encoded on the way in no longer matches the hash it was exported with. That
        // is expected: the stored hash is always the hash of the stored bytes.
        _cleaner.Transform = bytes => [.. bytes, (byte)'!'];
        var incoming = Bytes("jpeg");
        var bundle = new ContentBundle { MediaAssets = [BundleAsset(Photo, (1, incoming))], MediaFiles = Files(incoming) };

        var first = new ContentImportResult();
        await Sut().ImportAsync(bundle, first);
        var second = new ContentImportResult();
        await Sut().ImportAsync(bundle, second);

        var stored = _library.Blobs[MediaBlobKeys.Original(Photo, 1)];
        Assert.Equal(Bytes("jpeg!"), stored);
        Assert.Equal(Sha(stored), _library.Versions[(Photo, 1)].Sha256);
        Assert.Equal(1, _library.Assets[Photo].CurrentVersion);
        Assert.Equal(1, first.MediaAssetsCreated);
        Assert.Equal(1, second.MediaAssetsReused);
        Assert.Empty(first.Warnings);
        Assert.Empty(second.Warnings);
    }

    [Fact]
    public async Task Import_takes_type_and_size_from_the_cleaned_file_not_the_bundle()
    {
        var incoming = Bytes("svg");
        var asset = BundleAsset(Photo, (1, incoming));
        asset.Versions[0] = asset.Versions[0] with { ContentType = "text/html", Width = 99999, IsAnimated = true };

        await Sut().ImportAsync(new ContentBundle { MediaAssets = [asset], MediaFiles = Files(incoming) }, new ContentImportResult());

        var stored = _library.Versions[(Photo, 1)];
        Assert.Equal("image/png", stored.ContentType);
        Assert.Equal(2, stored.Width);
        Assert.False(stored.IsAnimated);
    }

    [Fact]
    public async Task Import_refuses_a_file_that_does_not_match_its_hash_and_says_so()
    {
        var bundle = new ContentBundle
        {
            MediaAssets = [BundleAsset(Photo, (1, Bytes("declared")))],
            MediaFiles = [new MediaFileBundleItem { Sha256 = Sha(Bytes("declared")), Data = Bytes("swapped") }]
        };
        var result = new ContentImportResult();

        await Sut().ImportAsync(bundle, result);

        Assert.False(_library.Assets.ContainsKey(Photo));
        Assert.Contains(result.Warnings, w => w.Contains(Photo.ToString()) && w.Contains("does not match"));
    }

    [Fact]
    public async Task Import_refuses_a_file_the_upload_checks_refuse_and_says_why()
    {
        var bad = Bytes("BAD file");
        var bundle = new ContentBundle { MediaAssets = [BundleAsset(Photo, (1, bad))], MediaFiles = Files(bad) };
        var result = new ContentImportResult();

        await Sut().ImportAsync(bundle, result);

        Assert.False(_library.Assets.ContainsKey(Photo));
        Assert.Contains(result.Warnings, w => w.Contains("photo.png") && w.Contains("Not an image"));
    }

    [Fact]
    public async Task Import_warns_when_a_version_has_no_file_in_the_bundle()
    {
        var v1 = Bytes("v1");
        var bundle = new ContentBundle { MediaAssets = [BundleAsset(Photo, (1, v1), (2, Bytes("v2")))], MediaFiles = Files(v1) };
        var result = new ContentImportResult();

        var map = await Sut().ImportAsync(bundle, result);

        Assert.Equal(1, map.TargetVersion(Photo, 1));
        Assert.Null(map.TargetVersion(Photo, 2));
        Assert.Contains(result.Warnings, w => w.Contains("photo.png") && w.Contains("no file"));
    }

    [Fact]
    public async Task Import_warns_about_content_that_uses_an_image_in_neither_the_bundle_nor_the_library()
    {
        _library.Seed(Photo, Bytes("here already"));
        var bundle = new ContentBundle
        {
            PageNodes = [Page("Landing page", Version(1, ImageWidget(Missing) + ""), Version(2, ImageWidget(Photo)))],
            ContentBlocks = [Block("Content", Missing)]
        };
        var result = new ContentImportResult();

        var map = await Sut().ImportAsync(bundle, result);

        var warning = Assert.Single(result.Warnings);
        Assert.Contains("Landing page", warning);
        Assert.Contains(Missing.ToString(), warning);
        Assert.Null(map.TargetVersion(Missing, 1));
    }

    [Fact]
    public async Task Import_of_a_bundle_without_media_changes_nothing()
    {
        var bundle = new ContentBundle { PageNodes = [Page("Plain", Version(1, "[]"))] };
        var result = new ContentImportResult();

        var map = await Sut().ImportAsync(bundle, result);

        Assert.Empty(_library.Assets);
        Assert.Empty(result.Warnings);
        Assert.Empty(map.MapPins([new MediaPinBundleItem { MediaId = Photo, Version = 1 }]));
    }

    [Fact]
    public async Task Import_removes_stale_files_left_under_a_new_image_id_before_storing()
    {
        // Renditions cached for an image deleted earlier would otherwise be served for the new file.
        _library.Blobs[MediaBlobKeys.Original(Photo, 1)] = Bytes("orphan");
        _library.Blobs[MediaBlobKeys.Rendition(Photo, 1, "abc")] = Bytes("stale rendition");
        var incoming = Bytes("new");

        await Sut().ImportAsync(
            new ContentBundle { MediaAssets = [BundleAsset(Photo, (1, incoming))], MediaFiles = Files(incoming) },
            new ContentImportResult());

        Assert.Equal(incoming, _library.Blobs[MediaBlobKeys.Original(Photo, 1)]);
        Assert.False(_library.Blobs.ContainsKey(MediaBlobKeys.Rendition(Photo, 1, "abc")));
    }

    [Fact]
    public async Task Import_cuts_metadata_to_the_library_limits_and_makes_a_safe_file_name()
    {
        var incoming = Bytes("v1");
        var asset = BundleAsset(Photo, (1, incoming)) with
        {
            FileName = "../../etc/" + new string('a', 400) + ".png",
            DefaultAltText = new string('x', 5000),
            Tags = [.. Enumerable.Range(0, 40).Select(i => $"tag{i}"), new string('t', 300)]
        };

        await Sut().ImportAsync(new ContentBundle { MediaAssets = [asset], MediaFiles = Files(incoming) }, new ContentImportResult());

        var created = _library.Assets[Photo];
        Assert.DoesNotContain("/", created.FileName);
        Assert.True(created.FileName.Length <= 255);
        Assert.EndsWith(".png", created.FileName);
        Assert.Equal(1000, created.DefaultAltText!.Length);
        Assert.True(created.Tags.Count <= 20);
        Assert.All(created.Tags, t => Assert.True(t.Length <= 100));
    }

    // ── bundle format ────────────────────────────────────────────────────────

    [Fact]
    public void A_bundle_written_before_media_existed_reads_with_no_media()
    {
        const string json = """
            {"$schema":"cpd-content-v2","schemaVersion":2,
             "pageNodes":[{"id":"aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa","segment":"a","title":"A","pageType":"content",
               "versions":[{"versionId":1,"content":"[]"}]}],
             "contentBlocks":[]}
            """;

        var bundle = ContentStagingJson.Deserialize(json)!;

        Assert.Empty(bundle.MediaAssets);
        Assert.Empty(bundle.MediaFiles);
        Assert.Empty(bundle.PageNodes[0].Versions[0].Media);
    }

    [Fact]
    public void Media_files_are_written_as_base64_and_read_back_byte_for_byte()
    {
        byte[] data = [0, 1, 2, 250, 255];
        var bundle = new ContentBundle
        {
            MediaAssets = [BundleAsset(Photo, (1, data))],
            MediaFiles = Files(data),
            PageNodes = [Page("P", Version(1, "[]", new MediaPinBundleItem { MediaId = Photo, Version = 1 }))]
        };

        var json = ContentStagingJson.Serialize(bundle);
        var back = ContentStagingJson.Deserialize(json)!;

        Assert.Contains($"\"data\": \"{Convert.ToBase64String(data)}\"", json);
        Assert.Equal(data, Assert.Single(back.MediaFiles).Data);
        Assert.Equal(Photo, Assert.Single(back.MediaAssets).Id);
        Assert.Equal(1, Assert.Single(back.PageNodes[0].Versions[0].Media).Version);
    }

    [Fact]
    public void Null_media_members_read_as_empty()
    {
        const string json = """
            {"$schema":"cpd-content-v2","schemaVersion":2,"mediaAssets":null,"mediaFiles":[null,{"sha256":null,"data":null}],
             "pageNodes":[{"id":"aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa","segment":"a","title":"A","pageType":"content",
               "versions":[{"versionId":1,"content":"[]","media":null}]}]}
            """;

        var bundle = ContentStagingJson.Deserialize(json)!;

        Assert.Empty(bundle.MediaAssets);
        var file = Assert.Single(bundle.MediaFiles);
        Assert.Equal("", file.Sha256);
        Assert.Empty(file.Data);
        Assert.Empty(bundle.PageNodes[0].Versions[0].Media);
    }

    // ── fakes ────────────────────────────────────────────────────────────────

    // Accepts any file not starting "BAD" as a 2 x 1 PNG, after an optional change to its bytes.
    private sealed class FakeCleaner : IMediaFileCleaner
    {
        public Func<byte[], byte[]> Transform { get; set; } = b => b;
        public int Calls { get; private set; }

        public Task<MediaCheckResult> CleanAsync(Stream content, CancellationToken ct = default)
        {
            Calls++;
            using var buffer = new MemoryStream();
            content.CopyTo(buffer);
            var bytes = buffer.ToArray();
            return Task.FromResult(Encoding.UTF8.GetString(bytes).StartsWith("BAD")
                ? MediaCheckResult.Reject("Not an image.")
                : MediaCheckResult.Accept(new CleanedMediaFile(Transform(bytes), MediaFormat.Png, 2, 1, false)));
        }
    }

    // The library rows and blob store, in memory.
    private sealed class FakeMediaLibrary : IMediaAssetRepository, IMediaBlobStore
    {
        public Dictionary<Guid, MediaAssetDto> Assets { get; } = [];
        public Dictionary<(Guid, int), MediaAssetVersionDto> Versions { get; } = [];
        public Dictionary<string, byte[]> Blobs { get; } = [];
        public Dictionary<(Guid PageVersion, Guid Asset), int> Pins { get; } = [];

        public MediaAssetDto Seed(Guid id, byte[] bytes)
        {
            var version = Store(id, 1, bytes, 20, 10);
            var asset = new MediaAssetDto(id, $"{id:N}.png", "slug", "Alt text", "A caption", "A credit", ["tag"], 1, version,
                DateTime.UtcNow, "seed", DateTime.UtcNow, "seed");
            Assets[id] = asset;
            return asset;
        }

        public void AddVersion(Guid id, byte[] bytes)
        {
            var next = Assets[id].CurrentVersion + 1;
            var version = Store(id, next, bytes, 20, 10);
            Assets[id] = Assets[id] with { CurrentVersion = next, Latest = version };
        }

        private MediaAssetVersionDto Store(Guid id, int number, byte[] bytes, int? width, int? height)
        {
            var key = MediaBlobKeys.Original(id, number);
            Blobs[key] = bytes;
            var version = new MediaAssetVersionDto(id, number, key, "image/png", width, height, bytes.Length, Sha(bytes), false, DateTime.UtcNow, "seed");
            Versions[(id, number)] = version;
            return version;
        }

        private MediaAssetVersionDto Row(Guid id, int number, NewMediaAssetVersion file)
        {
            var version = new MediaAssetVersionDto(id, number, file.BlobKey, file.ContentType, file.Width, file.Height,
                file.SizeBytes, file.Sha256, file.IsAnimated, DateTime.UtcNow, "import");
            Versions[(id, number)] = version;
            return version;
        }

        public Task<MediaAssetDto> CreateAsync(NewMediaAsset asset, CancellationToken ct = default)
        {
            if (Assets.ContainsKey(asset.Id)) throw new InvalidOperationException("taken");
            var first = Row(asset.Id, 1, asset.FirstVersion);
            var created = new MediaAssetDto(asset.Id, asset.FileName, asset.Slug, asset.Metadata.DefaultAltText,
                asset.Metadata.Caption, asset.Metadata.Credit, asset.Metadata.Tags, 1, first,
                DateTime.UtcNow, "import", DateTime.UtcNow, "import");
            Assets[asset.Id] = created;
            return Task.FromResult(created);
        }

        public Task<MediaAssetVersionDto> AddVersionAsync(Guid assetId, int version, NewMediaAssetVersion file, CancellationToken ct = default)
        {
            if (!Assets.TryGetValue(assetId, out var asset)) throw new KeyNotFoundException();
            if (version != asset.CurrentVersion + 1) throw new MediaVersionConflictException(assetId, version);
            var row = Row(assetId, version, file);
            Assets[assetId] = asset with { CurrentVersion = version, Latest = row };
            return Task.FromResult(row);
        }

        public Task<MediaAssetDto?> GetAsync(Guid assetId, CancellationToken ct = default) =>
            Task.FromResult(Assets.GetValueOrDefault(assetId));

        public Task<IReadOnlyList<MediaAssetDto>> GetManyAsync(IReadOnlyCollection<Guid> assetIds, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<MediaAssetDto>>(assetIds.Distinct().Where(Assets.ContainsKey).Select(id => Assets[id]).ToList());

        public Task<IReadOnlyList<MediaAssetVersionDto>> GetPinnedVersionsAsync(
            Guid pageNodeVersionId, IReadOnlyCollection<Guid> assetIds, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<MediaAssetVersionDto>>(assetIds
                .Where(id => Pins.ContainsKey((pageNodeVersionId, id)))
                .Select(id => Versions[(id, Pins[(pageNodeVersionId, id)])])
                .ToList());

        public Task<MediaAssetVersionDto?> GetVersionAsync(Guid assetId, int version, CancellationToken ct = default) =>
            Task.FromResult(Versions.GetValueOrDefault((assetId, version)));

        public Task<IReadOnlyList<MediaAssetVersionDto>> GetVersionsAsync(Guid assetId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<MediaAssetVersionDto>>(Versions.Values
                .Where(v => v.AssetId == assetId).OrderByDescending(v => v.Version).ToList());

        public Task<MediaAssetPage> ListAsync(MediaAssetQuery query, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> UpdateMetadataAsync(Guid assetId, MediaAssetMetadata metadata, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<MediaAssetDto>> FindBySha256Async(string sha256, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<MediaUsagePage>> GetUsagePagesAsync(Guid assetId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<MediaUsageBlock>> GetUsageBlocksAsync(Guid assetId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<MediaDeleteOutcome> DeleteAsync(Guid assetId, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<bool> PutAsync(string key, Stream content, string contentType, CancellationToken ct = default)
        {
            if (Blobs.ContainsKey(key)) return Task.FromResult(false);
            using var buffer = new MemoryStream();
            content.CopyTo(buffer);
            Blobs[key] = buffer.ToArray();
            return Task.FromResult(true);
        }

        public Task<MediaBlob?> GetAsync(string key, CancellationToken ct = default) =>
            Task.FromResult(Blobs.TryGetValue(key, out var bytes)
                ? new MediaBlob(new MemoryStream(bytes), "image/png", bytes.Length)
                : null);

        public Task<bool> ExistsAsync(string key, CancellationToken ct = default) => Task.FromResult(Blobs.ContainsKey(key));

        public Task<bool> DeleteAsync(string key, CancellationToken ct = default) => Task.FromResult(Blobs.Remove(key));

        public Task<int> DeleteByPrefixAsync(string prefix, CancellationToken ct = default)
        {
            var keys = Blobs.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList();
            foreach (var key in keys) Blobs.Remove(key);
            return Task.FromResult(keys.Count);
        }

        public Task<IReadOnlyList<MediaBlobEntry>> ListAsync(string prefix, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<MediaBlobEntry>>(Blobs
                .Where(b => b.Key.StartsWith(prefix, StringComparison.Ordinal))
                .Select(b => new MediaBlobEntry(b.Key, b.Value.Length))
                .ToList());
    }
}
