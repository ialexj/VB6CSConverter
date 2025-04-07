using Antlr4.Runtime.Misc;
using Antlr4.Runtime.Tree;
using System.Text;

namespace VB6Parser
{
    public static class TreeUtils
    {
        public static string ToStringTree(ITree t, IList<string> ruleNames)
        {
            var sb = new StringBuilder();

            void WriteTree(ITree t, IList<string> ruleNames, int indent)
            {
                string indentStr = new(' ', indent * 2);
                string name = Utils.EscapeWhitespace(Trees.GetNodeText(t, ruleNames), escapeSpaces: false);

                if (t.ChildCount == 0) {
                    sb.Append($" \"{name}\"");
                    return;
                }
                else {
                    sb.AppendLine();
                    sb.Append(indentStr);
                    sb.Append('(');
                    sb.Append(name);

                    for (int i = 0; i < t.ChildCount; i++) {
                        var child = t.GetChild(i);
                        WriteTree(child, ruleNames, indent + 1);
                    }

                    if (indent > 0 && EnumerateChildren(t).Any(t => t.ChildCount > 0)) {
                        sb.AppendLine();
                        sb.Append(indentStr);
                    }

                    sb.Append(')');
                }
            }

            WriteTree(t, ruleNames, 0);
            return sb.ToString();

            static IEnumerable<ITree> EnumerateChildren(ITree tree)
            {
                for (int i = 0; i < tree.ChildCount; i++) {
                    yield return tree.GetChild(i);
                }
            }
        }
    }
}
