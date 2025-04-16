using Antlr4.Runtime.Tree;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VB6Parser;
public  interface IBlockContext : IParseTree
{
    VisualBasic6Parser.BlockStmtContext[] blockStmt();
}

partial class VisualBasic6Parser
{
    partial class BlockContext : IBlockContext { }

    partial class IfInlineBlockStmtContext : IBlockContext { }
}
