using System.Collections.Generic;

namespace VB6Converter.CSharpModel;

public class CSharpStruct(CSharpVisibility vis, string name, CSharpVariable[] variables) : CSharpNamespaceMember(vis, name), ICSharpClassMember
{
    public CSharpVariable[] Variables { get; set; } = variables;

    protected override string GetDeclaration() => "struct";

    protected override IEnumerable<object> GetBody() => Variables;
}
