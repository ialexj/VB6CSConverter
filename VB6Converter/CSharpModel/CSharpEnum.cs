using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace VB6Converter.CSharpModel;

public class CSharpEnum(CSharpVisibility visibility, string name) : CSharpNamespaceMember(visibility, name), ICSharpClassMember
{
    public List<(string name, ICSharpExpression value)> Values { get; set; } = [];

    protected override string GetDeclaration() => "enum";

    protected override IEnumerable<object> GetBody() => Values.Select(v => {
        var b = new StringBuilder();
        b.Append(v.name);
        if (v.value != null) {
            b.Append(" = ");
            b.Append(v.value.ToString());
        }
        return b.ToString();
    });
}