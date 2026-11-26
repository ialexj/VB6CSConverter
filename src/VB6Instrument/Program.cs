#nullable enable
using System.CommandLine;
using System.Text.RegularExpressions;
using VB6Parser;

namespace VB6Instrument;

public static class Program
{
    public sealed class Options
    {
        public string ProjectPath { get; set; } = string.Empty;
        public bool LineNumbers { get; set; }
        public string? CallHeader { get; set; }
        public bool ErrorGuard { get; set; }
        public string? ErrorGuardStatement { get; set; }
        public ErrorPropagation Propagation { get; set; } = ErrorPropagation.Raise;
        public int CodePage { get; set; } = 1252;
        public bool WhatIf { get; set; }
    }

    public sealed record FileRun(string Path, int Members, IReadOnlyList<PassResult> Passes, bool Changed, MapFile? FileMap = null);

    public static async Task<int> Main(string[] args)
    {
        var projectArg = new Argument<string>("project") {
            Description = "Path to the VB6 project (.vbp), a VB6 source file, or a directory of VB6 sources. Files are instrumented in place — stage a copy first if the canonical sources must stay untouched.",
        };
        var lineNumbersOpt = new Option<bool>("--line-numbers", []) {
            Description = "Pass A: add virtual line numbers (Erl) to member bodies (10, 20, 30 ... at the MZ-Tools baseline) and write an instrumentation map (<project>.map).",
        };
        var callHeaderOpt = new Option<string?>("--call-header", []) {
            Description = "Pass B: trace statement prepended to every Sub/Function/Property. {File} and {Member} placeholders, e.g. --call-header \"Debug.Write \"{File}.{Member}\"\".",
        };
        var errorGuardOpt = new Option<string?>("--error-guard", []) {
            Description = "Pass C: add error guards to every member. With a value, the value is the swallow-handler statement template ({File}/{Member} placeholders), e.g. --error-guard \"ErrorHandle \"{file}\", \"{member}()\"\". Without a value the swallow handler emits Err.Clear only.",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var propagationOpt = new Option<ErrorPropagation>("--error-propagation", []) {
            Description = "Re-raise handler strategy. raise (default, probe-verified) decorates with the captured Erl and re-raises explicitly; fallthrough decorates and falls to End (probe result: does not propagate).",
            DefaultValueFactory = _ => ErrorPropagation.Raise,
        };
        var codePageOpt = new Option<int>("--codepage", []) {
            Description = "Code page for BOM-less files (files with a UTF-8 BOM are always read as UTF-8).",
            DefaultValueFactory = _ => 1252,
        };
        var whatIfOpt = new Option<bool>("--whatif", []) {
            Description = "Report what would change without writing any file.",
        };

        var rootCommand = new RootCommand("Add build-time instrumentation (line numbers, call traces, error guards) to a VB6 project.");
        rootCommand.Add(projectArg);
        rootCommand.Add(lineNumbersOpt);
        rootCommand.Add(callHeaderOpt);
        rootCommand.Add(errorGuardOpt);
        rootCommand.Add(propagationOpt);
        rootCommand.Add(codePageOpt);
        rootCommand.Add(whatIfOpt);

        rootCommand.SetAction(parseResult =>
        {
            var guardResult = parseResult.GetResult(errorGuardOpt);
            return Run(new Options {
                ProjectPath = parseResult.GetValue(projectArg)!,
                LineNumbers = parseResult.GetValue(lineNumbersOpt),
                CallHeader = parseResult.GetValue(callHeaderOpt),
                ErrorGuard = guardResult is not null,
                ErrorGuardStatement = guardResult?.GetValueOrDefault<string?>(),
                Propagation = parseResult.GetValue(propagationOpt),
                CodePage = parseResult.GetValue(codePageOpt),
                WhatIf = parseResult.GetValue(whatIfOpt),
            });
        });

        return await rootCommand.Parse(args).InvokeAsync();
    }

    internal static int Run(Options options)
    {
        if (!options.LineNumbers && options.CallHeader is null && !options.ErrorGuard) {
            Console.WriteLine("Nothing to do: pass at least one of --line-numbers, --call-header, --error-guard.");
            return 2;
        }

        List<string> targets;
        VisualBasicProject? project = null;
        try {
            (targets, project) = EnumerateTargets(options.ProjectPath);
        }
        catch (Exception ex) when (ex is not InstrumentationException) {
            Console.WriteLine($"ERROR {options.ProjectPath}: {ex.Message}");
            return 1;
        }

        if (targets.Count == 0) {
            Console.WriteLine("No VB6 source files found.");
            return 1;
        }

        // The .vbp declares each file's type (Module/Class/Form/Control);
        // for other target modes the map falls back to extension-derived
        // types.
        var declaredTypes = project is null
            ? null
            : project.Files
                .GroupBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().Type.ToString(), StringComparer.OrdinalIgnoreCase);

        var changedFiles = 0;
        var unchangedFiles = 0;
        var failures = new List<string>();
        var runs = new List<FileRun>();

        foreach (var target in targets) {
            try {
                string? fileType = null;
                if (declaredTypes is not null && declaredTypes.TryGetValue(target, out var declaredType)) {
                    fileType = declaredType;
                }
                var run = InstrumentFile(target, options, fileType);
                runs.Add(run);
                if (run.Changed) {
                    changedFiles++;
                }
                else {
                    unchangedFiles++;
                }
                Report(run, options.WhatIf);
            }
            catch (InstrumentationException ex) {
                failures.Add(ex.Message);
                Console.WriteLine($"ERROR {ex.Message}");
                unchangedFiles++;
            }
            catch (Exception ex) {
                failures.Add($"{target}: {ex.Message}");
                Console.WriteLine($"ERROR {target}: {ex.Message}");
                unchangedFiles++;
            }
        }

        // The map describes the whole run's state, so it is only written
        // for clean runs (a run with failures exits nonzero and must not
        // leave a partial artifact behind), and never under --whatif.
        if (options.LineNumbers && !options.WhatIf && failures.Count == 0) {
            var map = new InstrumentationMap();
            if (project is not null) {
                map.ProjectName = Path.GetFileName(options.ProjectPath);
                map.Version = project.Version;
                map.Metadata = project.Metadata.Count > 0 ? project.Metadata : null;
            }
            else if (Directory.Exists(options.ProjectPath)) {
                map.ProjectName = Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.ProjectPath)));
            }
            else {
                map.ProjectName = Path.GetFileName(options.ProjectPath);
            }
            var rootDirectory = Directory.Exists(options.ProjectPath)
                ? Path.GetFullPath(options.ProjectPath)
                : Path.GetDirectoryName(Path.GetFullPath(options.ProjectPath))!;
            foreach (var run in runs) {
                if (run.FileMap is not null) {
                    map.AddFile(Path.GetRelativePath(rootDirectory, run.Path).Replace('\\', '/'), run.FileMap);
                }
            }
            var mapPath = InstrumentationMap.GetMapPath(options.ProjectPath);
            File.WriteAllText(mapPath, map.ToJson() + Environment.NewLine);
            Console.WriteLine($"vb6instrument: map written to {mapPath}");
        }

        Console.WriteLine($"vb6instrument: {changedFiles} file(s) changed, {unchangedFiles} unchanged, {failures.Count} error(s).");

        if (failures.Count > 0) {
            Console.WriteLine($"{failures.Count} file(s) failed:");
            foreach (var failure in failures) {
                Console.WriteLine(failure);
            }
            return 1;
        }
        return 0;
    }

    internal static FileRun InstrumentFile(string path, Options options, string? fileType = null)
    {
        var source = SourceFile.Load(path, options.CodePage);
        var model = Vb6FileParser.Parse(source);

        var passes = new List<PassResult>();
        MapFile? fileMap = null;
        if (options.LineNumbers) {
            var lineNumbers = new LineNumbersPass(fileType).Run(model);
            passes.Add(lineNumbers);
            fileMap = lineNumbers.FileMap;
        }
        if (options.CallHeader is not null) {
            passes.Add(new CallTracePass(options.CallHeader).Run(model));
        }
        if (options.ErrorGuard) {
            passes.Add(new ErrorGuardPass(options.ErrorGuardStatement, options.Propagation).Run(model));
        }

        var changed = source.IsChanged;
        if (changed && !options.WhatIf) {
            source.WriteIfChanged();
        }
        return new FileRun(path, model.Members.Count, passes, changed, fileMap);
    }

    private static void Report(FileRun run, bool whatIf)
    {
        var parts = new List<string>();
        foreach (var pass in run.Passes) {
            parts.Add(pass.PassName switch {
                "line-numbers" when pass.LinesChanged > 0 => $"numbered {pass.LinesChanged} line(s) in {pass.MembersTouched} member(s)",
                "line-numbers" => "no numbering changes",
                "call-trace" when pass.LinesChanged > 0 => $"traced {pass.MembersTouched} member(s)",
                "call-trace" => "no trace changes",
                "error-guard" when pass.LinesChanged > 0 => $"guarded {pass.MembersTouched} member(s)",
                "error-guard" => "no guard changes",
                _ => pass.PassName,
            });
            if (pass.MembersSkipped > 0) {
                parts.Add($"{pass.MembersSkipped} skipped");
            }
        }

        var suffix = whatIf
            ? (run.Changed ? " [whatif: would write]" : " [unchanged]")
            : (run.Changed ? " [written]" : " [unchanged]");
        Console.WriteLine($"{run.Path}: {string.Join(", ", parts)}{suffix}");

        foreach (var pass in run.Passes) {
            foreach (var warning in pass.Warnings) {
                Console.WriteLine($"WARNING {run.Path}: {warning}");
            }
        }
    }

    /// <summary>
    /// Resolves the files to instrument. For a .vbp the project model is
    /// returned alongside the targets so the map can carry the project's
    /// version, metadata and declared file types.
    /// </summary>
    private static (List<string> Targets, VisualBasicProject? Project) EnumerateTargets(string path)
    {
        if (File.Exists(path) && path.EndsWith(".vbp", StringComparison.OrdinalIgnoreCase)) {
            var project = VisualBasicProject.Load(path);
            var targets = project.Files
                .Select(file => file.Path)
                .Where(file => Vb6MemberScanner.IsSourceExtension(System.IO.Path.GetExtension(file).ToLowerInvariant()))
                .Where(File.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return (targets, project);
        }

        if (Directory.Exists(path)) {
            return ([
                .. Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                    .Where(file => Vb6MemberScanner.IsSourceExtension(System.IO.Path.GetExtension(file).ToLowerInvariant()))
                    .Where(file => !Regex.IsMatch(file, @"\\(\.git|\.svn)\\"))
            ], null);
        }

        if (File.Exists(path)) {
            var extension = System.IO.Path.GetExtension(path).ToLowerInvariant();
            if (!Vb6MemberScanner.IsSourceExtension(extension)) {
                throw new InstrumentationException(path, $"Not a VB6 source file (.bas .cls .frm .ctl .dob .dsr .pag): {path}");
            }
            return ([path], null);
        }

        throw new InstrumentationException(path, "Path not found.");
    }
}
