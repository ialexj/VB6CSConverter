using System;

namespace VB6Converter.CSharpModel
{
    public enum CSharpVisibility
    {
        Public,
        Internal,
        Protected,
        Private
    }

    public static class CSharpVisibilityExtensions
    {
        public static string ToCodeString(this CSharpVisibility visibility) => visibility switch {
            CSharpVisibility.Public => "public",
            CSharpVisibility.Private => "private",
            CSharpVisibility.Protected => "protected",
            CSharpVisibility.Internal => "internal",
            _ => throw new ArgumentException($"Not supported: {visibility}"),
        };
    }
}
