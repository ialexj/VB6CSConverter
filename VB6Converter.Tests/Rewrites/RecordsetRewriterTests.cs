using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Build.Framework;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using VB6Converter.Rewriters;
using VB6Converter.Rewriters.Semantic;


namespace VB6Converter.Tests.Rewrites;

[TestClass]
public class RecordsetRewriterTests
{
    [TestMethod]
    public void Test()
    {
        CheckRewrite(
            """
            Recordset Tabela = default;
            Tabela = dbo.OpenRecordset("Existencias", dbOpenTable);
            Tabela["extCreator"] = "Test ABC";
            ExistenciaID = Tabela["extID"];
            """,
            """
            Recordset Tabela = default;
            Tabela = dbo.OpenRecordset("Existencias", RecordsetTypeEnum.dbOpenTable);
            Tabela.Fields["extCreator"] .Value= "Test ABC";
            ExistenciaID = Tabela.Fields["extID"].Value;
            """);
            
    }


    private static void CheckRewrite(string cs, string expected)
    {
        var cu = SyntaxFactory.ParseCompilationUnit(cs);
        var comp = CSharpCompilation.Create("Test",
            [cu.SyntaxTree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);

        var semantics = comp.GetSemanticModel(cu.SyntaxTree, true);
        var rewriter = new DAORewriter(semantics);

        var newCu = rewriter.Visit(cu);
        newCu.ToFullString().Should().Be(expected);
    }
}
