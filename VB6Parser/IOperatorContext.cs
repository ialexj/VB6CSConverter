using Antlr4.Runtime.Tree;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static VB6Parser.VisualBasic6Parser;

namespace VB6Parser
{
    public interface IOperatorContext : IParseTree
    {
        ValueStmtContext[] valueStmt();
    }

    partial class VisualBasic6Parser
    {
        partial class VsAmpContext : IOperatorContext { }

        partial class VsAddContext : IOperatorContext { }
        partial class VsMinusContext : IOperatorContext { }
        partial class VsMultContext : IOperatorContext { }
        partial class VsDivContext : IOperatorContext { }
        partial class VsPowContext : IOperatorContext { }
        partial class VsModContext : IOperatorContext { }

        partial class VsNegationContext : IOperatorContext {
            ValueStmtContext[] IOperatorContext.valueStmt() => [valueStmt()];
        }


        partial class VsEqContext : IOperatorContext { }
        partial class VsNeqContext : IOperatorContext { }


        partial class VsLtContext : IOperatorContext { }
        partial class VsLeqContext : IOperatorContext { }


        partial class VsGtContext : IOperatorContext { }
        partial class VsGeqContext : IOperatorContext { }


        partial class VsAndContext : IOperatorContext { }
        partial class VsOrContext : IOperatorContext { }
        partial class VsXorContext : IOperatorContext { }
        partial class VsNotContext : IOperatorContext
        {
            ValueStmtContext[] IOperatorContext.valueStmt() => [ valueStmt() ];
        }


        partial class VsIsContext : IOperatorContext { }

    }
}
