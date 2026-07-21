using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Runtime.CompilerServices;
using System.Text;
using VB6Converter.Rewriters;
using VB6Parser;

namespace VB6Converter.Tests;

public static class Validations
{
    public static void ValidateClassMatches(string vb, string cs, [CallerMemberName] string? name = null)
    {
        var cu = ConversionShouldSucceed(vb, name);

        ValidateStringsMatch(cs, cu.Class.NormalizeWhitespace().ToFullString());
    }

    /// <summary>
    /// Like <see cref="ValidateClassMatches(string, string, string?)"/>, but additionally
    /// applies one or more non-semantic rewriters (e.g. <see cref="VBCoreRewriter"/>,
    /// <see cref="VBLiteralRewriter"/>) to the converted compilation unit before comparing.
    /// These rewriters now run as part of the post-conversion fixup/semantic loop rather
    /// than during initial conversion, so tests that exercise them must apply them explicitly.
    /// </summary>
    public static void ValidateClassMatches(string vb, string cs, LoggedRewriter rewriter, [CallerMemberName] string? name = null)
    {
        var cu = ApplyRewriter(ConversionShouldSucceed(vb, name), rewriter);

        ValidateStringsMatch(cs, cu.Class.NormalizeWhitespace().ToFullString());
    }

    public static void ValidateMemberMatches(string vb, string cs, [CallerMemberName] string? name = null)
    {
        var cu = ConversionShouldSucceed(vb, name);
        ValidateStringsMatch(cs, cu.Class.Members.OfType<MemberDeclarationSyntax>()
            .Should().ContainSingle().Which
                .NormalizeWhitespace().ToFullString());
    }

    /// <inheritdoc cref="ValidateClassMatches(string, string, LoggedRewriter, string?)"/>
    public static void ValidateMemberMatches(string vb, string cs, LoggedRewriter rewriter, [CallerMemberName] string? name = null)
    {
        var cu = ApplyRewriter(ConversionShouldSucceed(vb, name), rewriter);
        ValidateStringsMatch(cs, cu.Class.Members.OfType<MemberDeclarationSyntax>()
            .Should().ContainSingle().Which
                .NormalizeWhitespace().ToFullString());
    }

    static VB6ToCSharpConversion ApplyRewriter(VB6ToCSharpConversion cu, LoggedRewriter rewriter)
        => cu with { CompilationUnit = (CompilationUnitSyntax)rewriter.Visit(cu.CompilationUnit) };

    static VB6ToCSharpConversion ApplyRewriters(VB6ToCSharpConversion cu, LoggedRewriter rewriter1, LoggedRewriter rewriter2)
        => ApplyRewriter(ApplyRewriter(cu, rewriter1), rewriter2);

    public static VB6ToCSharpConversion ConversionShouldSucceed(string vb, [CallerMemberName] string? name = null)
    {
        var cu = VB6ToCSharpConversion.ConvertString(vb, name);
        try {
            cu.ParseErrors.Should().BeEmpty();
            cu.TransformErrors.Should().BeEmpty();
            cu.SyntaxErrors.Should().BeEmpty();

            System.Diagnostics.Debug.WriteLine(cu.CompilationUnit.NormalizeWhitespace());
            return cu;
        }
        finally {
            if (cu.ParseErrors.Any()) {
                foreach (var error in cu.ParseErrors) {
                    System.Diagnostics.Debug.WriteLine(error.ToString());
                }

                if (cu.Parse.Parser != null) {
                    System.Diagnostics.Debug.WriteLine("============ Parse Tree =============");

                    using var writer = new StringWriter();
                    cu.Parse.Parser.WriteTree(writer);
                    System.Diagnostics.Debug.WriteLine(writer.ToString());
                }

                if (cu.Parse.Tokens != null) {
                    System.Diagnostics.Debug.WriteLine("============ Tokens =============");

                    using var writer = new StringWriter();
                    cu.Parse.Tokens.WriteTokens(cu.Parse.Lexer.Vocabulary, writer);
                    System.Diagnostics.Debug.WriteLine(writer.ToString());
                }
            }

            if (cu.TransformErrors.Count > 0) {
                System.Diagnostics.Debug.WriteLine("============ Transform Errors =============");

                foreach (var error in cu.TransformErrors) {
                    System.Diagnostics.Debug.WriteLine(error.ErrorTree);
                }
            }

            System.Diagnostics.Debug.WriteLine("============ Source =============");
            System.Diagnostics.Debug.WriteLine(cu.Parse.Source);
        }
    }

    public static void ValidateBodyMatches(string vb, string cs, [CallerMemberName] string? name = null)
    {
        var cu = ConversionShouldSucceed(GetBodyWrapper(vb), name);
        ValidateStringsMatch(cs, GetBodyText(cu));
    }

    /// <inheritdoc cref="ValidateClassMatches(string, string, LoggedRewriter, string?)"/>
    public static void ValidateBodyMatches(string vb, string cs, LoggedRewriter rewriter, [CallerMemberName] string? name = null)
    {
        var cu = ApplyRewriter(ConversionShouldSucceed(GetBodyWrapper(vb), name), rewriter);
        ValidateStringsMatch(cs, GetBodyText(cu));
    }

    /// <inheritdoc cref="ValidateClassMatches(string, string, LoggedRewriter, string?)"/>
    public static void ValidateBodyMatches(string vb, string cs, LoggedRewriter rewriter1, LoggedRewriter rewriter2, [CallerMemberName] string? name = null)
    {
        var cu = ApplyRewriters(ConversionShouldSucceed(GetBodyWrapper(vb), name), rewriter1, rewriter2);
        ValidateStringsMatch(cs, GetBodyText(cu));
    }

    static string GetBodyWrapper(string vb) => $"""
        Sub Test()
            {vb}
        End Sub
        """;

    static string GetBodyText(VB6ToCSharpConversion cu)
    {
        var met = (MethodDeclarationSyntax)cu.Class.Members[0].NormalizeWhitespace();

        var sb = new StringBuilder();
        foreach (var m in met.Body!.Statements) {
            sb.AppendLine(m.NormalizeWhitespace().ToFullString());
        }

        return sb.ToString();
    }

    static void ValidateStringsMatch(string expected, string actual)
    {
        actual = actual.ReplaceLineEndings(Environment.NewLine).TrimEnd(Environment.NewLine.ToCharArray());
        expected = expected.ReplaceLineEndings(Environment.NewLine).TrimEnd(Environment.NewLine.ToCharArray());
        actual.Should().Be(expected);
    }

}
