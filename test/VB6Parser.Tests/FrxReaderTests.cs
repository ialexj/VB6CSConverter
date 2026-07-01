using AwesomeAssertions;
using System;
using System.IO;
using System.Text;
using VB6Parser.Frx;

namespace VB6Parser.Tests;

[TestClass]
public class FrxReaderTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string WriteTempFile(byte[] data)
    {
        var path = Path.GetTempFileName();
        File.WriteAllBytes(path, data);
        return path;
    }

    private static byte[] Int32Le(int value) => BitConverter.GetBytes(value);
    private static byte[] Int16Le(short value) => BitConverter.GetBytes(value);

    // ── BinaryBlob: zero-length payload ──────────────────────────────────────

    [TestMethod]
    public void Read_ZeroLengthBinaryBlob()
    {
        // byteLength == 4, payloadLen == 0 → BinaryBlob with empty RawPayload
        var data = Int32Le(0);
        var path = WriteTempFile(data);
        try {
            var item = FrxReader.Read(path, 0, 4);
            item.Should().BeOfType<FrxBinaryBlob>();
            var blob = (FrxBinaryBlob)item;
            blob.DataLength.Should().Be(0);
            blob.Payload.Should().BeOfType<FrxRawPayload>();
            blob.Payload.GetData().Should().BeEmpty();
        }
        finally { File.Delete(path); }
    }

    // ── BinaryBlob: ImagePayload without CLSID ────────────────────────────────

    [TestMethod]
    public void Read_ImagePayload_NoCLSID()
    {
        // Build the ICO example from frx.md:
        //   payloadLen = 3782  (byteLength = 3786)
        //   magic 6C 74 00 00
        //   imageLen = 3774   (3786 − 12 = 3774)
        //   image data (ICO header: 00 00 01 00 …)
        var imageData = new byte[3774];
        imageData[0] = 0x00; imageData[1] = 0x00;
        imageData[2] = 0x01; imageData[3] = 0x00; // ICO magic

        var buf = new MemoryStream();
        buf.Write(Int32Le(3782));               // payloadLen
        buf.Write([0x6C, 0x74, 0x00, 0x00]);    // image magic
        buf.Write(Int32Le(3774));               // imageLen
        buf.Write(imageData);

        var path = WriteTempFile(buf.ToArray());
        try {
            var item = FrxReader.Read(path, 0, 3786);
            item.Should().BeOfType<FrxBinaryBlob>();
            var blob = (FrxBinaryBlob)item;
            blob.Payload.Should().BeOfType<FrxImagePayload>();
            var img = (FrxImagePayload)blob.Payload;
            img.ClsId.Should().BeNull();
            img.ImageLength.Should().Be(3774);
            img.ImageBytes.Length.Should().Be(3774);
            img.ImageBytes[0..4].Should().Equal([0x00, 0x00, 0x01, 0x00]);
        }
        finally { File.Delete(path); }
    }

    // ── BinaryBlob: ImagePayload with CLSID ──────────────────────────────────

    [TestMethod]
    public void Read_ImagePayload_WithCLSID()
    {
        // CLSID {0BE35204-8F91-11CE-9DE3-00AA004BB851} (OLE StdPicture)
        // Bytes (little-endian): 04 52 E3 0B 91 8F CE 11 9D E3 00 AA 00 4B B8 51
        var clsidBytes = new byte[] {
            0x04, 0x52, 0xE3, 0x0B, 0x91, 0x8F, 0xCE, 0x11,
            0x9D, 0xE3, 0x00, 0xAA, 0x00, 0x4B, 0xB8, 0x51
        };

        var imageData = new byte[100];
        imageData[0] = 0x42; imageData[1] = 0x4D; // BMP magic

        // byteLength = 4 + 16 + 4 + 4 + 100 = 128
        // payloadLen = 128 − 4 = 124
        // imageLen   = 128 − 28 = 100
        var buf = new MemoryStream();
        buf.Write(Int32Le(124));                // payloadLen
        buf.Write(clsidBytes);                  // CLSID (16 bytes)
        buf.Write([0x6C, 0x74, 0x00, 0x00]);    // image magic
        buf.Write(Int32Le(100));                // imageLen
        buf.Write(imageData);

        var path = WriteTempFile(buf.ToArray());
        try {
            var item = FrxReader.Read(path, 0, 128);
            item.Should().BeOfType<FrxBinaryBlob>();
            var blob = (FrxBinaryBlob)item;
            blob.Payload.Should().BeOfType<FrxImagePayload>();
            var img = (FrxImagePayload)blob.Payload;
            img.ClsId.Should().NotBeNull();
            img.ClsId!.Value.Should().Be(new Guid(clsidBytes));
            img.ImageLength.Should().Be(100);
            img.ImageBytes[0..2].Should().Equal([0x42, 0x4D]);
        }
        finally { File.Delete(path); }
    }

    // ── BinaryBlob: bad imageLength falls back to RawPayload ─────────────────

    [TestMethod]
    public void Read_ImagePayload_BadImageLength_FallsBackToRaw()
    {
        // payloadLen matches, magic present, but imageLen is wrong
        var buf = new MemoryStream();
        buf.Write(Int32Le(12));               // payloadLen = byteLength − 4 = 12 ✓
        buf.Write([0x6C, 0x74, 0x00, 0x00]); // magic
        buf.Write(Int32Le(999));              // imageLen = 999 ≠ 16 − 12 = 4 → invalid

        var path = WriteTempFile(buf.ToArray());
        try {
            var item = FrxReader.Read(path, 0, 16);
            item.Should().BeOfType<FrxBinaryBlob>();
            var blob = (FrxBinaryBlob)item;
            blob.Payload.Should().BeOfType<FrxRawPayload>();
        }
        finally { File.Delete(path); }
    }

    // ── StringList: empty (count == 0) ───────────────────────────────────────

    [TestMethod]
    public void Read_StringList_Empty()
    {
        // byteLength == 2, count == 0
        var data = Int16Le(0);
        var path = WriteTempFile(data);
        try {
            var item = FrxReader.Read(path, 0, 2);
            item.Should().BeOfType<FrxStringList>();
            var list = (FrxStringList)item;
            list.Strings.Should().BeEmpty();
        }
        finally { File.Delete(path); }
    }

    // ── StringList: 3 items "1", "22", "333" ─────────────────────────────────

    [TestMethod]
    public void Read_StringList_ThreeItems()
    {
        // From frx.md ItemData example:
        //   03 00  count=3
        //   03 00  maxItemLength=3
        //   01 00  31                 "1"
        //   02 00  32 32              "22"
        //   03 00  33 33 33           "333"
        var data = new byte[] {
            0x03, 0x00,
            0x03, 0x00,
            0x01, 0x00, 0x31,
            0x02, 0x00, 0x32, 0x32,
            0x03, 0x00, 0x33, 0x33, 0x33
        };
        var path = WriteTempFile(data);
        try {
            var item = FrxReader.Read(path, 0, data.Length);
            item.Should().BeOfType<FrxStringList>();
            var list = (FrxStringList)item;
            list.Strings.Should().HaveCount(3);
            list.Strings[0].Should().Be("1");
            list.Strings[1].Should().Be("22");
            list.Strings[2].Should().Be("333");
        }
        finally { File.Delete(path); }
    }

    // ── StringList: 3 items with varying length from frx.md ─────────────────

    [TestMethod]
    public void Read_StringList_VaryingLength()
    {
        // List property example: "XXXX", "YYYYYY", "ZZZZZZZZ"
        var data = new byte[] {
            0x03, 0x00,  // count = 3
            0x08, 0x00,  // maxItemLength = 8
            0x04, 0x00, 0x58, 0x58, 0x58, 0x58,                    // "XXXX"
            0x06, 0x00, 0x59, 0x59, 0x59, 0x59, 0x59, 0x59,        // "YYYYYY"
            0x08, 0x00, 0x5A, 0x5A, 0x5A, 0x5A, 0x5A, 0x5A, 0x5A, 0x5A  // "ZZZZZZZZ"
        };
        var path = WriteTempFile(data);
        try {
            var item = FrxReader.Read(path, 0, data.Length);
            item.Should().BeOfType<FrxStringList>();
            var list = (FrxStringList)item;
            list.Strings.Should().Equal(["XXXX", "YYYYYY", "ZZZZZZZZ"]);
        }
        finally { File.Delete(path); }
    }

    // ── Bindings: single-entry (DataSource only) ────────────────────────────

    [TestMethod]
    public void Read_Bindings_SingleEntry()
    {
        // C6 FA 01 00 + flags=9 + nameLen=6 + "datDoc" + 6-byte zero trailer
        var data = new byte[] {
            0xC6, 0xFA, 0x01, 0x00,
            0x09, 0x00, 0x00, 0x00,
            0x06,
            0x64, 0x61, 0x74, 0x44, 0x6F, 0x63,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        };

        var path = WriteTempFile(data);
        try {
            var item = FrxReader.Read(path, 0, data.Length);
            item.Should().BeOfType<FrxBindings>();
            var bindings = (FrxBindings)item;
            bindings.DataSource.Should().Be("datDoc");
            bindings.DataField.Should().BeNull();
            bindings.Entries.Should().HaveCount(1);
            bindings.Entries[0].Flags.Should().Be(9);
            bindings.Entries[0].Name.Should().Be("datDoc");
        }
        finally { File.Delete(path); }
    }

    // ── Bindings: two-entry (DataField + DataSource) ────────────────────────

    [TestMethod]
    public void Read_Bindings_TwoEntry()
    {
        // C6 FA 02 00 + "Defs" + "datClientes(7)" + trailer
        var data = new byte[] {
            0xC6, 0xFA, 0x02, 0x00,
            0x02, 0x00, 0x00, 0x00,
            0x04,
            0x44, 0x65, 0x66, 0x73,
            0x10, 0x00, 0x00, 0x00,
            0x0E,
            0x64, 0x61, 0x74, 0x43, 0x6C, 0x69, 0x65, 0x6E, 0x74, 0x65, 0x73, 0x28, 0x37, 0x29,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        };

        var path = WriteTempFile(data);
        try {
            var item = FrxReader.Read(path, 0, data.Length);
            item.Should().BeOfType<FrxBindings>();
            var bindings = (FrxBindings)item;
            bindings.DataField.Should().Be("Defs");
            bindings.DataSource.Should().Be("datClientes(7)");
            bindings.Entries.Should().HaveCount(2);
            bindings.Entries[0].Flags.Should().Be(2);
            bindings.Entries[1].Flags.Should().Be(16);
        }
        finally { File.Delete(path); }
    }

    // ── RTF text: detected by "{\rtf1" magic ─────────────────────────────────

    [TestMethod]
    public void Read_RtfText_DetectedByMagic()
    {
        // Minimal well-formed RTF document, per the frx.md example — no length
        // prefix/wrapper, self-delimiting via balanced braces.
        var data = Encoding.ASCII.GetBytes("{\\rtf1\\ansi\\deff0{\\fonttbl{\\f0 Arial;}}\\f0\\fs17 hello\\par }");
        var path = WriteTempFile(data);
        try {
            var item = FrxReader.Read(path, 0, data.Length);
            item.Should().BeOfType<FrxRtfText>();
            var rtf = (FrxRtfText)item;
            rtf.RtfBytes.Should().Equal(data);
            rtf.GetPayloadData().Should().Equal(data);
        }
        finally { File.Delete(path); }
    }

    // ── OleObjectBlob: detected by "LB" magic, MS-OLEPS properties decoded ────

    private static byte[] BuildOlePropertySetBlob()
    {
        // PropertySetStream header (48 bytes, NumPropertySets == 1, Offset0 == 48).
        var oleps = new MemoryStream();
        oleps.Write([0xFE, 0xFF]);              // ByteOrder
        oleps.Write(Int16Le(0));                // Version (internal OLEPS version)
        oleps.Write(Int32Le(0));                // SystemIdentifier
        oleps.Write(new byte[16]);              // CLSID
        oleps.Write(Int32Le(1));                // NumPropertySets
        oleps.Write(new byte[16]);               // FMTID0
        oleps.Write(Int32Le(48));               // Offset0

        // PropertySet section @48: Size(60), NumProperties(3), then 3 × (id, offset).
        oleps.Write(Int32Le(60));               // Size
        oleps.Write(Int32Le(3));                // NumProperties
        oleps.Write(Int32Le(2)); oleps.Write(Int32Le(32));  // id=2 @ relative 32 (abs 80)
        oleps.Write(Int32Le(3)); oleps.Write(Int32Le(40));  // id=3 @ relative 40 (abs 88)
        oleps.Write(Int32Le(4)); oleps.Write(Int32Le(48));  // id=4 @ relative 48 (abs 96)

        // prop id=2: VT_I4 = 50 (8 bytes: type + int32 value)
        oleps.Write(Int32Le(3));                // VT_I4
        oleps.Write(Int32Le(50));

        // prop id=3: VT_BOOL = true (8 bytes: type + int16 value + 2-byte padding)
        oleps.Write(Int32Le(11));               // VT_BOOL
        oleps.Write(Int16Le(-1));               // VARIANT_BOOL TRUE (0xFFFF)
        oleps.Write(Int16Le(0));                // padding

        // prop id=4: VT_LPSTR = "Hi" (12 bytes: type + size(3, incl. null) + "Hi\0" + 1 pad)
        oleps.Write(Int32Le(30));               // VT_LPSTR
        oleps.Write(Int32Le(3));                // byte count, includes null terminator
        oleps.Write(Encoding.ASCII.GetBytes("Hi\0"));
        oleps.Write(new byte[] { 0x00 });       // 4-byte alignment padding

        var olepsBytes = oleps.ToArray();
        olepsBytes.Length.Should().Be(108);

        // LB header (24 bytes): magic, version, contentSize == olepsBytes.Length, 16-byte control header.
        var buf = new MemoryStream();
        buf.Write([0x4C, 0x42]);                // "LB" magic
        buf.Write(Int16Le(13));                 // LB blob version
        buf.Write(Int32Le(olepsBytes.Length));  // contentSize
        buf.Write(new byte[16]);                // control-specific header
        buf.Write(olepsBytes);

        return buf.ToArray();
    }

    [TestMethod]
    public void Read_OleObjectBlob_DecodesKnownProperties()
    {
        var data = BuildOlePropertySetBlob();
        var path = WriteTempFile(data);
        try {
            var item = FrxReader.Read(path, 0, data.Length);
            item.Should().BeOfType<FrxOleObjectBlob>();
            var blob = (FrxOleObjectBlob)item;
            blob.Version.Should().Be(13);
            blob.Properties.Should().HaveCount(3);

            blob.Properties[0].Id.Should().Be(2);
            blob.Properties[0].Type.Should().Be("VT_I4");
            blob.Properties[0].Value.Should().Be(50);

            blob.Properties[1].Id.Should().Be(3);
            blob.Properties[1].Type.Should().Be("VT_BOOL");
            blob.Properties[1].Value.Should().Be(true);

            blob.Properties[2].Id.Should().Be(4);
            blob.Properties[2].Type.Should().Be("VT_LPSTR");
            blob.Properties[2].Value.Should().Be("Hi");
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void Read_MalformedOleObjectBlobHeader_FallsBackToRawItem()
    {
        // "LB" magic present but contentSize doesn't match byteLength - 24 → not a valid blob.
        var data = new byte[30];
        data[0] = 0x4C; data[1] = 0x42;
        Int32Le(999).CopyTo(data, 4); // bogus contentSize

        var path = WriteTempFile(data);
        try {
            var item = FrxReader.Read(path, 0, data.Length);
            item.Should().NotBeOfType<FrxOleObjectBlob>();
        }
        finally { File.Delete(path); }
    }

    // ── FrxRawItem fallback ───────────────────────────────────────────────────

    [TestMethod]
    public void Read_UnrecognisedData_FallsBackToRawItem()
    {
        // Bindings magic: C6 FA 01 00 — payloadLen check fails, StringList parse fails
        var data = new byte[] { 0xC6, 0xFA, 0x01, 0x00, 0x09, 0x00, 0x00, 0x00, 0x0B };
        var path = WriteTempFile(data);
        try {
            var item = FrxReader.Read(path, 0, data.Length);
            item.Should().BeOfType<FrxRawItem>();
            ((FrxRawItem)item).Data.Should().Equal(data);
        }
        finally { File.Delete(path); }
    }

    // ── Read from non-zero offset ─────────────────────────────────────────────

    [TestMethod]
    public void Read_NonZeroOffset()
    {
        // Prepend 8 garbage bytes, then a 2-byte empty StringList at offset 8
        var data = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00, 0x00 };
        var path = WriteTempFile(data);
        try {
            var item = FrxReader.Read(path, 8, 2);
            item.Should().BeOfType<FrxStringList>();
        }
        finally { File.Delete(path); }
    }
}
