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

    public string DefaultNamespace { get; private set; }

    public string OutputPath => Path.GetDirectoryName(Project.FilePath);

    public string MissingTypesPath => Path.Combine(Path.GetDirectoryName(Project.FilePath), "MissingTypes");

    public string ReferenceStubsPath => Path.Combine(Path.GetDirectoryName(Project.FilePath), "_Reference");


    public IReadOnlyCollection<ConversionTarget> Targets { get; private set; }

    public IReadOnlyCollection<ConversionTarget> ActiveTargets { get; private set; }

    public void SetActiveFilter(IReadOnlyCollection<string> filter)
    {
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

        // Create a global usings file to replicate VB6's module accessibility
        var globalUsingsPath = Path.Combine(outDir, "_VB6Usings.cs");
        if (!File.Exists(globalUsingsPath)) {
            var globalUsings = CompilationUnitConverter.GetGlobalStaticUsings().NormalizeWhitespace();
            File.WriteAllText(globalUsingsPath, globalUsings.ToFullString());
        }

        Project = await _ws.OpenProjectAsync(projectPath);
    }

    public async Task<Project> ReloadProject()
    {
        _ws.CloseSolution();
        Project = await _ws.OpenProjectAsync(Project.FilePath);
        return Project;
    }


    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1068:CancellationToken parameters must come last", Justification = "This is a wrapper for a callback, which works best last.")]
    public async ValueTask<bool> WithCompilationUnit(
        ConversionTarget target, CancellationToken cancel,
        Func<CompilationUnitSyntax, ValueTask<CompilationUnitSyntax>> task)
    {
        var log = Log.ForFile(target.Name);
        var doc = GetOrCreateDocument(target);
        var st  = await doc.GetSyntaxTreeAsync(cancel);
        var cu  = st.GetCompilationUnitRoot(cancel);

        var newcu = await task(cu);

        if (string.IsNullOrEmpty(newcu.SyntaxTree.FilePath)) {
            st = newcu.SyntaxTree.WithFilePath(target.OutputPath);
            newcu = st.GetCompilationUnitRoot(cancel);
        }

        if (!RoslynHelpers.IsEquivalentSyntax(cu, newcu)) {
            doc = await SaveDocument(doc, newcu, cancel);
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
