using System.Collections.Generic;

namespace VB6Converter.CSharpModel
{
    public class CSharpClass(string name)
    {
        public string Name { get; } = name;

        public CSharpVisibility Visibility { get; set; } = CSharpVisibility.Internal;

        public List<CSharpClassMember> Members { get; set; } = [];

        public List<CSharpDeclarationStatement> Declarations { get; set; } = [];

        public override string ToString()
        {
            var b = new StatementBuilder();
            b.StartBlock($"{Visibility.ToCodeString()} class {Name} {{");

            foreach (var member in Members) {
                b.WriteLine(member.ToString());
            }

            b.EndBlock("}");
            return b.ToString();
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
                b.WriteLine(statement.ToString());
            }
            b.EndBlock("}");
            return b.ToString();
        }
    }
}
