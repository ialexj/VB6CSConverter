using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace VB6Converter.Tests
{
    public static class Validations
    {
        public static void ValidateClassMatches(string vb, string cs, [CallerMemberName] string? name = null)
        {
            var cu = VB6ToCSharpConverter.GetCompilationUnit(vb, name);
            cu.Members.OfType<FileScopedNamespaceDeclarationSyntax>()
                .Should().ContainSingle().Which
                .Members.OfType<ClassDeclarationSyntax>()
                    .Should().ContainSingle()
                    .Which.ToFullString().Should().Be(cs);
        }

        public static void ValidateMemberMatches(string vb, string cs, [CallerMemberName] string? name = null)
        {
            var cu = VB6ToCSharpConverter.GetCompilationUnit(vb, name);
            cu.Members.OfType<FileScopedNamespaceDeclarationSyntax>()
                .Should().ContainSingle().Which
                .Members.OfType<ClassDeclarationSyntax>()
                    .Should().ContainSingle().Which
                        .Members.OfType<MemberDeclarationSyntax>()
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

            var cu = VB6ToCSharpConverter.GetCompilationUnit(wrapper, name);
            var ns = (FileScopedNamespaceDeclarationSyntax)cu.Members[0];
            var cls = (ClassDeclarationSyntax)ns.Members[0];
            var met = (MethodDeclarationSyntax)cls.Members[0];

            var sb = new StringBuilder();
            foreach (var m in met.Body!.Statements) {
                sb.AppendLine(m.NormalizeWhitespace().ToFullString());
            }

            string converted = sb.ToString().TrimEnd(Environment.NewLine.ToCharArray());
            converted.Should().Be(cs);
        }
    }
}
