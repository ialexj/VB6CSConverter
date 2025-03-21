using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VB6Converter.CSharpModel
{
    class CSharpAssignmentStatement(ICSharpExpression target, ICSharpExpression value) : CSharpStatement
    {
        public ICSharpExpression Target { get; } = target;

        public ICSharpExpression Value { get; } = value;

        public override string ToString()
        {
            return $"{Target} = {Value};";
        }
    }
}
