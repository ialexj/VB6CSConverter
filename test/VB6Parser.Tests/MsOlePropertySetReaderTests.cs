using AwesomeAssertions;
using System;
using System.IO;
using System.Text;
using VB6Parser.Frx;

namespace VB6Parser.Tests;

[TestClass]
public class MsOlePropertySetReaderTests
{
    private static byte[] Int32Le(int value) => BitConverter.GetBytes(value);
    private static byte[] Int16Le(short value) => BitConverter.GetBytes(value);

    // ── Header helpers ────────────────────────────────────────────────────────

    private static void WriteHeader(MemoryStream stream, int offset0)
    {
        stream.Write([0xFE, 0xFF]);            // ByteOrder
        stream.Write(Int16Le(0));              // Version
        stream.Write(Int32Le(0));              // SystemIdentifier
        stream.Write(new byte[16]);            // CLSID
        stream.Write(Int32Le(1));              // NumPropertySets
        stream.Write(new byte[16]);            // FMTID0
        stream.Write(Int32Le(offset0));        // Offset0
    }

    // ── Known scalar types ────────────────────────────────────────────────────

    [TestMethod]
    public void TryParse_SingleVT_I4_Property_DecodesInt()
    {
        var stream = new MemoryStream();
        WriteHeader(stream, offset0: 48);

        stream.Write(Int32Le(20));             // Size (8 dir header + 8 entry + 4+4 value)
        stream.Write(Int32Le(1));              // NumProperties
        stream.Write(Int32Le(5)); stream.Write(Int32Le(16)); // id=5 @ relative 16

        stream.Write(Int32Le(3));              // VT_I4
        stream.Write(Int32Le(-123));

        var data = stream.ToArray();
        MsOlePropertySetReader.TryParse(data, out _, out var properties).Should().BeTrue();
        properties.Should().ContainSingle();
        properties![0].Id.Should().Be(5);
        properties[0].Type.Should().Be("VT_I4");
        properties[0].Value.Should().Be(-123);
    }

    [TestMethod]
    public void TryParse_SingleVT_BOOL_Property_DecodesBool()
    {
        var stream = new MemoryStream();
        WriteHeader(stream, offset0: 48);

        stream.Write(Int32Le(20));
        stream.Write(Int32Le(1));
        stream.Write(Int32Le(1)); stream.Write(Int32Le(16));

        stream.Write(Int32Le(11));             // VT_BOOL
        stream.Write(Int16Le(-1));              // TRUE
        stream.Write(Int16Le(0));               // padding

        var data = stream.ToArray();
        MsOlePropertySetReader.TryParse(data, out _, out var properties).Should().BeTrue();
        properties![0].Type.Should().Be("VT_BOOL");
        properties[0].Value.Should().Be(true);
    }

    [TestMethod]
    public void TryParse_SingleVT_LPSTR_Property_DecodesString()
    {
        var stream = new MemoryStream();
        WriteHeader(stream, offset0: 48);

        var chars = Encoding.ASCII.GetBytes("Test\0"); // 5 bytes incl. null terminator
        stream.Write(Int32Le(8 + 8 + 4 + 4 + chars.Length + 3)); // dir + entry + type + size + chars + pad(3→align4)
        stream.Write(Int32Le(1));
        stream.Write(Int32Le(9)); stream.Write(Int32Le(16));

        stream.Write(Int32Le(30));             // VT_LPSTR
        stream.Write(Int32Le(chars.Length));
        stream.Write(chars);
        stream.Write(new byte[3]);              // alignment padding

        var data = stream.ToArray();
        MsOlePropertySetReader.TryParse(data, out _, out var properties).Should().BeTrue();
        properties![0].Type.Should().Be("VT_LPSTR");
        properties[0].Value.Should().Be("Test");
    }

    // ── Unknown/unhandled types fall back to raw byte range ──────────────────

    [TestMethod]
    public void TryParse_UnhandledType_FallsBackToRawBytes()
    {
        var stream = new MemoryStream();
        WriteHeader(stream, offset0: 48);

        var rawValue = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x01, 0x02, 0x03, 0x04 };
        stream.Write(Int32Le(8 + 8 + 4 + rawValue.Length)); // Size
        stream.Write(Int32Le(1));              // NumProperties
        stream.Write(Int32Le(7)); stream.Write(Int32Le(16)); // id=7 @ relative 16

        stream.Write(Int32Le(6));              // VT_CY (not explicitly decoded)
        stream.Write(rawValue);

        var data = stream.ToArray();
        MsOlePropertySetReader.TryParse(data, out _, out var properties).Should().BeTrue();
        properties!.Should().ContainSingle();
        properties[0].Type.Should().Be("VT_CY");
        properties[0].Value.Should().BeOfType<byte[]>();
        ((byte[])properties[0].Value!).Should().Equal(rawValue);
    }

    // ── Malformed input fails safely ──────────────────────────────────────────

    [TestMethod]
    public void TryParse_BadByteOrderMark_ReturnsFalse()
    {
        var data = new byte[48];
        data[0] = 0x00; data[1] = 0x00; // not 0xFFFE

        MsOlePropertySetReader.TryParse(data, out _, out var properties).Should().BeFalse();
        properties.Should().BeNull();
    }

    [TestMethod]
    public void TryParse_TruncatedData_ReturnsFalse()
    {
        var data = new byte[10];
        MsOlePropertySetReader.TryParse(data, out _, out var properties).Should().BeFalse();
        properties.Should().BeNull();
    }
}
