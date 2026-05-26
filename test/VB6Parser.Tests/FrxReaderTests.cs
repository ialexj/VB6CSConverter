using AwesomeAssertions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace VB6Parser.Tests;

[TestClass]
public class FrxReaderTests
{
    // ── helpers ────────────────────────────────────────────────────────────

    /// <summary>Builds a Variant A (12-byte header) blob.</summary>
    private static byte[] VariantA(byte[] payload)
    {
        uint dwSizeImage = (uint)payload.Length;
        uint dwSizeImageEx = dwSizeImage + 8;

        var bytes = new List<byte>();
        bytes.AddRange(BitConverter.GetBytes(dwSizeImageEx)); // +0
        bytes.AddRange(BitConverter.GetBytes(0x0000746Cu));   // +4  key
        bytes.AddRange(BitConverter.GetBytes(dwSizeImage));    // +8
        bytes.AddRange(payload);
        return bytes.ToArray();
    }

    /// <summary>Builds a Variant B (28-byte header) blob.</summary>
    private static byte[] VariantB(byte[] payload)
    {
        uint dwSizeImage = (uint)payload.Length;
        uint dwSizeImageEx = dwSizeImage + 24;

        var bytes = new List<byte>();
        bytes.AddRange(BitConverter.GetBytes(dwSizeImageEx)); // +0
        bytes.AddRange(BitConverter.GetBytes(0x0000746Cu));   // +4  key
        bytes.AddRange(new byte[16]);                          // +8  extra metadata
        bytes.AddRange(BitConverter.GetBytes(dwSizeImage));    // +24
        bytes.AddRange(payload);
        return bytes.ToArray();
    }

    /// <summary>Builds a Variant C (4-byte length-prefix) blob.</summary>
    private static byte[] VariantC(byte[] payload)
    {
        var bytes = new List<byte>();
        bytes.AddRange(BitConverter.GetBytes((uint)payload.Length));
        bytes.AddRange(payload);
        return bytes.ToArray();
    }

    private static byte[] BmpMagic() => [0x42, 0x4D, 0x00, 0x00];
    private static byte[] GifMagic() => [0x47, 0x49, 0x46, 0x38];
    private static byte[] JpegMagic() => [0xFF, 0xD8, 0xFF, 0xE0];
    private static byte[] IcoMagic()  => [0x00, 0x00, 0x01, 0x00];
    private static byte[] CurMagic()  => [0x00, 0x00, 0x02, 0x00];
    private static byte[] WmfMagic()  => [0xD7, 0xCD, 0xC6, 0x9A];

    private static byte[] EmfMagic()
    {
        // EMF signature " EMF" appears at byte offset 40 in the blob
        var bytes = new byte[44];
        bytes[40] = 0x20; bytes[41] = 0x45; bytes[42] = 0x4D; bytes[43] = 0x46;
        return bytes;
    }

    // ── FrxReader.Read ─────────────────────────────────────────────────────

    [TestMethod]
    public void Read_VariantA_Bmp_ReturnsCorrectBlob()
    {
        var payload = BmpMagic().Concat(new byte[100]).ToArray();
        var data = VariantA(payload);

        var blob = FrxReader.Read(data, "0000");

        blob.Should().NotBeNull();
        blob!.Format.Should().Be(FrxFormat.Bmp);
        blob.Data.Length.Should().Be(payload.Length);
        blob.Offset.Should().Be(0);
    }

    [TestMethod]
    public void Read_VariantB_Bmp_ReturnsCorrectBlob()
    {
        var payload = BmpMagic().Concat(new byte[50]).ToArray();
        var data = VariantB(payload);

        var blob = FrxReader.Read(data, "0000");

        blob.Should().NotBeNull();
        blob!.Format.Should().Be(FrxFormat.Bmp);
        blob.Data.Length.Should().Be(payload.Length);
    }

    [TestMethod]
    public void Read_VariantC_StringBlob_SplitsOnNull()
    {
        var strings = new byte[] { (byte)'a', (byte)'b', 0x00, (byte)'c', (byte)'d', 0x00 };
        var data = VariantC(strings);

        var blob = FrxReader.Read(data, "0000");

        blob.Should().NotBeNull();
        blob!.Format.Should().Be(FrxFormat.StringBlob);
        blob.Strings.Should().BeEquivalentTo(["ab", "cd"]);
    }

    [TestMethod]
    public void Read_VariantC_EmptyBlob_ReturnsEmptyStringBlob()
    {
        var data = VariantC([]);

        var blob = FrxReader.Read(data, "0000");

        blob.Should().NotBeNull();
        blob!.Format.Should().Be(FrxFormat.StringBlob);
        blob.Strings.Should().BeEmpty();
    }

    [TestMethod]
    public void Read_IcoMagic_DetectedAsIco()
    {
        var payload = IcoMagic().Concat(new byte[20]).ToArray();
        var blob = FrxReader.Read(VariantA(payload), "0000");
        blob!.Format.Should().Be(FrxFormat.Ico);
    }

    [TestMethod]
    public void Read_CurMagic_DetectedAsCur()
    {
        var payload = CurMagic().Concat(new byte[20]).ToArray();
        var blob = FrxReader.Read(VariantA(payload), "0000");
        blob!.Format.Should().Be(FrxFormat.Cur);
    }

    [TestMethod]
    public void Read_GifMagic_DetectedAsGif()
    {
        var payload = GifMagic().Concat(new byte[20]).ToArray();
        var blob = FrxReader.Read(VariantA(payload), "0000");
        blob!.Format.Should().Be(FrxFormat.Gif);
    }

    [TestMethod]
    public void Read_JpegMagic_DetectedAsJpeg()
    {
        var payload = JpegMagic().Concat(new byte[20]).ToArray();
        var blob = FrxReader.Read(VariantA(payload), "0000");
        blob!.Format.Should().Be(FrxFormat.Jpeg);
    }

    [TestMethod]
    public void Read_WmfMagic_DetectedAsWmf()
    {
        var payload = WmfMagic().Concat(new byte[20]).ToArray();
        var blob = FrxReader.Read(VariantA(payload), "0000");
        blob!.Format.Should().Be(FrxFormat.Wmf);
    }

    [TestMethod]
    public void Read_EmfMagic_DetectedAsEmf()
    {
        var payload = EmfMagic();
        var blob = FrxReader.Read(VariantA(payload), "0000");
        blob!.Format.Should().Be(FrxFormat.Emf);
    }

    [TestMethod]
    public void Read_NullData_ReturnsNull()
    {
        var blob = FrxReader.Read([], "0000");
        blob.Should().BeNull();
    }

    [TestMethod]
    public void Read_OffsetBeyondEnd_ReturnsNull()
    {
        var data = VariantA(BmpMagic());
        var blob = FrxReader.Read(data, "FFFF");
        blob.Should().BeNull();
    }

    [TestMethod]
    public void Read_HexOffset_ParsedCorrectly()
    {
        // First blob at 0x0000; write a second at the offset after it
        var payload = BmpMagic().Concat(new byte[4]).ToArray();
        var first = VariantA(payload);
        // second blob at offset = first.Length
        var secondPayload = IcoMagic().Concat(new byte[4]).ToArray();
        var second = VariantA(secondPayload);

        var combined = first.Concat(second).ToArray();
        var hex = first.Length.ToString("X4");

        var blob = FrxReader.Read(combined, hex);
        blob.Should().NotBeNull();
        blob!.Format.Should().Be(FrxFormat.Ico);
    }

    // ── FrxReader.ReadAll ─────────────────────────────────────────────────

    [TestMethod]
    public void ReadAll_TwoBlobs_ReturnsBothWithCorrectOffsets()
    {
        var payload1 = BmpMagic().Concat(new byte[8]).ToArray();
        var payload2 = IcoMagic().Concat(new byte[8]).ToArray();
        var data = VariantA(payload1).Concat(VariantA(payload2)).ToArray();

        var blobs = FrxReader.ReadAll(data).ToArray();

        blobs.Length.Should().Be(2);
        blobs[0].Format.Should().Be(FrxFormat.Bmp);
        blobs[0].Offset.Should().Be(0);
        blobs[1].Format.Should().Be(FrxFormat.Ico);
        blobs[1].Offset.Should().Be(VariantA(payload1).Length);
    }

    [TestMethod]
    public void ReadAll_MixedVariantAAndC_YieldsAllBlobs()
    {
        var imgPayload = BmpMagic().Concat(new byte[4]).ToArray();
        var strPayload = new byte[] { (byte)'x', (byte)'y', 0x00 };

        var data = VariantA(imgPayload).Concat(VariantC(strPayload)).ToArray();
        var blobs = FrxReader.ReadAll(data).ToArray();

        blobs.Length.Should().Be(2);
        blobs[0].Format.Should().Be(FrxFormat.Bmp);
        blobs[1].Format.Should().Be(FrxFormat.StringBlob);
        blobs[1].Strings.Should().BeEquivalentTo(["xy"]);
    }

    [TestMethod]
    public void ReadAll_EmptyData_ReturnsEmpty()
    {
        var blobs = FrxReader.ReadAll([]).ToArray();
        blobs.Should().BeEmpty();
    }

    // ── FrxReader.GetExtension ─────────────────────────────────────────────

    [TestMethod]
    public void GetExtension_KnownFormats_ReturnCorrectExtensions()
    {
        FrxReader.GetExtension(FrxFormat.Bmp).Should().Be("bmp");
        FrxReader.GetExtension(FrxFormat.Gif).Should().Be("gif");
        FrxReader.GetExtension(FrxFormat.Jpeg).Should().Be("jpg");
        FrxReader.GetExtension(FrxFormat.Ico).Should().Be("ico");
        FrxReader.GetExtension(FrxFormat.Cur).Should().Be("cur");
        FrxReader.GetExtension(FrxFormat.Wmf).Should().Be("wmf");
        FrxReader.GetExtension(FrxFormat.Emf).Should().Be("emf");
    }
}
