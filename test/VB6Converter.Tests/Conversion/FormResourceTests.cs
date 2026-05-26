using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VB6Parser;

namespace VB6Converter.Tests.Conversion;

[TestClass]
public class FormResourceTests
{
    // ── helpers ────────────────────────────────────────────────────────────

    private static byte[] VariantA(byte[] payload)
    {
        uint size = (uint)payload.Length;
        var bytes = new List<byte>();
        bytes.AddRange(BitConverter.GetBytes(size + 8));       // dwSizeImageEx
        bytes.AddRange(BitConverter.GetBytes(0x0000746Cu));    // key
        bytes.AddRange(BitConverter.GetBytes(size));           // dwSizeImage
        bytes.AddRange(payload);
        return bytes.ToArray();
    }

    private static byte[] VariantC(byte[] payload)
    {
        var bytes = new List<byte>();
        bytes.AddRange(BitConverter.GetBytes((uint)payload.Length));
        bytes.AddRange(payload);
        return bytes.ToArray();
    }

    /// <summary>
    /// Writes an FRX file to a temp directory and runs FrxExtractor.Extract(),
    /// returning the extractor and the temp directory (caller must clean up).
    /// </summary>
    private static (FrxExtractor extractor, string tempDir) BuildExtractor(string formName, byte[] frxData)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"frx_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        var resourcesDir = Path.Combine(tempDir, "_Resources");

        // Write a dummy .frm file (content doesn't matter; only the path is used)
        var frmPath = Path.Combine(tempDir, $"{formName}.frm");
        File.WriteAllText(frmPath, "");

        // Write the FRX file
        var frxPath = Path.Combine(tempDir, $"{formName}.frx");
        File.WriteAllBytes(frxPath, frxData);

        // Create a minimal ConversionTarget pointing at the .frm
        var file = new VisualBasicProjectFile(frmPath, formName, VisualBasicFileType.Form);
        var target = ConversionTarget.Create(file, tempDir, tempDir);

        var extractor = FrxExtractor.Extract([target], resourcesDir);
        return (extractor, tempDir);
    }

    /// <summary>Converts a VB6 form string with the given FrxExtractor and returns the InitializeComponent body text.</summary>
    private static string ConvertFormAndGetInitializeComponent(string vb, FrxExtractor extractor, string formName = "frmMain")
    {
        var opts = new ConversionOptions { FrxExtractor = extractor };
        var conversion = VB6ToCSharpConversion.ConvertString(
            vb, formName, type: VisualBasicFileType.Form, options: opts);

        conversion.ParseErrors.Should().BeEmpty();

        var initMethod = conversion.CompilationUnit
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(m => m.Identifier.Text == "InitializeComponent");

        initMethod.Should().NotBeNull("InitializeComponent should be generated for a form");
        return initMethod!.ToFullString();
    }

    // ── Icon property ──────────────────────────────────────────────────────

    [TestMethod]
    public void Icon_FrxOffset_EmitsNewIconExpression()
    {
        var icoPayload = new byte[] { 0x00, 0x00, 0x01, 0x00 }.Concat(new byte[20]).ToArray();
        var frxData = VariantA(icoPayload);

        var (extractor, tempDir) = BuildExtractor("frmMain", frxData);
        try {
            var vb = """
                Begin VB.Form frmMain
                    Icon            =   "frmMain.frx":0000
                End
                """;

            var body = ConvertFormAndGetInitializeComponent(vb, extractor);

            body.Should().Contain("new System.Drawing.Icon(");
            body.Should().Contain("frmMain.0x0000.ico");
        }
        finally {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    // ── Picture property (BMP) ─────────────────────────────────────────────

    [TestMethod]
    public void Picture_FrxOffset_EmitsNewBitmapExpression()
    {
        var bmpPayload = new byte[] { 0x42, 0x4D }.Concat(new byte[20]).ToArray();
        var frxData = VariantA(bmpPayload);

        var (extractor, tempDir) = BuildExtractor("frmEditor", frxData);
        try {
            var vb = """
                Begin VB.Form frmEditor
                    Picture         =   "frmEditor.frx":0000
                End
                """;

            var body = ConvertFormAndGetInitializeComponent(vb, extractor, "frmEditor");

            body.Should().Contain("new System.Drawing.Bitmap(");
            body.Should().Contain("frmEditor.0x0000.bmp");
        }
        finally {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    // ── Cursor property ────────────────────────────────────────────────────

    [TestMethod]
    public void MouseIcon_FrxOffset_EmitsNewCursorExpression()
    {
        var curPayload = new byte[] { 0x00, 0x00, 0x02, 0x00 }.Concat(new byte[20]).ToArray();
        var frxData = VariantA(curPayload);

        var (extractor, tempDir) = BuildExtractor("frmMain", frxData);
        try {
            var vb = """
                Begin VB.Form frmMain
                    MouseIcon       =   "frmMain.frx":0000
                End
                """;

            var body = ConvertFormAndGetInitializeComponent(vb, extractor);

            body.Should().Contain("new System.Windows.Forms.Cursor(");
            body.Should().Contain("frmMain.0x0000.cur");
        }
        finally {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    // ── String blob → Items.Add() ──────────────────────────────────────────

    [TestMethod]
    public void List_StringBlob_EmitsItemsAddCalls()
    {
        // Build a Variant C string blob: "Apple\0Banana\0Cherry\0"
        var strings = System.Text.Encoding.Default.GetBytes("Apple\0Banana\0Cherry\0");
        var frxData = VariantC(strings);

        var (extractor, tempDir) = BuildExtractor("frmMain", frxData);
        try {
            var vb = """
                Begin VB.Form frmMain
                    Begin VB.ListBox List1
                        List            =   "frmMain.frx":0000
                    End
                End
                """;

            var body = ConvertFormAndGetInitializeComponent(vb, extractor);

            body.Should().Contain("Items.Add(\"Apple\")");
            body.Should().Contain("Items.Add(\"Banana\")");
            body.Should().Contain("Items.Add(\"Cherry\")");
        }
        finally {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    // ── Unknown blob → TODO comment ────────────────────────────────────────

    [TestMethod]
    public void UnknownBlob_FrxOffset_EmitsTransformError()
    {
        // Variant A with unrecognised magic bytes
        var unknownPayload = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }.Concat(new byte[20]).ToArray();
        var frxData = VariantA(unknownPayload);

        var (extractor, tempDir) = BuildExtractor("frmMain", frxData);
        try {
            var vb = """
                Begin VB.Form frmMain
                    Icon            =   "frmMain.frx":0000
                End
                """;

            var opts = new ConversionOptions { FrxExtractor = extractor };
            var conversion = VB6ToCSharpConversion.ConvertString(
                vb, "frmMain", type: VisualBasicFileType.Form, options: opts);

            conversion.ParseErrors.Should().BeEmpty();
            // An Unknown-format blob should emit a transform error, not a valid Icon expression
            conversion.TransformErrors.Should().NotBeEmpty();
        }
        finally {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    // ── FrxExtractor.Load round-trip ───────────────────────────────────────

    [TestMethod]
    public void Load_RoundTripsStringBlob()
    {
        var strings = System.Text.Encoding.Default.GetBytes("One\0Two\0");
        var frxData = VariantC(strings);

        var (_, tempDir) = BuildExtractor("frmMain", frxData);
        try {
            var resourcesDir = Path.Combine(tempDir, "_Resources");

            var loaded = FrxExtractor.Load(resourcesDir);
            var entry = loaded.GetResource("frmmain.frx", "0000");

            entry.Should().NotBeNull();
            entry!.Format.Should().Be(FrxFormat.StringBlob);
            entry.Strings.Should().BeEquivalentTo(["One", "Two"]);
        }
        finally {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void Load_RoundTripsImageEntry()
    {
        var icoPayload = new byte[] { 0x00, 0x00, 0x01, 0x00 }.Concat(new byte[20]).ToArray();
        var frxData = VariantA(icoPayload);

        var (_, tempDir) = BuildExtractor("frmMain", frxData);
        try {
            var resourcesDir = Path.Combine(tempDir, "_Resources");

            var loaded = FrxExtractor.Load(resourcesDir);
            var entry = loaded.GetResource("frmmain.frx", "0000");

            entry.Should().NotBeNull();
            entry!.Format.Should().Be(FrxFormat.Ico);
            entry.ResourceFilePath.Should().Contain("frmMain.0x0000.ico");
        }
        finally {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
