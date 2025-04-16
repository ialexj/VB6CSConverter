using Antlr4.Runtime.Tree;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VB6Parser
{
    public interface IVisibilityContext : IParseTree
    {
        ITerminalNode PRIVATE();
        ITerminalNode PUBLIC();
        ITerminalNode FRIEND();
        ITerminalNode GLOBAL();
    }

    public partial class VisualBasic6Parser
    {
        public partial class VisibilityContext : IVisibilityContext { }

        public partial class PublicPrivateVisibilityContext : IVisibilityContext
        {
            public ITerminalNode FRIEND() => null;
            public ITerminalNode GLOBAL() => null;
        }

        public partial class PublicPrivateGlobalVisibilityContext : IVisibilityContext
        {
            public ITerminalNode FRIEND() => null;
        }
    }
}
