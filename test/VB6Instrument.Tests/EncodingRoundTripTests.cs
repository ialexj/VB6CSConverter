#nullable enable
using System.Text;
using AwesomeAssertions;

namespace VB6Instrument.Tests;

[TestClass]
public class EncodingRoundTripTests
{
    [TestMethod]
    public void BomLessCp1252FileIsNeverReencodedAsUtf8()
    {
        // é ã ç encode as 0xE9 0xE3 0xE7 in CP1252 — silent mojibake to UTF-8
        // would double these bytes.
        const string text = "message = \"Não foi possível\"\r\n";
        var output = TestSources.Run(
            text,
            [_ => { }], // no passes — nothing to change
            encoding: Encoding.GetEncoding(1252));
        Encoding.GetEncoding(1252).GetBytes(output).Should().BeEquivalentTo(Encoding.GetEncoding(1252).GetBytes(TestSources.Crlf(text)));
        output.Should().Contain("Não foi possível");
    }

    [TestMethod]
    public void UntouchedRegionsStayByteIdentical()
    {
        var output = TestSources.Run(
            "Attribute VB_Name = \"modAccents\"\r\nOption Explicit\r\n\r\nPublic Sub Show()\r\n    MsgBox \"Versão\"\r\nEnd Sub\r\n",
            [model => new LineNumbersPass().Run(model)],
            fileName: "modAccents.bas",
            encoding: Encoding.GetEncoding(1252));

        var bytes = Encoding.GetEncoding(1252).GetBytes(output);
        // The accented string sits outside the changed region —
        // 0xE3 (ã in "Versão") must appear exactly once, as a single byte.
        bytes.Count(b => b == 0xE3).Should().Be(1);
    }

    [TestMethod]
    public void Utf8BomIsPreserved()
    {
        var directory = Path.Combine(Path.GetTempPath(), "vb6instrument-tests");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"bom-{Guid.NewGuid():N}.bas");
        try {
            File.WriteAllText(path, "Attribute VB_Name = \"modUtf8\"\r\nPublic Sub Show()\r\n    MsgBox \"Versão\"\r\nEnd Sub\r\n", new UTF8Encoding(true));
            var source = SourceFile.Load(path, 1252);
            var model = Vb6FileParser.Parse(source);
            new LineNumbersPass().Run(model);
            source.WriteIfChanged();

            var bytes = File.ReadAllBytes(path);
            bytes[0..3].Should().BeEquivalentTo(new byte[] { 0xEF, 0xBB, 0xBF });
            File.ReadAllText(path, new UTF8Encoding(true)).Should().Contain("Versão");
        }
        finally {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void MissingFinalNewlineSurvives()
    {
        var directory = Path.Combine(Path.GetTempPath(), "vb6instrument-tests");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"noeol-{Guid.NewGuid():N}.bas");
        try {
            File.WriteAllText(path, "Attribute VB_Name = \"modT\"\r\nPublic Sub Foo()\r\n    x = 1\r\nEnd Sub", Encoding.ASCII);
            var source = SourceFile.Load(path, 1252);
            var model = Vb6FileParser.Parse(source);
            new LineNumbersPass().Run(model);
            source.WriteIfChanged();

            var text = File.ReadAllText(path);
            text.Should().NotEndWith("\n");
            text.Should().EndWith("End Sub");
        }
        finally {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void LoneCrIsRejected()
    {
        var directory = Path.Combine(Path.GetTempPath(), "vb6instrument-tests");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"cr-{Guid.NewGuid():N}.bas");
        try {
            File.WriteAllText(path, "a\rb", Encoding.ASCII);
            var act = () => SourceFile.Load(path, 1252);
            act.Should().Throw<InstrumentationException>().WithMessage("*Lone CR*");
        }
        finally {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void UnchangedFileIsNotRewritten()
    {
        var directory = Path.Combine(Path.GetTempPath(), "vb6instrument-tests");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"same-{Guid.NewGuid():N}.bas");
        try {
            File.WriteAllText(path, "Attribute VB_Name = \"modT\"\r\n", Encoding.ASCII);
            var before = File.GetLastWriteTimeUtc(path);
            var source = SourceFile.Load(path, 1252);
            source.WriteIfChanged();
            File.GetLastWriteTimeUtc(path).Should().Be(before, "a file whose bytes did not change must not be rewritten");
        }
        finally {
            File.Delete(path);
        }
    }
}
