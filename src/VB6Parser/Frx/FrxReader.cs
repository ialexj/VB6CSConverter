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

    // Magic marker that identifies an RTF text item ("{\rtf1").
    private static readonly byte[] RtfMagic = [0x7B, 0x5C, 0x72, 0x74, 0x66, 0x31];

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

        // ── 0. Attempt Bindings (magic-based) ───────────────────────────────
        if (TryParseBindings(data, length, out var entries))
            return new FrxBindings(filename, offset, length, entries!);

        // ── 0.5 Attempt RTF text (magic-based) ──────────────────────────────
        if (length >= RtfMagic.Length && data.AsSpan(0, RtfMagic.Length).SequenceEqual(RtfMagic))
            return new FrxRtfText(filename, offset, length, data);

        // ── 0.7 Attempt OleObjectBlob (magic-based) ─────────────────────────
        if (TryParseOleObjectBlob(data, length, out var oleVersion, out var oleProperties))
            return new FrxOleObjectBlob(filename, offset, length, oleVersion, data, oleProperties!);

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

    // ── OleObjectBlob (LB magic) parsing ────────────────────────────────────────

    private static bool TryParseOleObjectBlob(byte[] data, int byteLength, out int version, out FrxOleObjectBlobProperty[]? properties)
    {
        version = 0;
        properties = null;

        // Header: "LB" magic (+0), int16 version (+2), int32 contentSize (+4).
        // contentSize must equal byteLength - 24 (the 24-byte LB header itself).
        if (byteLength < 24) return false;
        if (data[0] != 0x4C || data[1] != 0x42) return false;

        var blobVersion = BitConverter.ToInt16(data, 2);
        var contentSize = BitConverter.ToInt32(data, 4);
        if (contentSize != byteLength - 24) return false;

        // +8..+23: 16 bytes of control-specific position/size fields — not needed here.
        var olepsData = data[24..byteLength];
        if (!MsOlePropertySetReader.TryParse(olepsData, out _, out var parsedProperties)) return false;

        version = blobVersion;
        properties = parsedProperties;
        return true;
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

    private static bool TryParseBindings(byte[] data, int byteLength, out FrxBindingsEntry[] entries)
    {
        entries = null;

        // Header: C5/C6 FA N 00
        if (byteLength < 10) return false;
        if (data[1] != 0xFA || data[3] != 0x00) return false;
        if (data[0] != 0xC5 && data[0] != 0xC6) return false;

        var count = data[2];
        var pos = 4;
        var parsed = new FrxBindingsEntry[count];
        var totalNameLen = 0;

        for (var i = 0; i < count; i++) {
            // flags (int32) + nameLen (byte)
            if (pos + 5 > byteLength) return false;

            var flags = BitConverter.ToInt32(data, pos);
            pos += 4;

            var nameLen = data[pos];
            pos += 1;

            if (pos + nameLen > byteLength) return false;

            var name = Cp1252.GetString(data, pos, nameLen);
            pos += nameLen;
            totalNameLen += nameLen;

            parsed[i] = new FrxBindingsEntry(flags, name);
        }

        // Fixed 6-byte zero trailer.
        if (pos + 6 != byteLength) return false;
        for (var i = 0; i < 6; i++) {
            if (data[pos + i] != 0x00) return false;
        }

        // Exact size formula: 4 + 6 + N*5 + sum(nameLen)
        var expectedLength = 4 + 6 + (count * 5) + totalNameLen;
        if (expectedLength != byteLength) return false;

        entries = parsed;
        return true;
    }

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
