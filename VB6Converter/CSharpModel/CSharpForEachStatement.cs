using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VB6Converter.CSharpModel
{
    public class CSharpForEachStatement : ICSharpStatement
    {
        public CSharpIdentifierExpression Variable { get; internal set; }

        public ICSharpExpression Enumerator { get; internal set; }

        public CSharpBlockStatement Statements { get; internal set; }

        public override string ToString()
        {
            return $"foreach (var {Variable} in {Enumerator}) {Statements}";
        }
    }
}
