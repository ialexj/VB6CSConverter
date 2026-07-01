using System;
using System.Collections.Generic;
using System.Linq;

namespace VB6Parser.Frx;

/// <summary>
/// Schema-free decoder for the MS-OLEPS "Property Set" binary stream format
/// (<c>IPropertyStorage</c> / <c>PropertySetStream</c>), as embedded in an
/// <see cref="FrxOleObjectBlob"/> payload starting immediately after its 24-byte "LB" header.
/// </summary>
/// <remarks>
/// Only the first property set (the <c>NumPropertySets == 1</c> case) is parsed; a second,
/// Office-style "user properties" set is ignored — it has not been observed in VB6 control
/// blobs. Property IDs are returned as-is; the Dictionary (property ID 0) is not resolved to
/// names, matching every other property's raw id/type/value shape.
///
/// Known scalar types are decoded to native CLR values. Anything else (VT_VARIANT, vectors,
/// arrays, blobs, unknown codes) is preserved as the raw <see cref="byte"/>[] of its bounded
/// byte range — <see cref="System.Text.Json.JsonSerializer"/> serializes <see cref="byte"/>[]
/// as a base64 string by default, so no manual encoding is needed.
///
/// Any structural mismatch (bad byte-order mark, out-of-range offsets, truncated data) returns
/// <see langword="false"/> — this never throws, so callers can safely fall back to raw byte
/// preservation.
/// </remarks>
public static class MsOlePropertySetReader
{
    private static readonly Dictionary<int, string> TypeNames = new() {
        [0] = "VT_EMPTY", [1] = "VT_NULL", [2] = "VT_I2", [3] = "VT_I4",
        [4] = "VT_R4", [5] = "VT_R8", [6] = "VT_CY", [7] = "VT_DATE",
        [8] = "VT_BSTR", [9] = "VT_DISPATCH", [10] = "VT_ERROR", [11] = "VT_BOOL",
        [12] = "VT_VARIANT", [13] = "VT_UNKNOWN", [14] = "VT_DECIMAL",
        [16] = "VT_I1", [17] = "VT_UI1", [18] = "VT_UI2", [19] = "VT_UI4",
        [20] = "VT_I8", [21] = "VT_UI8", [22] = "VT_INT", [23] = "VT_UINT",
        [24] = "VT_VOID", [25] = "VT_HRESULT", [26] = "VT_PTR", [27] = "VT_SAFEARRAY",
        [28] = "VT_CARRAY", [29] = "VT_USERDEFINED", [30] = "VT_LPSTR", [31] = "VT_LPWSTR",
        [64] = "VT_FILETIME", [65] = "VT_BLOB", [66] = "VT_STREAM", [67] = "VT_STORAGE",
        [68] = "VT_STREAMED_OBJECT", [69] = "VT_STORED_OBJECT", [70] = "VT_BLOB_OBJECT",
        [71] = "VT_CF", [72] = "VT_CLSID", [73] = "VT_VERSIONED_STREAM",
    };

    private const int VT_VECTOR = 0x1000;
    private const int VT_ARRAY = 0x2000;

    // CP1252 is not available by default in .NET; register the provider once.
    private static readonly System.Text.Encoding Cp1252 = GetCp1252();
    private static System.Text.Encoding GetCp1252()
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        return System.Text.Encoding.GetEncoding(1252);
    }

    /// <summary>
    /// Attempts to parse an MS-OLEPS <c>PropertySetStream</c> from <paramref name="data"/>.
    /// <paramref name="version"/> receives the stream's own internal format version (distinct
    /// from the caller's outer "LB" blob version). Returns <see langword="false"/> on any
    /// structural mismatch — never throws.
    /// </summary>
    public static bool TryParse(byte[] data, out short version, out FrxOleObjectBlobProperty[]? properties)
    {
        version = 0;
        properties = null;

        try {
            // ── PropertySetStream header (48 bytes when NumPropertySets == 1) ──────
            if (data.Length < 48) return false;

            var byteOrder = BitConverter.ToUInt16(data, 0);
            if (byteOrder != 0xFFFE) return false;

            version = BitConverter.ToInt16(data, 2);
            // SystemIdentifier @4 (4 bytes), CLSID @8 (16 bytes) — not needed here.

            var numPropertySets = BitConverter.ToUInt32(data, 24);
            if (numPropertySets is not (1 or 2)) return false;

            // FMTID0 @28 (16 bytes) — not needed; Offset0 @44 (4 bytes, relative to stream start).
            var offset0 = BitConverter.ToInt32(data, 44);
            if (offset0 < 0 || offset0 + 8 > data.Length) return false;

            // ── PropertySet section (first set only) ───────────────────────────────
            var size = BitConverter.ToInt32(data, offset0);
            var numProperties = BitConverter.ToInt32(data, offset0 + 4);
            if (size < 8 || numProperties < 0) return false;

            var sectionEnd = offset0 + size;
            if (sectionEnd > data.Length) return false;

            var directoryEnd = offset0 + 8 + (numProperties * 8);
            if (directoryEnd > sectionEnd) return false;

            var entries = new (int Id, int Offset)[numProperties];
            for (var i = 0; i < numProperties; i++) {
                var entryPos = offset0 + 8 + (i * 8);
                var id = BitConverter.ToInt32(data, entryPos);
                var relativeOffset = BitConverter.ToInt32(data, entryPos + 4);
                if (relativeOffset < 0) return false;
                entries[i] = (id, offset0 + relativeOffset);
            }

            // Sort absolute offsets to bound each property's byte range by its successor —
            // mirrors the FRX-item "sort offsets, diff to next" convention used for
            // FRM-referenced FRX byte lengths (see docs/frx.md "FRM syntax").
            var sortedOffsets = entries.Select(e => e.Offset).OrderBy(o => o).ToArray();

            var result = new FrxOleObjectBlobProperty[numProperties];
            for (var i = 0; i < numProperties; i++) {
                var (id, propOffset) = entries[i];
                if (propOffset < 0 || propOffset + 4 > data.Length) return false;

                var rawType = BitConverter.ToInt32(data, propOffset);
                var baseType = rawType & ~(VT_VECTOR | VT_ARRAY);
                var typeName = TypeNames.TryGetValue(baseType, out var name) ? name : $"VT_UNKNOWN_{baseType}";
                if ((rawType & VT_VECTOR) != 0) typeName = $"VT_VECTOR|{typeName}";
                if ((rawType & VT_ARRAY) != 0) typeName = $"VT_ARRAY|{typeName}";

                var valueOffset = propOffset + 4;
                var nextOffset = sortedOffsets.Where(o => o > propOffset).DefaultIfEmpty(sectionEnd).Min();
                var rangeEnd = Math.Min(nextOffset, sectionEnd);

                object? value = null;
                if (rawType == baseType) {
                    value = DecodeScalar(baseType, data, valueOffset, rangeEnd);
                }
                value ??= RawRange(data, valueOffset, rangeEnd);

                result[i] = new FrxOleObjectBlobProperty(id, typeName, value);
            }

            properties = result;
            return true;
        }
        catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentOutOfRangeException or OverflowException) {
            return false;
        }
    }

    // ── Scalar decoding ─────────────────────────────────────────────────────────

    private static object? DecodeScalar(int type, byte[] data, int offset, int rangeEnd)
    {
        switch (type) {
            case 2: // VT_I2
                if (offset + 2 > data.Length) return null;
                return (int)BitConverter.ToInt16(data, offset);

            case 3:  // VT_I4
            case 22: // VT_INT
                if (offset + 4 > data.Length) return null;
                return BitConverter.ToInt32(data, offset);

            case 4: // VT_R4
                if (offset + 4 > data.Length) return null;
                return BitConverter.ToSingle(data, offset);

            case 5: // VT_R8
                if (offset + 8 > data.Length) return null;
                return BitConverter.ToDouble(data, offset);

            case 11: // VT_BOOL — VARIANT_BOOL: 0xFFFF = true, 0x0000 = false
                if (offset + 2 > data.Length) return null;
                return BitConverter.ToInt16(data, offset) != 0;

            case 8:  // VT_BSTR (CodePageString)
            case 30: // VT_LPSTR (CodePageString)
                return DecodeCodePageString(data, offset, rangeEnd);

            case 31: // VT_LPWSTR (UnicodeString)
                return DecodeUnicodeString(data, offset, rangeEnd);

            default:
                return null; // caller falls back to the raw byte range
        }
    }

    private static string? DecodeCodePageString(byte[] data, int offset, int rangeEnd)
    {
        if (offset + 4 > data.Length) return null;
        var byteCount = BitConverter.ToInt32(data, offset);
        if (byteCount < 0 || offset + 4 + byteCount > data.Length) return null;
        // Sanity bound: a genuine string shouldn't spill past its neighbour's start
        // (plus a little slack for 4-byte alignment padding).
        if (offset + 4 + byteCount > rangeEnd + 4) return null;

        return Cp1252.GetString(data, offset + 4, byteCount).TrimEnd('\0');
    }

    private static string? DecodeUnicodeString(byte[] data, int offset, int rangeEnd)
    {
        if (offset + 4 > data.Length) return null;
        var charCount = BitConverter.ToInt32(data, offset);
        if (charCount < 0) return null;

        var byteCount = charCount * 2;
        if (offset + 4 + byteCount > data.Length) return null;
        if (offset + 4 + byteCount > rangeEnd + 4) return null;

        return System.Text.Encoding.Unicode.GetString(data, offset + 4, byteCount).TrimEnd('\0');
    }

    private static byte[] RawRange(byte[] data, int start, int end)
    {
        start = Math.Max(0, Math.Min(start, data.Length));
        end = Math.Max(start, Math.Min(end, data.Length));
        return data[start..end];
    }
}
