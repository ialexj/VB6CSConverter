using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VB6Converter.CSharpModel
{
    public class CSharpNewExpression : ICSharpExpression
    {
        public ICSharpExpression Type { get; set; }

        public override string ToString() => $"new {Type}";
    }
}
