// Template generated code from Antlr4BuildTasks.Template v 8.17
namespace VB6Converter
{
    using Antlr4.Runtime;
    using Antlr4.Runtime.Tree;
    using System.Text;

    class ConsoleVisitor : VB6Parser.VisualBasic6BaseVisitor<string>
    {
        public StringBuilder sb = new();
        int indent = 0;

        public override string VisitChildren(IRuleNode node)
        {
            sb.AppendLine($"{new string(' ', indent)} '{(node as ParserRuleContext).Start.Text}' {node.GetType().Name} ({node.ChildCount} children)");

            indent++;
            try {
                return base.VisitChildren(node);
            }
            finally {
                indent--;
            }
        }

        public override string Visit(IParseTree tree)
        {
            base.Visit(tree);
            return sb.ToString();
        }
    }
}
