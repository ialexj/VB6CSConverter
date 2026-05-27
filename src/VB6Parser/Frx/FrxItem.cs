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

/// <summary>Unrecognised item — raw bytes preserved for export.</summary>
public record FrxRawItem(string Filename, int Offset, int Length, byte[] Data)
    : FrxItem(Filename, Offset, Length)
{
    public override byte[] GetPayloadData() => Data;
}
