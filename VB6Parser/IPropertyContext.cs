using Antlr4.Runtime.Tree;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static VB6Parser.VisualBasic6Parser;

namespace VB6Parser
{
    public interface IPropertyContext : IParseTree
    {
        AmbiguousIdentifierContext ambiguousIdentifier();

        VisibilityContext visibility();
        
        BlockContext block();
        
        ITerminalNode STATIC();
    }

    public interface IPropertySetContext : IPropertyContext
    {
        ArgListContext argList();
    }

    public partial class VisualBasic6Parser
    {
        public partial class PropertyGetStmtContext : IPropertyContext { }

        public partial class PropertyLetStmtContext : IPropertySetContext { }

        public partial class PropertySetStmtContext : IPropertySetContext { }

        public partial class ModuleBodyElementContext
        {
            public IPropertyContext propertyAccessor() 
                => new IPropertyContext[] {
                    propertyGetStmt(),
                    propertyLetStmt(),
                    propertySetStmt()
                }.OfType<IPropertyContext>().FirstOrDefault();
        }
    }
}
