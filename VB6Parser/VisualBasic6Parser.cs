using Antlr4.Runtime;

namespace VB6Parser;

public partial class VisualBasic6Parser
{
    public static VisualBasic6Parser Parse(CommonTokenStream tokens, string name, string treeFilePath = null)
    {
        var parser = new VisualBasic6Parser(tokens);

        if (treeFilePath != null) {
            File.WriteAllText(treeFilePath, TreeUtils.ToStringTree(parser.startRule(), parser.RuleNames));
            tokens.Reset();
        }

        if (parser.NumberOfSyntaxErrors > 0) {
            throw new Exception($"error parsing {name}.");
        }

        return parser;
    }
}
