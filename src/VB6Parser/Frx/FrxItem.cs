using System;

namespace VB6Parser.Frx;

/// <summary>Base for all FRX items located by an offset in a .frx file.</summary>
public abstract record FrxItem(string Filename, int Offset, int Length)
{
    /// <summary>Returns the raw exportable data for this item (e.g. image bytes, raw blob bytes).</summary>
    public abstract byte[] GetPayloadData();
}

// ── Payload types for BinaryBlob ─────────────────────────────────────────────

public interface IFrxBinaryPayload
{
    byte[] GetData();
}

/// <summary>Unrecognised binary payload — preserved as-is.</summary>
public record FrxRawPayload(byte[] RawBytes) : IFrxBinaryPayload
{
    public byte[] GetData() => RawBytes;
}

/// <summary>
/// Image payload detected by the <c>6C 74 00 00</c> magic marker.
/// Optionally preceded by a 16-byte little-endian CLSID.
/// </summary>
public record FrxImagePayload(int ImageLength, byte[] ImageBytes, Guid? ClsId) : IFrxBinaryPayload
{
    public byte[] GetData() => ImageBytes;
}

/// <summary>Single Bindings entry: (flags, name).</summary>
public record FrxBindingsEntry(int Flags, string Name);

// ── FrxItem subclasses ────────────────────────────────────────────────────────

/// <summary>
/// Length-prefixed binary blob (first int32 == byteLength − 4).
/// The <see cref="Payload"/> is either an <see cref="FrxImagePayload"/> or an <see cref="FrxRawPayload"/>.
/// </summary>
public record FrxBinaryBlob(string Filename, int Offset, int Length, int DataLength, IFrxBinaryPayload Payload)
    : FrxItem(Filename, Offset, Length)
{
    public override byte[] GetPayloadData() => Payload.GetData();
}

/// <summary>CP1252-encoded string array (ListBox/ComboBox List/ItemData properties).</summary>
public record FrxStringList(string Filename, int Offset, int Length, string[] Strings)
    : FrxItem(Filename, Offset, Length)
{
    public override byte[] GetPayloadData()
    {
        if (Strings.Length == 0) return [];
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        return System.Text.Encoding.GetEncoding(1252).GetBytes(Strings[0]);
    }
}

/// <summary>
/// Data binding descriptor item (<c>C5/C6 FA N 00</c> magic) used by data-aware controls.
/// </summary>
public record FrxBindings(string Filename, int Offset, int Length, FrxBindingsEntry[] Entries)
    : FrxItem(Filename, Offset, Length)
{
    public string? DataSource => Entries.Length == 0 ? null : Entries[^1].Name;

    public string? DataField => Entries.Length >= 2 ? Entries[0].Name : null;

    public override byte[] GetPayloadData()
    {
        if (string.IsNullOrEmpty(DataSource)) return [];
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        return System.Text.Encoding.GetEncoding(1252).GetBytes(DataSource);
    }
}

/// <summary>Unrecognised item — raw bytes preserved for export.</summary>
public record FrxRawItem(string Filename, int Offset, int Length, byte[] Data)
    : FrxItem(Filename, Offset, Length)
{
    public override byte[] GetPayloadData() => Data;
}

/// <summary>
/// RTF text (<c>{\rtf1</c> magic) used by RichTextBox-style controls to persist a
/// formatted-text (<c>TextRTF</c>) property. Self-delimiting via its own balanced
/// braces — no length prefix or wrapper.
/// </summary>
public record FrxRtfText(string Filename, int Offset, int Length, byte[] RtfBytes)
    : FrxItem(Filename, Offset, Length)
{
    public override byte[] GetPayloadData() => RtfBytes;
}

/// <summary>
/// A single decoded MS-OLEPS property: its numeric identifier, symbolic <c>VT_*</c> type
/// name, and decoded value. Scalar types (<c>VT_I2</c>/<c>I4</c>/<c>R4</c>/<c>R8</c>/
/// <c>BOOL</c>/<c>BSTR</c>/<c>LPSTR</c>/<c>LPWSTR</c>) are decoded to their native CLR value;
/// anything else (<c>VT_VARIANT</c>, vectors, arrays, unknown types) is preserved as the raw
/// <see cref="byte"/>[] of its bounded byte range — which <see cref="System.Text.Json.JsonSerializer"/>
/// serializes as a base64 string by default.
/// </summary>
public record FrxOleObjectBlobProperty(int Id, string Type, object? Value);

/// <summary>
/// Grid-control serialised state (<c>4C 42</c> "LB" magic). The payload past the 24-byte
/// header is an MS-OLEPS <c>PropertySetStream</c> (<c>IPropertyStorage</c>), decoded
/// schema-free into <see cref="Properties"/> by <see cref="MsOlePropertySetReader"/>.
/// </summary>
public record FrxOleObjectBlob(string Filename, int Offset, int Length, int Version, byte[] RawBytes, FrxOleObjectBlobProperty[] Properties)
    : FrxItem(Filename, Offset, Length)
{
    public override byte[] GetPayloadData() => RawBytes;
}
