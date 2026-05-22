using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static VB6Parser.VisualBasic6Parser;

namespace VB6Converter.Conversion;

public readonly record struct CallContext(ImplicitCallStmt_InStmtContext With) { }

public readonly record struct ClassContext(string Name, bool Static) { }
