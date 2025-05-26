using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VB6Converter.Rewriters.Forms;

public static class FormsExtensions
{
    public static T WithUsingForms<T>(this T node) where T : SyntaxNode
    {
        return node.WithAdditionalAnnotations(
            new SyntaxAnnotation("Using", "System.Windows.Forms"),
            new SyntaxAnnotation("IgnoreMissing", string.Empty)
        );
    }
}
