using System.IO.Compression;
using System.Text;

namespace DfE.CheckPerformanceData.E2ETests.Helpers;

// Image files for media library tests. The library refuses a second copy of the same cleaned
// file, and no test can delete an image, so each call makes a file no earlier run has made: a
// PNG or SVG filled with a random colour.
public static class MediaFixtures
{
    public static byte[] UniquePng(int width = 48, int height = 24)
    {
        var colour = Guid.NewGuid().ToByteArray();
        var raw = new MemoryStream();
        for (var y = 0; y < height; y++)
        {
            raw.WriteByte(0); // no filter
            for (var x = 0; x < width; x++)
            {
                // Two colours, so the image cannot clean down to the same bytes as another run.
                var offset = x < width / 2 ? 0 : 3;
                raw.Write(colour, offset, 3);
            }
        }

        var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var header = new byte[13];
        WriteBigEndian(header, 0, (uint)width);
        WriteBigEndian(header, 4, (uint)height);
        header[8] = 8; // bit depth
        header[9] = 2; // truecolour
        WriteChunk(png, "IHDR", header);
        using (var compressed = new MemoryStream())
        {
            using (var z = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            {
                raw.Position = 0;
                raw.CopyTo(z);
            }
            WriteChunk(png, "IDAT", compressed.ToArray());
        }
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    public static byte[] UniqueSvg()
    {
        var colour = Convert.ToHexString(Guid.NewGuid().ToByteArray(), 0, 3).ToLowerInvariant();
        return Encoding.UTF8.GetBytes(
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"40\" height=\"20\" viewBox=\"0 0 40 20\">"
            + $"<rect width=\"40\" height=\"20\" fill=\"#{colour}\"/></svg>");
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        var length = new byte[4];
        WriteBigEndian(length, 0, (uint)data.Length);
        stream.Write(length);
        var typeAndData = Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
        stream.Write(typeAndData);
        var crc = new byte[4];
        WriteBigEndian(crc, 0, Crc32(typeAndData));
        stream.Write(crc);
    }

    private static void WriteBigEndian(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    private static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var i = 0; i < 8; i++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }
        return crc ^ 0xFFFFFFFFu;
    }
}
