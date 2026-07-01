using AwesomeAssertions;
using System;
using System.IO;
using System.Text.Json;
using VB6Parser.Frx;

namespace VB6Converter.Tests;

[TestClass]
public class FrxResourceExporterTests
{
    [TestMethod]
    public void Export_BindingsSingleEntry_WritesBindingsJson()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), $"vb6_out_{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputDir);

        try {
            var item = new FrxBindings(
                Filename: "Form1.frx",
                Offset: 0x1305,
                Length: 22,
                Entries: [new FrxBindingsEntry(16, "datUser")]);

            var relativePath = FrxResourceExporter.Export(item, "Form1", outputDir);
            relativePath.Should().EndWith(".bindings.json");

            var fullPath = Path.Combine(outputDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
            File.Exists(fullPath).Should().BeTrue();

            using var doc = JsonDocument.Parse(File.ReadAllText(fullPath));
            doc.RootElement.GetProperty("DataSource").GetString().Should().Be("datUser");
            doc.RootElement.GetProperty("DataField").ValueKind.Should().Be(JsonValueKind.Null);
        }
        finally {
            if (Directory.Exists(outputDir)) {
                Directory.Delete(outputDir, recursive: true);
            }
        }
    }

    [TestMethod]
    public void Export_BindingsTwoEntry_WritesDataSourceAndDataField()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), $"vb6_out_{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputDir);

        try {
            var item = new FrxBindings(
                Filename: "Form1.frx",
                Offset: 0x15B69,
                Length: 38,
                Entries: [
                    new FrxBindingsEntry(2, "Defs"),
                    new FrxBindingsEntry(16, "datClientes(7)"),
                ]);

            var relativePath = FrxResourceExporter.Export(item, "Form1", outputDir);
            relativePath.Should().EndWith(".bindings.json");

            var fullPath = Path.Combine(outputDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
            File.Exists(fullPath).Should().BeTrue();

            using var doc = JsonDocument.Parse(File.ReadAllText(fullPath));
            doc.RootElement.GetProperty("DataSource").GetString().Should().Be("datClientes(7)");
            doc.RootElement.GetProperty("DataField").GetString().Should().Be("Defs");
        }
        finally {
            if (Directory.Exists(outputDir)) {
                Directory.Delete(outputDir, recursive: true);
            }
        }
    }

    [TestMethod]
    public void Export_RtfText_WritesRtfFile()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), $"vb6_out_{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputDir);

        try {
            var rtfBytes = System.Text.Encoding.ASCII.GetBytes("{\\rtf1\\ansi\\deff0 hello\\par }");
            var item = new FrxRtfText(
                Filename: "Form1.frx",
                Offset: 0x0019,
                Length: rtfBytes.Length,
                RtfBytes: rtfBytes);

            var relativePath = FrxResourceExporter.Export(item, "Form1", outputDir);
            relativePath.Should().EndWith(".rtf");

            var fullPath = Path.Combine(outputDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
            File.Exists(fullPath).Should().BeTrue();
            File.ReadAllBytes(fullPath).Should().Equal(rtfBytes);
        }
        finally {
            if (Directory.Exists(outputDir)) {
                Directory.Delete(outputDir, recursive: true);
            }
        }
    }

    [TestMethod]
    public void Export_OleObjectBlob_WritesMsOlepsJson()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), $"vb6_out_{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputDir);

        try {
            var item = new FrxOleObjectBlob(
                Filename: "Form1.frx",
                Offset: 0x2702,
                Length: 200,
                Version: 13,
                RawBytes: [],
                Properties: [
                    new FrxOleObjectBlobProperty(2, "VT_I4", 50),
                    new FrxOleObjectBlobProperty(3, "VT_BOOL", true),
                    new FrxOleObjectBlobProperty(4, "VT_LPSTR", "GridRows"),
                    new FrxOleObjectBlobProperty(100, "VT_VARIANT", new byte[] { 0xDE, 0xAD }),
                ]);

            var relativePath = FrxResourceExporter.Export(item, "Form1", outputDir);
            relativePath.Should().EndWith(".msoleps.json");

            var fullPath = Path.Combine(outputDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
            File.Exists(fullPath).Should().BeTrue();

            using var doc = JsonDocument.Parse(File.ReadAllText(fullPath));
            doc.RootElement.GetProperty("version").GetInt32().Should().Be(13);

            var properties = doc.RootElement.GetProperty("properties");
            properties.GetArrayLength().Should().Be(4);

            properties[0].GetProperty("id").GetInt32().Should().Be(2);
            properties[0].GetProperty("type").GetString().Should().Be("VT_I4");
            properties[0].GetProperty("value").GetInt32().Should().Be(50);

            properties[1].GetProperty("type").GetString().Should().Be("VT_BOOL");
            properties[1].GetProperty("value").GetBoolean().Should().BeTrue();

            properties[2].GetProperty("type").GetString().Should().Be("VT_LPSTR");
            properties[2].GetProperty("value").GetString().Should().Be("GridRows");

            properties[3].GetProperty("type").GetString().Should().Be("VT_VARIANT");
            properties[3].GetProperty("value").GetString().Should().Be(Convert.ToBase64String([0xDE, 0xAD]));
        }
        finally {
            if (Directory.Exists(outputDir)) {
                Directory.Delete(outputDir, recursive: true);
            }
        }
    }
}
