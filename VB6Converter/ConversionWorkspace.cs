using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using Serilog;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using VB6Converter.Conversion;
using VB6Converter.Rewriters;

namespace VB6Converter;
public class ConversionWorkspace : IDisposable
{
    readonly ConversionTarget[] _targets;
    readonly MSBuildWorkspace _ws;

    readonly CSharpSyntaxRewriter[] PostRewriters = new[] {
        new UsingsRewriter()
    };  

    public ConversionWorkspace(ConversionTarget[] targets)
    {
        _ws = MSBuildWorkspace.Create();
        _ws.WorkspaceFailed += (s, e) => Log.Default.Warning("Workspace failed: {error}", e.Diagnostic);
        _targets = targets ?? throw new ArgumentNullException(nameof(targets));
    }

    public void Dispose() => _ws.Dispose();

    public Project Project { get; private set; }

    public async Task Open(string outDir, string name)
    {
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
                    <TargetFramework>net9.0</TargetFramework>
                    <LangVersion>latest</LangVersion>
                  </PropertyGroup>
                </Project>
                """);

            // Create a global usings file to replicate VB6's module accessibility
            var globalUsings = CompilationUnitConverter.GetGlobalStaticUsings(_targets.Select(t => $"{name}.{t.File.Name}")).NormalizeWhitespace();
            var globalUsingsPath = Path.Combine(outDir, "_VB6Usings.cs");
            File.WriteAllText(globalUsingsPath, globalUsings.ToFullString());
        }

        Project = await _ws.OpenProjectAsync(projectPath);
    }

    public async ValueTask<bool> RunOperations(string oper, Func<ConversionTarget, CompilationUnitSyntax, ILogger, CancellationToken, ValueTask<CompilationUnitSyntax>> task)
    {
        Log.Default.ForContext("operation", oper).Information($"{oper}...");

        bool hasChanges = false;

        await Parallel.ForEachAsync(_targets, async (target, cancel) => {
            var log = Log.ForFile(target.Name).ForContext("operation", oper);

            try {
                var doc = GetOrCreateDocument(target);
                var st  = await doc.GetSyntaxTreeAsync(cancel);
                var cu  = st.GetCompilationUnitRoot(cancel);

                var newcu = await task(target, cu, log, cancel);

                if (!newcu.IsEquivalentTo(cu)) {
                    doc = await SaveDocument(log, doc, newcu, cancel);
                    Interlocked.Exchange(ref hasChanges, true);
                }
                else {
                    Interlocked.Exchange(ref hasChanges, false);
                }
            }
            catch (Exception ex) when (!Debugger.IsAttached) {
                log.Error(ex, "{file} failed during {title}.");
            }
        });

        return hasChanges;
    }

    public async ValueTask<bool> RunRewrites(string title, Func<CompilationUnitSyntax, Compilation, ILogger, CancellationToken, ValueTask<CompilationUnitSyntax>> task)
    {
        var usings = new UsingsRewriter();

        // Fixing some issues will often unblock additional issues that can only be found on the next compile
        int count = 1;
        bool hasChanges;
        do {
            var compilation = await GetCompilation();

            hasChanges = await RunOperations(title + $" ({count++})", async (t, cu, log, cancel) => {
                var newcu = await task(cu, compilation, log, cancel);

                // Update                 
                if (string.IsNullOrEmpty(newcu.SyntaxTree.FilePath)) {
                    var st = newcu.SyntaxTree.WithFilePath(t.OutputPath);
                    newcu = st.GetCompilationUnitRoot();
                }

                // Run additional rewriters to update global state
                // to update things like usings, etc.
                foreach (var post in PostRewriters) {
                    newcu = (CompilationUnitSyntax)post.Visit(newcu);
                }

                return newcu;
            });
        }
        while (hasChanges);
        return hasChanges;
    }

    public async Task<Compilation> GetCompilation()
    {
        Log.Default.Information("Compiling...");
        return await Project.GetCompilationAsync();
    }


    Document GetOrCreateDocument(ConversionTarget target)
    {
        lock (_ws) {
            var doc = Project.Documents.FirstOrDefault(d => string.Equals(d.Name, target.OutputDocumentName, StringComparison.CurrentCultureIgnoreCase));
            
            if (doc is null) {
                doc = Project.AddDocument(target.OutputDocumentName, string.Empty);
                Project = doc.Project;
            }

            return doc;
        }
    }

    async Task<Document> SaveDocument(ILogger log, Document doc, CompilationUnitSyntax cu, CancellationToken cancel)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(doc.FilePath));

        await File.WriteAllTextAsync(doc.FilePath, cu.ToFullString(), cancel);

        lock (_ws) {
            doc = doc.WithSyntaxRoot(cu);
            Project = doc.Project;
        }

        log.Debug("{file} saved.");
        return doc;
    }
}
