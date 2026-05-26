#nullable enable
using Serilog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace ComStubGenerator;

/// <summary>
/// Invokes the ComQuery.exe external process for a specific architecture and
/// deserializes the resulting JSON array of <see cref="ComQueryLibrary"/> records.
/// </summary>
public static class ComQueryClient
{
    static readonly JsonSerializerOptions JsonOptions = new() {
        Converters = { new JsonStringEnumConverter() },
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Invokes <c>ComQuery.exe</c> for <paramref name="arch"/> with the supplied
    /// <paramref name="libArgs"/> and returns the deserialized result, or
    /// <see langword="null"/> if the executable is not found or the process fails.
    /// </summary>
    /// <param name="arch">Architecture string: <c>"x86"</c> or <c>"x64"</c>.</param>
    /// <param name="libArgs">
    /// Arguments to pass as <c>--lib=&lt;value&gt;</c>.
    /// Each element is appended as a separate <c>--lib</c> option.
    /// </param>
    public static async Task<ComQueryLibrary[]?> QueryAsync(string arch, IEnumerable<string> libArgs)
    {
        string exePath = FindComQueryExe(arch);
        if (!File.Exists(exePath)) {
            Log.Default.Warning("ComQueryClient: ComQuery.exe for {arch} not found at {path}", arch, exePath);
            return null;
        }

        var psi = new ProcessStartInfo {
            FileName = exePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        var libList = libArgs.ToList();
        foreach (var lib in libList) {
            psi.ArgumentList.Add("--lib");
            psi.ArgumentList.Add(lib);
        }

        using var process = Process.Start(psi)!;

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();
        string stdout = await stdoutTask;
        string stderr = await stderrTask;

        if (!string.IsNullOrWhiteSpace(stderr)) {
            foreach (var line in stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                Log.Default.Warning("ComQuery ({arch}): {line}", arch, line.Trim());
        }

        if (process.ExitCode != 0) {
            Log.Default.Warning("ComQuery ({arch}) exited with code {code}", arch, process.ExitCode);
        }

        if (string.IsNullOrWhiteSpace(stdout)) return [];

        try {
            return JsonSerializer.Deserialize<ComQueryLibrary[]>(stdout, JsonOptions) ?? [];
        }
        catch (JsonException ex) {
            Log.Default.Warning(ex, "ComQueryClient: failed to deserialize JSON output from {arch}", arch);
            return null;
        }
    }

    static string FindComQueryExe(string arch)
    {
        string baseDir = AppContext.BaseDirectory;
        return Path.Combine(baseDir, "comquery", arch, "ComQuery.exe");
    }
}
