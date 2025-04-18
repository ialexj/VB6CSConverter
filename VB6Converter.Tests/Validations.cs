using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Runtime.CompilerServices;
using System.Text;
using VB6Parser;

namespace VB6Converter.Tests;

public static class Validations
{
    public static void ValidateClassMatches(string vb, string cs, [CallerMemberName] string? name = null)
    {
        var cu = ValidateShouldNotFail(vb, name);
        ValidateStringsMatch(cs, cu.Class.NormalizeWhitespace().ToFullString());
    }

    public static void ValidateMemberMatches(string vb, string cs, [CallerMemberName] string? name = null)
    {
        var cu = ValidateShouldNotFail(vb, name);
        ValidateStringsMatch(cs, cu.Class.Members.OfType<MemberDeclarationSyntax>()
            .Should().ContainSingle().Which
                .NormalizeWhitespace().ToFullString());
    }

    public static VB6ToCSharpConversion ValidateShouldNotFail(string vb, [CallerMemberName] string? name = null)
    {
        var cu = VB6ToCSharpConversion.ConvertString(vb, name);
        try {
            cu.ParseErrors.Should().BeEmpty();
            cu.TransformErrors.Should().BeEmpty();
            cu.SyntaxErrors.Should().BeEmpty();
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
        var wrapper = $"""
        Sub Test()
            {vb}
        End Sub
        """;

        var cu  = ValidateShouldNotFail(wrapper, name);
        var met = (MethodDeclarationSyntax)cu.Class.Members[0].NormalizeWhitespace();

        var sb = new StringBuilder();
        foreach (var m in met.Body!.Statements) {
            sb.AppendLine(m.NormalizeWhitespace().ToFullString());
        }

        ValidateStringsMatch(cs, sb.ToString());
    }

    static void ValidateStringsMatch(string expected, string actual)
    {
        actual = actual.ReplaceLineEndings(Environment.NewLine).TrimEnd(Environment.NewLine.ToCharArray());
        expected = expected.ReplaceLineEndings(Environment.NewLine).TrimEnd(Environment.NewLine.ToCharArray());
        actual.Should().Be(expected);
    }
}
