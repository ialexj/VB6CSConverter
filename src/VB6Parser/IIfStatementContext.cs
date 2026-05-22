using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static VB6Parser.VisualBasic6Parser;

namespace VB6Parser;
public interface IIfStatementContext
{
    IIfBlockStmtContext ifBlock();

    IEnumerable<IIfBlockStmtContext> elseIfBlock();

    IIfBlockStmtContext elseBlock();
}

public interface IIfBlockStmtContext
{
    IBlockContext block();
}

partial class VisualBasic6Parser
{
    partial class BlockIfThenElseContext : IIfStatementContext
    {
        IIfBlockStmtContext IIfStatementContext.ifBlock() => ifBlockStmt();

        IEnumerable<IIfBlockStmtContext> IIfStatementContext.elseIfBlock() => ifElseIfBlockStmt();

        IIfBlockStmtContext IIfStatementContext.elseBlock() => ifElseBlockStmt();
    }

    public partial class IfBlockStmtContext : IIfBlockStmtContext
    {
        IBlockContext IIfBlockStmtContext.block() => block();
    }

    public partial class IfElseIfBlockStmtContext : IIfBlockStmtContext
    {
        IBlockContext IIfBlockStmtContext.block() => block();
    }

    public partial class IfElseBlockStmtContext : IIfBlockStmtContext
    {
        IBlockContext IIfBlockStmtContext.block() => block();
    }
}
