using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Build.Locator;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VB6Converter.Conversion;
using VB6Parser;

namespace VB6Converter;

public sealed class ConversionWorkspace : IDisposable
{
    static readonly object _msbuildLock = new();

    readonly MSBuildWorkspace _ws;

    public ConversionWorkspace()
    {
        EnsureMsBuildRegistered();

        _ws = MSBuildWorkspace.Create();
        _ws.RegisterWorkspaceFailedHandler(e => Log.Default.Warning("Workspace failed: {error}", e.Diagnostic));
    }

    static void EnsureMsBuildRegistered()
    {
        lock (_msbuildLock) {
            if (MSBuildLocator.IsRegistered) {
                return;
            }

            var instances = MSBuildLocator.QueryVisualStudioInstances()
                .OrderByDescending(i => i.Version)
                .ToArray();

            if (instances.Length > 0) {
                var instance = instances[0];
                MSBuildLocator.RegisterInstance(instance);
                Log.Default.Information("Using MSBuild from {path} ({version})", instance.MSBuildPath, instance.Version);
            }
            else {
                var instance = MSBuildLocator.RegisterDefaults();
                Log.Default.Information("Using MSBuild from {path} ({version})", instance.MSBuildPath, instance.Version);
            }
        }
    }

    public void Dispose() => _ws.Dispose();


    public Project Project { get; private set; }

    public string Name { get; private set; }

    public ConversionOptions Options { get; set; } = ConversionOptions.Default;

    public ICollection<string> Filter { get; set; } = new List<string>();

    public string DefaultNamespace { get; private set; }

    public string OutputDir => Path.GetDirectoryName(Project.FilePath);

    public string MissingTypesPath => Path.Combine(Path.GetDirectoryName(Project.FilePath), "MissingTypes");

    public string ReferenceStubsPath => Path.Combine(Path.GetDirectoryName(Project.FilePath), "_Reference");


    public IReadOnlyCollection<ConversionTarget> Targets { get; private set; }

    public IReadOnlyCollection<ConversionTarget> ActiveTargets { get; private set; }

    public void SetActiveFilter(ICollection<string> filter)
    {
        Filter = filter ?? [];

        if (filter.Count == 0) {
            ActiveTargets = Targets;
            return;
        }
        else {
            var targets = new List<ConversionTarget>();

            foreach (var f in filter) {
                targets.Add(Targets.FirstOrDefault(t => t.File.Name.Equals(f, StringComparison.CurrentCultureIgnoreCase))
                    ?? throw new ArgumentException($"Target not found: {f}"));
            }

            ActiveTargets = targets;
        }
    }

    public async Task Open(IReadOnlyCollection<ConversionTarget> targets, string outDir, string name)
    {
        Name = name;

        Targets = targets ?? throw new ArgumentNullException(nameof(targets));

        outDir = Path.GetFullPath(outDir);
        if (!Directory.Exists(outDir)) {
            Directory.CreateDirectory(outDir);
        }

        // Create project
        string projectPath = Path.Combine(outDir, $"{name}.csproj");
        if (!File.Exists(projectPath)) {
            File.WriteAllText(projectPath, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net10.0-windows</TargetFramework>
                    <LangVersion>latest</LangVersion>
                    <UseWindowsForms>true</UseWindowsForms>
                  </PropertyGroup>
                </Project>
                """);

            DefaultNamespace = name;
        }

        Project = await _ws.OpenProjectAsync(projectPath);
    }

    /// <summary>
    /// Opens an existing C# project from a .csproj file path without requiring
    /// ConversionTargets or a VB6 project name. Used by the standalone
    /// diagnostics command to load already-converted output.
    /// </summary>
    public async Task Open(string csprojPath)
    {
        csprojPath = Path.GetFullPath(csprojPath);
        if (!File.Exists(csprojPath)) {
            throw new FileNotFoundException("Project file not found.", csprojPath);
        }

        Targets = [];
        ActiveTargets = [];
        Project = await _ws.OpenProjectAsync(csprojPath);
    }

    public async Task<Project> ReloadProject()
    {
        _ws.CloseSolution();
        Project = await _ws.OpenProjectAsync(Project.FilePath);
        return Project;
    }


    public Compilation Compilation { get; private set; }

    public async Task Compile()
    {
        await ReloadProject();
        Compilation = await Project.GetCompilationAsync();
    }

    public IEnumerable<Document> Documents => Project.Documents;

    public IEnumerable<Document> GetActiveDocuments()
    {
        if (Filter.Count == 0) {
            return Documents;
        }
        else {
            return Documents.Where(d =>
                Filter.Contains(GetDocumentBaseName(d), StringComparer.OrdinalIgnoreCase)
            );
        }
    }

    static string GetDocumentBaseName(Document d)
    {
        if (string.IsNullOrEmpty(d.FilePath)) {
            return d.Name;
        }
        else if (d.FilePath.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase)) {
            return Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(d.FilePath)[..^".designer".Length]);
        }
        else {
            return Path.GetFileNameWithoutExtension(d.FilePath);
        }
    }


    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1068:CancellationToken parameters must come last", Justification = "This is a wrapper for a callback, which works best last.")]
    public async ValueTask<bool> WithCompilationUnit(
        ConversionTarget target, CancellationToken cancel,
        Func<CompilationUnitSyntax, ValueTask<CompilationUnitSyntax>> task)
    {
        var doc = GetOrCreateDocument(target);

        return await WithCompilationUnit(doc, cancel, async cu =>
        {
            var newcu = await task(cu);

            if (string.IsNullOrEmpty(newcu.SyntaxTree.FilePath)) {
                var st = newcu.SyntaxTree.WithFilePath(target.OutputPath);
                newcu = st.GetCompilationUnitRoot(cancel);
            }

            return newcu;
        });
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1068:CancellationToken parameters must come last", Justification = "This is a wrapper for a callback, which works best last.")]
    public async ValueTask<bool> WithCompilationUnit(
        Document doc, CancellationToken cancel,
        Func<CompilationUnitSyntax, ValueTask<CompilationUnitSyntax>> task)
    {
        var st = await doc.GetSyntaxTreeAsync(cancel);
        var cu = st.GetCompilationUnitRoot(cancel);

        var newcu = await task(cu);
        newcu = newcu.NormalizeWhitespace();

        if (!RoslynHelpers.IsEquivalentSyntax(cu, newcu)) {
            await SaveDocument(doc, newcu, cancel);
            return true;
        }
        else {
            return false;
        }
    }

    public IEnumerable<string> GetForms() => Targets.Where(t => t.File.Type == VisualBasicFileType.Form).Select(t => t.File.Name);

    public void AddToActiveTargets(IEnumerable<ConversionTarget> targets)
    {
        ActiveTargets = [.. ActiveTargets, .. targets];
    }

    Document GetOrCreateDocument(ConversionTarget target)
    {
        lock (_ws) {
            var targetPath = Path.GetFullPath(target.OutputPath);

            var doc = Project.Documents.FirstOrDefault(d => !string.IsNullOrEmpty(d.FilePath)
                && string.Equals(Path.GetFullPath(d.FilePath), targetPath, StringComparison.CurrentCultureIgnoreCase));

            if (doc is null) {
                doc = Project.Documents.FirstOrDefault(d => string.IsNullOrEmpty(d.FilePath)
                    && string.Equals(d.Name, target.OutputDocumentName, StringComparison.CurrentCultureIgnoreCase));
            }

            if (doc is null) {
                doc = Project.AddDocument(target.OutputDocumentName, string.Empty, filePath: target.OutputPath);
                Project = doc.Project;
            }

            return doc;
        }
    }

    async Task<Document> SaveDocument(Document doc, CompilationUnitSyntax cu, CancellationToken cancel)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(doc.FilePath));

        await File.WriteAllTextAsync(doc.FilePath, cu.ToFullString(), cancel);
        doc = doc.WithText(cu.GetText());

        lock (_ws) {
            Project = doc.Project;
        }

        return doc;
    }


}
