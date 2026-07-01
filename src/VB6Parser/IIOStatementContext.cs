using Antlr4.Runtime.Tree;

namespace VB6Parser;

public interface IIOStatementContext : IParseTree { }

public partial class VisualBasic6Parser
{
    public partial class BlockStmtContext
    {
        public IIOStatementContext ioStmt() => (
            openStmt() as IIOStatementContext ??
            closeStmt() as IIOStatementContext ??
            printStmt() as IIOStatementContext ??
            lineInputStmt() as IIOStatementContext ??
            writeStmt() as IIOStatementContext ??
            filecopyStmt() as IIOStatementContext ??
            getStmt() as IIOStatementContext ??
            putStmt() as IIOStatementContext ??
            seekStmt() as IIOStatementContext ??
            killStmt() as IIOStatementContext ??
            nameStmt() as IIOStatementContext ??
            resetStmt() as IIOStatementContext ??
            mkdirStmt() as IIOStatementContext ??
            rmdirStmt() as IIOStatementContext ??
            chDirStmt() as IIOStatementContext ??
            chDriveStmt() as IIOStatementContext ??
            setattrStmt() as IIOStatementContext ??
            lockStmt() as IIOStatementContext ??
            unlockStmt() as IIOStatementContext ??
            inputStmt() as IIOStatementContext ??
            eraseStmt() as IIOStatementContext ??
            widthStmt() as IIOStatementContext
        );
    }

    public partial class OpenStmtContext : IIOStatementContext { }

    public partial class CloseStmtContext : IIOStatementContext { }

    public partial class PrintStmtContext : IIOStatementContext { }

    public partial class LineInputStmtContext : IIOStatementContext { }

    public partial class WriteStmtContext : IIOStatementContext { }

    public partial class FilecopyStmtContext : IIOStatementContext { }

    public partial class GetStmtContext : IIOStatementContext { }

    public partial class PutStmtContext : IIOStatementContext { }

    public partial class SeekStmtContext : IIOStatementContext { }

    public partial class KillStmtContext : IIOStatementContext { }

    public partial class FilecopyStmtContext : IIOStatementContext { }

    public partial class NameStmtContext : IIOStatementContext { }

    public partial class ResetStmtContext : IIOStatementContext { }

    public partial class MkdirStmtContext : IIOStatementContext { }

    public partial class RmdirStmtContext : IIOStatementContext { }

    public partial class ChDirStmtContext : IIOStatementContext { }

    public partial class ChDriveStmtContext : IIOStatementContext { }

    public partial class SetattrStmtContext : IIOStatementContext { }

    public partial class LockStmtContext : IIOStatementContext { }

    public partial class UnlockStmtContext : IIOStatementContext { }

    public partial class InputStmtContext : IIOStatementContext { }

    public partial class EraseStmtContext : IIOStatementContext { }

    public partial class WidthStmtContext : IIOStatementContext { }
}
