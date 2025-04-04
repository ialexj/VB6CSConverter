using Antlr4.Runtime.Tree;
using static VB6Parser.VisualBasic6Parser;

namespace VB6Parser;

public interface IIdentifierContext 
{
    ITerminalNode[] IDENTIFIER();

    ITerminalNode IDENTIFIER(int i);

    AmbiguousKeywordContext[] ambiguousKeyword();

    AmbiguousKeywordContext ambiguousKeyword(int i);

    string GetText();
}

public partial class VisualBasic6Parser
{
    public partial class CertainIdentifierContext : IIdentifierContext { }

    public partial class AmbiguousIdentifierContext : IIdentifierContext { }
}
