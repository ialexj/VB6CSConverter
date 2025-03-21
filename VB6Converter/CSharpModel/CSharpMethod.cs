using System.Collections.Generic;

namespace VB6Converter.CSharpModel
{
    public class CSharpMethod(string name) : CSharpClassMember
    {
        public string Name { get; } = name;

        public CSharpVisibility Visibility { get; set; }

        public CSharpType ReturnType { get; set; }

        public List<CSharpStatement> Statements { get; set; } = [];

        public List<CSharpArgument> Arguments { get; internal set; }

        public override string ToString()
        {
            var b = new StatementBuilder();
            b.StartBlock($"{Visibility.ToCodeString()} {ReturnType} {Name} ({Arguments.ToArgumentsString()}) {{");

            foreach (var statement in Statements) {
                b.WriteLine(statement.ToString());
            }

            b.EndBlock("}");
            return b.ToString();
        }
    }
}
