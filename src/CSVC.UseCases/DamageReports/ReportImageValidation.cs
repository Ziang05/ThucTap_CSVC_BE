using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace CSVC.UseCases.DamageReports;

internal static class ReportImageValidation
{
    public static string? Detect(byte[] bytes)
    {
        try
        {
            if (bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return Png(bytes) ? "image/png" : null;
            return Jpeg(bytes) ? "image/jpeg" : null;
        }
        catch (Exception e) when (e is InvalidDataException or IOException or OverflowException or ArgumentException) { return null; }
    }
    private static bool Png(byte[] bytes)
    {
        var offset = 8; var header = false; var end = false; long maxDecoded = 0;
        using var compressed = new MemoryStream();
        while (offset <= bytes.Length - 12)
        {
            var size = checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4)));
            if (size > bytes.Length - offset - 12) return false;
            var kind = Encoding.ASCII.GetString(bytes, offset + 4, 4);
            var chunk = bytes.AsSpan(offset + 4, size + 4);
            if (Crc(chunk) != BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset + 8 + size, 4))) return false;
            if (!header && kind != "IHDR") return false;
            if (kind == "IHDR")
            {
                if (header || size != 13) return false;
                var data = bytes.AsSpan(offset + 8, size);
                var w = BinaryPrimitives.ReadUInt32BigEndian(data); var h = BinaryPrimitives.ReadUInt32BigEndian(data[4..]);
                if (w == 0 || h == 0 || w > 12000 || h > 12000 || (long)w * h > 20000000 || data[10] != 0 || data[11] != 0 || data[12] > 1) return false;
                if (data[8] is not (1 or 2 or 4 or 8 or 16) || data[9] is not (0 or 2 or 3 or 4 or 6)) return false;
                maxDecoded = ((long)w * 8 + 8) * (h + 8); header = true;
            }
            else if (kind == "IDAT") compressed.Write(bytes, offset + 8, size);
            else if (kind == "IEND") { if (size != 0) return false; end = true; offset += 12; break; }
            else if (kind is "acTL" or "fcTL" or "fdAT") return false;
            offset += size + 12;
        }
        if (!header || !end || offset != bytes.Length || compressed.Length == 0) return false;
        compressed.Position = 0;
        using var decoder = new ZLibStream(compressed, CompressionMode.Decompress);
        var buffer = new byte[8192]; long decoded = 0; int count;
        while ((count = decoder.Read(buffer)) > 0) { decoded += count; if (decoded > maxDecoded) return false; }
        return decoded > 0;
    }
    private static uint Crc(ReadOnlySpan<byte> data)
    {
        uint crc = 0xffffffff;
        foreach (var b in data) { crc ^= b; for (var i = 0; i < 8; i++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0); }
        return ~crc;
    }
    private static bool Jpeg(byte[] data)
    {
        if (data[0] != 255 || data[1] != 216 || data[^2] != 255 || data[^1] != 217) return false;
        var p = 2; var dimensions = false; var scan = false;
        while (p < data.Length - 2)
        {
            if (data[p++] != 255) return false;
            while (p < data.Length && data[p] == 255) p++;
            if (p >= data.Length) return false;
            var marker = data[p++];
            if (marker == 217) return scan && dimensions && p == data.Length;
            if (marker == 0 || marker == 216) return false;
            if (marker is >= 208 and <= 215) continue;
            if (p > data.Length - 2) return false;
            var length = (data[p] << 8) + data[p + 1];
            if (length < 2 || length > data.Length - p) return false;
            if (marker is 192 or 193 or 194)
            {
                if (length < 8) return false;
                var h = (data[p + 3] << 8) + data[p + 4]; var w = (data[p + 5] << 8) + data[p + 6];
                if (w == 0 || h == 0 || w > 12000 || h > 12000 || (long)w * h > 20000000) return false;
                dimensions = true;
            }
            p += length;
            if (marker != 218) continue;
            scan = true;
            while (p < data.Length - 1)
            {
                if (data[p] != 255) { p++; continue; }
                var next = data[p + 1];
                if (next == 0 || next is >= 208 and <= 215) { p += 2; continue; }
                break;
            }
        }
        return dimensions && scan && p == data.Length - 2;
    }
}
