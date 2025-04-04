using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VB6Converter.CSharpModel
{
    class CSharpWhileStatement : ICSharpStatement
    {
        public ICSharpExpression Condition { get; set; } = new CSharpGenericExpression("true");

        public CSharpBlockStatement Statements { get; set; } = [];

        public override string ToString() => $"while ({Condition}) {Statements}";
    }
}
