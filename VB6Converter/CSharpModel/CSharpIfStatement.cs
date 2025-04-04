using System.Collections.Generic;

namespace VB6Converter.CSharpModel
{
    public class CSharpIfStatement : ICSharpStatement
    {
        public ICSharpExpression Condition { get; set; }

        public CSharpBlockStatement Then { get; set; } = [];

        public CSharpBlockStatement Else { get; set; } = [];

        public override string ToString()
        {
            var sb = new StatementBuilder();

            sb.Append($"if ({Condition}) {Then}");
            if (Else.Statements.Count > 0) {
                sb.Append($" else {Else}");
            }

            return sb.ToString();
        }
    }
}
