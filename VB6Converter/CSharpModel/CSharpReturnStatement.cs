namespace VB6Converter.CSharpModel
{
    public class CSharpReturnStatement : ICSharpStatement
    {
        public ICSharpExpression Value { get; set; }

        public override string ToString()
        {
            if (Value != null)
            {
                return $"return {Value};";
            }
            else
            {
                return "return;";
            }
        }
    }
}
