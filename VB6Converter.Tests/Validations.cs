using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Runtime.CompilerServices;
using System.Text;

namespace VB6Converter.Tests;

public static class Validations
{
    public static void ValidateClassMatches(string vb, string cs, [CallerMemberName] string? name = null)
    {
        var cu = VB6ToCSharpConverter.GetConversion(vb, name);
        cu.Class.NormalizeWhitespace().ToFullString().Should().Be(cs);
    }

    public static void ValidateMemberMatches(string vb, string cs, [CallerMemberName] string? name = null)
    {
        var cu = VB6ToCSharpConverter.GetConversion(vb, name);
        cu.Class.Members.OfType<MemberDeclarationSyntax>()
            .Should().ContainSingle().Which
                .NormalizeWhitespace().ToFullString().Should().Be(cs);
    }

    public static void ValidateBodyMatches(string vb, string cs, [CallerMemberName] string? name = null)
    {
        var wrapper = $"""
        Sub Test()
            {vb}
        End Sub
        """;

        var cu  = VB6ToCSharpConverter.GetConversion(wrapper, name);
        var met = (MethodDeclarationSyntax)cu.Class.Members[0].NormalizeWhitespace();

        var sb = new StringBuilder();
        foreach (var m in met.Body!.Statements) {
            sb.AppendLine(m.NormalizeWhitespace().ToFullString());
        }

        string converted = sb.ToString().TrimEnd(Environment.NewLine.ToCharArray());
        converted.Should().Be(cs);
    }
}
