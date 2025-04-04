using System.Collections.Generic;
using System.Linq;

namespace VB6Converter.CSharpModel;


public class CSharpClass(CSharpVisibility vis, string name) : CSharpNamespaceMember(vis, name)
{
    public List<ICSharpClassMember> Members { get; set; } = [];

    public bool IsStatic { get; set; }

    public bool IsPartial { get; set; }


    protected override string GetDeclaration() => $"{(IsPartial ? "partial" : "")} {(IsStatic ? "static" : "")} class".TrimStart();

    protected override IEnumerable<object> GetBody() => Members;


    public CSharpClassProperty GetOrCreateProperty(string name)
    {
        var property = Members.OfType<CSharpClassProperty>().FirstOrDefault(p => p.Name == name);

        if (property is null) {
            property = new CSharpClassProperty() { Name = name };
            Members.Add(property);
        }

        return property;
    }
}

public interface ICSharpClassMember 
{
    CSharpVisibility Visibility { get; }
}

public class CSharpClassProperty : ICSharpClassMember
{
    public bool IsStatic { get; set; }

    public string Name { get; set; }

    public CSharpType Type { get; set; }

    public CSharpVisibility Visibility
    {
        get
        {
            var getter = Getter?.Item1 ?? CSharpVisibility.Private;
            var setter = Setter?.Item1 ?? CSharpVisibility.Private;

            if (getter == CSharpVisibility.Private && setter == CSharpVisibility.Private) {
                return CSharpVisibility.Private;
            }
            else {
                return CSharpVisibility.Public;
            }
        }
    }

    
    public (CSharpVisibility, CSharpBlockStatement)? Getter { get; set; }

    public (CSharpVisibility, CSharpBlockStatement, CSharpVariable)? Setter { get; set; }

    public override string ToString()
    {
        var sb = new StatementBuilder();
        sb.StartBlock($"{Visibility.ToCodeString()} {Type} {Name} {{");

        if (Getter is (CSharpVisibility visibility, CSharpBlockStatement getBlock)) {
            if (visibility != Visibility) {
                sb.Append(Visibility.ToCodeString());
            }
            sb.Append(" get ");
            sb.Append(getBlock.ToString());
        }

        if (Setter is (CSharpVisibility vis, CSharpBlockStatement setBlock, CSharpVariable variable)) {
            if (vis != Visibility) {
                sb.Append(Visibility.ToCodeString());
            }
            sb.AppendLine($"// value = {variable}");
            sb.Append(" set ");
            sb.Append(setBlock.ToString()); // todo: replace variable with value
        }

        sb.EndBlock("}");
        return sb.ToString();
    }
}
