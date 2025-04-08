using Antlr4.Runtime.Tree;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static VB6Parser.VisualBasic6Parser;

namespace VB6Parser;

public interface IAssignmentContext : IRuleNode, IParseTree, ISyntaxTree, ITree
{
    ImplicitCallStmt_InStmtContext implicitCallStmt_InStmt();

    ValueStmtContext valueStmt();
}

public partial class VisualBasic6Parser
{
    public partial class LetStmtContext : IAssignmentContext { }

    public partial class SetStmtContext : IAssignmentContext { }

    public partial class BlockStmtContext
    {
        public IAssignmentContext assignment() => (
            letStmt() as IAssignmentContext ??
            setStmt() as IAssignmentContext
        );
    }
}
