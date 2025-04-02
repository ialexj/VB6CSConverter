using System.Collections.Generic;
using System.Linq;

namespace VB6Converter.CSharpModel
{
    public class CSharpClass(string name)
    {
        public string Name { get; } = name;

        public CSharpVisibility Visibility { get; set; } = CSharpVisibility.Internal;

        public List<CSharpClassMember> Members { get; set; } = [];

        public List<CSharpDeclarationStatement> Declarations { get; set; } = [];

        public List<CSharpEnum> Enums { get; internal set; } = [];

        public List<CSharpStruct> Structs { get; internal set; }

        public override string ToString()
        {
            var b = new StatementBuilder();
            b.StartBlock($"{Visibility.ToCodeString()} class {Name} {{");

            foreach (var member in Members) {
                b.AppendLine(member.ToString());
            }

            b.EndBlock("}");
            return b.ToString();
        }

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

    public class CSharpClassMember { }

    public class CSharpClassStatement(CSharpStatement Statement) : CSharpClassMember
    {
        public CSharpStatement Statement { get; } = Statement;

        public override string ToString() => Statement.ToString();
    }

    public class CSharpClassStatements : CSharpClassMember
    {
        public CSharpClassStatements() { }

        public CSharpClassStatements(IEnumerable<CSharpStatement> statements)
        {
            Statements.AddRange(statements);
        }

        public List<CSharpStatement> Statements { get; set; } = [];

        public override string ToString()
        {
            var b = new StatementBuilder();
            b.StartBlock("{");
            foreach (var statement in Statements) {
                b.AppendLine(statement.ToString());
            }
            b.EndBlock("}");
            return b.ToString();
        }
    }

    public class CSharpClassProperty : CSharpClassMember
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
}
