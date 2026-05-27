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
