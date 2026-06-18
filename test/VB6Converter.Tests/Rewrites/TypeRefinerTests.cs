using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Concurrent;
using VB6Converter.Rewriters.Semantic;

namespace VB6Converter.Tests.Rewrites;

[TestClass]
public class TypeRefinerTests
{
    [TestMethod]
    public async Task RefinesConstDynamicFromInitializer()
    {
        const string source = "class T { const dynamic A = 1; }";
        const string expected = "class T { const int A = 1; }";

        var rewritten = await RewriteWithTypeRefiner(source);
        Normalize(rewritten).Should().Be(Normalize(expected));
    }

    [TestMethod]
    public async Task RefinesChainedConstDynamicAcrossPasses()
    {
        const string source = "class T { const dynamic A = 1; const dynamic B = A | 2; }";
        const string expected = "class T { const int A = 1; const int B = A | 2; }";

        var firstPass = await RewriteWithTypeRefiner(source);
        var secondPass = await RewriteWithTypeRefiner(firstPass);

        Normalize(secondPass).Should().Be(Normalize(expected));
    }

    private static async Task<string> RewriteWithTypeRefiner(string cs)
    {
        using var workspace = new AdhocWorkspace();

        var projectInfo = ProjectInfo.Create(
            ProjectId.CreateNewId(),
            VersionStamp.Create(),
            "TestProject",
            "TestProject",
            LanguageNames.CSharp)
            .WithMetadataReferences([
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location)
            ])
            .WithCompilationOptions(
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var project = workspace.AddProject(projectInfo);
        var document = project.AddDocument("Test.cs", cs);
        var semantics = await document.GetSemanticModelAsync();
        var root = (CompilationUnitSyntax)(await document.GetSyntaxRootAsync())!;

        var varTypes = new ConcurrentDictionary<VariableDeclaratorSyntax, TypeSyntax>();
        await TypeRefiner.GetAllVariablesAndUsages(varTypes, semantics!, document.Project.Solution);

        var rewriter = new TypeRefiner(varTypes);
        var rewritten = (CompilationUnitSyntax)rewriter.Visit(root)!;

        return rewritten.ToFullString();
    }

    private static string Normalize(string cs)
        => SyntaxFactory.ParseCompilationUnit(cs).NormalizeWhitespace().ToFullString();
}
