using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;
using VB6Converter.Rewriters;
using VB6Parser;

namespace VB6Converter.Tests.Rewrites;

[TestClass]
public class DesignerFileSplitterTests
{
    static (CompilationUnitSyntax Main, CompilationUnitSyntax? Designer) ConvertAndSplit(string vb, string name = "MyForm")
    {
        var conversion = VB6ToCSharpConversion.ConvertString(vb, name, type: VisualBasicFileType.Form);
        conversion.ParseErrors.Should().BeEmpty();
        conversion.SyntaxErrors.Should().BeEmpty();
        return DesignerFileSplitter.Split(conversion.CompilationUnit);
    }

    [TestMethod]
    public void NoControlProperties_ReturnsOriginalWithNullDesigner()
    {
        var vb = """
            Private Sub Test()
                Dim x As Integer
                x = 1
            End Sub
            """;

        var conversion = VB6ToCSharpConversion.ConvertString(vb, "MyForm", type: VisualBasicFileType.Form);
        var (main, designer) = DesignerFileSplitter.Split(conversion.CompilationUnit);

        designer.Should().BeNull();
        main.Should().BeSameAs(conversion.CompilationUnit);
    }

    [TestMethod]
    public void WithControlProperties_ProducesDesignerFile()
    {
        var vb = """
            Begin VB.Form MyForm
                Caption = "Test"
                Begin VB.Label Label1
                    Caption = "Hello"
                End
            End
            Private Sub Command1_Click()
                MsgBox "Hello"
            End Sub
            """;

        var (main, designer) = ConvertAndSplit(vb);

        designer.Should().NotBeNull();
    }

    [TestMethod]
    public void MainClass_HasNoBaseType()
    {
        var vb = """
            Begin VB.Form MyForm
                Caption = "Test"
            End
            Private Sub Command1_Click()
                MsgBox "Hello"
            End Sub
            """;

        var (main, _) = ConvertAndSplit(vb);

        var mainClass = main.DescendantNodes().OfType<ClassDeclarationSyntax>().Single();
        mainClass.BaseList.Should().BeNull();
    }

    [TestMethod]
    public void DesignerClass_HasBaseType()
    {
        var vb = """
            Begin VB.Form MyForm
                Caption = "Test"
            End
            Private Sub Command1_Click()
                MsgBox "Hello"
            End Sub
            """;

        var (_, designer) = ConvertAndSplit(vb);

        var designerClass = designer!.DescendantNodes().OfType<ClassDeclarationSyntax>().Single();
        designerClass.BaseList.Should().NotBeNull();
    }

    [TestMethod]
    public void MainClass_ContainsCodeBehindMethod()
    {
        var vb = """
            Begin VB.Form MyForm
                Caption = "Test"
            End
            Private Sub Command1_Click()
                MsgBox "Hello"
            End Sub
            """;

        var (main, _) = ConvertAndSplit(vb);

        var mainClass = main.DescendantNodes().OfType<ClassDeclarationSyntax>().Single();
        mainClass.Members.OfType<MethodDeclarationSyntax>()
            .Should().Contain(m => m.Identifier.Text == "Command1_Click");
    }

    [TestMethod]
    public void MainClass_DoesNotContainInstanceOrInitialize()
    {
        var vb = """
            Begin VB.Form MyForm
                Caption = "Test"
            End
            Private Sub Command1_Click()
                MsgBox "Hello"
            End Sub
            """;

        var (main, _) = ConvertAndSplit(vb);

        var mainClass = main.DescendantNodes().OfType<ClassDeclarationSyntax>().Single();
        mainClass.Members.OfType<FieldDeclarationSyntax>()
            .SelectMany(f => f.Declaration.Variables)
            .Should().NotContain(v => v.Identifier.Text == "_Instance");
        mainClass.Members.OfType<MethodDeclarationSyntax>()
            .Should().NotContain(m => m.Identifier.Text == "InitializeComponent");
    }

    [TestMethod]
    public void DesignerClass_ContainsInstanceFieldAndInitializeComponent()
    {
        var vb = """
            Begin VB.Form MyForm
                Caption = "Test"
            End
            Private Sub Command1_Click()
                MsgBox "Hello"
            End Sub
            """;

        var (_, designer) = ConvertAndSplit(vb);

        var designerClass = designer!.DescendantNodes().OfType<ClassDeclarationSyntax>().Single();
        designerClass.Members.OfType<FieldDeclarationSyntax>()
            .SelectMany(f => f.Declaration.Variables)
            .Should().Contain(v => v.Identifier.Text == "_Instance");
        designerClass.Members.OfType<MethodDeclarationSyntax>()
            .Should().Contain(m => m.Identifier.Text == "InitializeComponent");
    }

    [TestMethod]
    public void DesignerClass_DoesNotContainCodeBehindMethod()
    {
        var vb = """
            Begin VB.Form MyForm
                Caption = "Test"
            End
            Private Sub Command1_Click()
                MsgBox "Hello"
            End Sub
            """;

        var (_, designer) = ConvertAndSplit(vb);

        var designerClass = designer!.DescendantNodes().OfType<ClassDeclarationSyntax>().Single();
        designerClass.Members.OfType<MethodDeclarationSyntax>()
            .Should().NotContain(m => m.Identifier.Text == "Command1_Click");
    }

    [TestMethod]
    public void DesignerClass_HasSameClassNameAsMain()
    {
        var vb = """
            Begin VB.Form MyForm
                Caption = "Test"
            End
            """;

        var (main, designer) = ConvertAndSplit(vb);

        var mainClass = main.DescendantNodes().OfType<ClassDeclarationSyntax>().Single();
        var designerClass = designer!.DescendantNodes().OfType<ClassDeclarationSyntax>().Single();
        designerClass.Identifier.Text.Should().Be(mainClass.Identifier.Text);
    }

    [TestMethod]
    public void DesignerClass_HasGeneratedCodeAttribute()
    {
        var vb = """
            Begin VB.Form MyForm
                Caption = "Test"
            End
            """;

        var (_, designer) = ConvertAndSplit(vb);

        var designerClass = designer!.DescendantNodes().OfType<ClassDeclarationSyntax>().Single();
        designerClass.AttributeLists
            .SelectMany(al => al.Attributes)
            .Should().Contain(a => a.Name.ToString().Contains("GeneratedCode"));
    }

    [TestMethod]
    public void DesignerClass_ContainsControlField()
    {
        var vb = """
            Begin VB.Form MyForm
                Caption = "Test"
                Begin VB.Label Label1
                    Caption = "Hello"
                End
            End
            """;

        var (_, designer) = ConvertAndSplit(vb);

        var designerClass = designer!.DescendantNodes().OfType<ClassDeclarationSyntax>().Single();
        designerClass.Members.OfType<FieldDeclarationSyntax>()
            .SelectMany(f => f.Declaration.Variables)
            .Should().Contain(v => v.Identifier.Text == "Label1");
    }

    [TestMethod]
    public void DesignerFile_HasNoRegionDirective()
    {
        var vb = """
            Begin VB.Form MyForm
                Caption = "Test"
            End
            """;

        var (_, designer) = ConvertAndSplit(vb);

        var text = designer!.NormalizeWhitespace().ToFullString();
        text.Should().NotContain("#region");
    }

    [TestMethod]
    public void MainFile_HasNoRegionOrEndRegionDirective()
    {
        var vb = """
            Begin VB.Form MyForm
                Caption = "Test"
            End
            Private Sub Command1_Click()
                MsgBox "Hello"
            End Sub
            """;

        var (main, _) = ConvertAndSplit(vb);

        var text = main.NormalizeWhitespace().ToFullString();
        text.Should().NotContain("#region");
        text.Should().NotContain("#endregion");
    }
}
