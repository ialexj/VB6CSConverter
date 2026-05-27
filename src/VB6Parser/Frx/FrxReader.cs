using System;
using System.IO;
using System.Text;

namespace VB6Parser.Frx;

/// <summary>
/// Reads a single FRX item from a .frx file by absolute offset and known byte length.
/// The byte length must be derived by the caller by sorting all offsets referenced in
/// the .frm file and computing the difference between consecutive entries.
/// </summary>
public static class FrxReader
{
    // Magic marker that identifies an image payload inside a BinaryBlob.
    private static readonly byte[] ImageMagic = [0x6C, 0x74, 0x00, 0x00];

    // CP1252 is not available by default in .NET; register the provider once.
    private static readonly System.Text.Encoding Cp1252 = GetCp1252();
    private static System.Text.Encoding GetCp1252()
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        return System.Text.Encoding.GetEncoding(1252);
    }

    /// <summary>
    /// Reads <paramref name="length"/> bytes at <paramref name="offset"/> from
    /// <paramref name="frxPath"/> and parses them to the most specific
    /// <see cref="FrxItem"/> subtype possible.
    /// </summary>
    public static FrxItem Read(string frxPath, int offset, int length)
    {
        ArgumentNullException.ThrowIfNull(frxPath);
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));

        var filename = Path.GetFileName(frxPath);
        var data = ReadBytes(frxPath, offset, length);

        // Edge case: empty range
        if (length == 0)
            return new FrxRawItem(filename, offset, length, data);

        // ── 1. Attempt BinaryBlob ─────────────────────────────────────────────
        if (length >= 4) {
            var candidate = BitConverter.ToInt32(data, 0);
            if (candidate == length - 4) {
                var payload = ParsePayload(data, length);
                return new FrxBinaryBlob(filename, offset, length, candidate, payload);
            }
        }

        // ── 2. Attempt StringList ─────────────────────────────────────────────
        if (TryParseStringList(data, length, out var strings))
            return new FrxStringList(filename, offset, length, strings!);

        // ── 3. Fallback: raw bytes ────────────────────────────────────────────
        return new FrxRawItem(filename, offset, length, data);
    }

    // ── Payload parsing ───────────────────────────────────────────────────────

    private static IFrxBinaryPayload ParsePayload(byte[] data, int byteLength)
    {
        // Payload starts at byte 4 (after the 4-byte length prefix).
        // Check for image magic at payload[0..3] (no CLSID).
        if (PayloadHasMagic(data, payloadOffset: 4)) {
            // No CLSID: magic at +4, imageLength at +8, image data at +12
            var imgLen = BitConverter.ToInt32(data, 8);
            if (imgLen == byteLength - 12 && imgLen >= 0) {
                var imgBytes = data[12..(12 + imgLen)];
                return new FrxImagePayload(imgLen, imgBytes, ClsId: null);
            }
        }

        // Check for image magic at payload[16..19] (preceded by 16-byte CLSID).
        if (data.Length >= 4 + 20 && PayloadHasMagic(data, payloadOffset: 20)) {
            // CLSID at +4..+19, magic at +20, imageLength at +24, image data at +28
            var clsidBytes = data[4..20];
            var clsId = new Guid(clsidBytes);
            var imgLen = BitConverter.ToInt32(data, 24);
            if (imgLen == byteLength - 28 && imgLen >= 0) {
                var imgBytes = data[28..(28 + imgLen)];
                return new FrxImagePayload(imgLen, imgBytes, clsId);
            }
        }

        // Unknown / opaque payload — return raw bytes (exclude the 4-byte length prefix)
        return new FrxRawPayload(data[4..]);
    }

    private static bool PayloadHasMagic(byte[] data, int payloadOffset)
    {
        if (data.Length < payloadOffset + 4) return false;
        return data[payloadOffset]     == ImageMagic[0]
            && data[payloadOffset + 1] == ImageMagic[1]
            && data[payloadOffset + 2] == ImageMagic[2]
            && data[payloadOffset + 3] == ImageMagic[3];
    }

    // ── StringList parsing ────────────────────────────────────────────────────

    private static bool TryParseStringList(byte[] data, int byteLength, out string[] strings)
    {
        strings = null;

        // Minimum: 2 bytes for count
        if (byteLength < 2) return false;

        var count = BitConverter.ToInt16(data, 0);
        if (count < 0) return false;

        // Empty list — valid only if byteLength == 2
        if (count == 0) {
            if (byteLength == 2) {
                strings = [];
                return true;
            }
            return false;
        }

        // At least 4 bytes needed: count (2) + maxItemLength (2)
        if (byteLength < 4) return false;
        // maxItemLength is a hint only — read and discard
        // int16 maxItemLength = BitConverter.ToInt16(data, 2);

        // CP1252 encoding — provider registered via static field above.
        var result = new string[count];
        var pos = 4; // skip count + maxItemLength

        for (var i = 0; i < count; i++) {
            // Need 2 bytes for item length
            if (pos + 2 > byteLength) return false;

            var itemLen = BitConverter.ToInt16(data, pos);
            if (itemLen < 0) return false;
            pos += 2;

            // Need itemLen bytes for item data
            if (pos + itemLen > byteLength) return false;

            result[i] = Cp1252.GetString(data, pos, itemLen);
            pos += itemLen;
        }

        strings = result;
        return true;
    }

    // ── File I/O ──────────────────────────────────────────────────────────────

    private static byte[] ReadBytes(string path, int offset, int length)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        fs.Seek(offset, SeekOrigin.Begin);
        var buf = new byte[length];
        var read = 0;
        while (read < length) {
            var n = fs.Read(buf, read, length - read);
            if (n == 0) break;
            read += n;
        }
        return buf;
    }
}
