using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static VB6Parser.VisualBasic6Parser;

namespace VB6Parser
{
    public interface IMethodContext
    {
        bool IsFunction { get; }

        VisibilityContext visibility();

        AmbiguousIdentifierContext ambiguousIdentifier();

        AsTypeClauseContext asTypeClause();

        ArgListContext argList();

        BlockContext block();
    }

    public partial class VisualBasic6Parser
    {
        public partial class SubStmtContext : IMethodContext
        {
            public bool IsFunction => false;

            AsTypeClauseContext IMethodContext.asTypeClause() => null;
        }

        public partial class FunctionStmtContext : IMethodContext
        {
            public bool IsFunction => true;
        }
    }
}
