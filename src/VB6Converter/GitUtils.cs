using System;
using System.Diagnostics;
using System.Threading.Tasks;

public static class GitUtils
{
    // ─────────────────────────────────────────────────────────────────────
    // Git integration — commits the output directory after each rewriter pass
    // when --git is specified. Repository initialization and .gitignore setup
    // are the caller's responsibility; the output directory must already be
    // inside a git repository.
    // ─────────────────────────────────────────────────────────────────────

    public static async Task EnsureGitAvailable(string outputDir)
    {
        (int ExitCode, string StdOut, string StdErr) versionResult;
        try {
            versionResult = await RunGitProcess(null, "--version");
        }
        catch (System.ComponentModel.Win32Exception ex) {
            throw new InvalidOperationException("--git was specified but 'git' was not found on PATH.", ex);
        }

        if (versionResult.ExitCode != 0) {
            throw new InvalidOperationException($"--git was specified but 'git --version' failed: {versionResult.StdErr}");
        }

        var repoCheck = await RunGitProcess(outputDir, "rev-parse", "--is-inside-work-tree");
        if (repoCheck.ExitCode != 0 || !repoCheck.StdOut.Trim().Equals("true", StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidOperationException(
                $"--git was specified but '{outputDir}' is not inside a git repository. " +
                $"Initialize one (e.g. 'git init \"{outputDir}\"') before running with --git.");
        }
    }

    public static async Task Commit(string outputDir, string message)
    {
        var add = await RunGitProcess(outputDir, "add", "-A");
        if (add.ExitCode != 0) {
            throw new InvalidOperationException($"git add failed in '{outputDir}': {add.StdErr}");
        }

        var commit = await RunGitProcess(outputDir, "commit", "--quiet", "-m", message);
        if (commit.ExitCode != 0) {
            throw new InvalidOperationException($"git commit failed in '{outputDir}': {commit.StdErr}{commit.StdOut}");
        }
    }

    static async Task<(int ExitCode, string StdOut, string StdErr)> RunGitProcess(string workingDir, params string[] args)
    {
        var psi = new ProcessStartInfo {
            FileName = "git",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        if (workingDir is not null) {
            psi.ArgumentList.Add("-C");
            psi.ArgumentList.Add(workingDir);
        }
        foreach (var arg in args) {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi)!;

        var stdOutTask = process.StandardOutput.ReadToEndAsync();
        var stdErrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return (process.ExitCode, await stdOutTask, await stdErrTask);
    }
}
