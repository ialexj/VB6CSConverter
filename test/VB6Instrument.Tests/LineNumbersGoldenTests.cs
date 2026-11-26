#nullable enable
using System.Text;
using AwesomeAssertions;

namespace VB6Instrument.Tests;

/// <summary>
/// Byte-compatibility acceptance: the line-numbers pass must reproduce the
/// incumbent Set-Vb6LineNumbers.ps1 (Add mode) output byte for byte. The
/// Expected fixtures were produced by running both tools over the same
/// inputs and verifying byte equality (they are the tool's own outputs,
/// which match the incumbent's).
/// </summary>
[TestClass]
public class LineNumbersGoldenTests
{
    public static IEnumerable<object[]> FixtureNames()
    {
        var inputDirectory = FixtureDirectory("Input");
        foreach (var file in Directory.EnumerateFiles(inputDirectory)) {
            yield return [Path.GetFileName(file)];
        }
    }

    [DataTestMethod]
    [DynamicData(nameof(FixtureNames), DynamicDataSourceType.Method)]
    public void OutputMatchesIncumbentToolByteForByte(string fileName)
    {
        var inputPath = Path.Combine(FixtureDirectory("Input"), fileName);
        var expectedPath = Path.Combine(FixtureDirectory("Expected"), fileName);

        var output = InstrumentInTemp(inputPath, Path.GetFileName(fileName));

        var expectedBytes = File.ReadAllBytes(expectedPath);
        var actualBytes = Encoding.GetEncoding(1252).GetBytes(output);
        if (HasUtf8Bom(File.ReadAllBytes(inputPath))) {
            actualBytes = [.. new UTF8Encoding(true).GetPreamble(), .. new UTF8Encoding(true).GetBytes(output)];
        }
        actualBytes.Should().BeEquivalentTo(expectedBytes, because: $"{fileName} must byte-match the incumbent numbering tool");
    }

    private static string FixtureDirectory(string name)
    {
        var candidate = Path.Combine(AppContext.BaseDirectory, "Fixtures", "LineNumbers", name);
        Directory.Exists(candidate).Should().BeTrue($"fixture directory missing: {candidate}");
        return candidate;
    }

    private static bool HasUtf8Bom(byte[] bytes) =>
        bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;

    private static string InstrumentInTemp(string inputPath, string fileName)
    {
        var directory = Path.Combine(Path.GetTempPath(), "vb6instrument-goldens");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        File.Copy(inputPath, path, overwrite: true);
        try {
            var source = SourceFile.Load(path, 1252);
            var model = Vb6FileParser.Parse(source);
            new LineNumbersPass().Run(model);
            return source.BuildText();
        }
        finally {
            File.Delete(path);
        }
    }
}
