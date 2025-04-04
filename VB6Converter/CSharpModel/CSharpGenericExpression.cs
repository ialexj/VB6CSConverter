namespace VB6Converter.CSharpModel
{
    public interface ICSharpExpression { }

    public class CSharpGenericExpression(string text) : ICSharpExpression
    {
        public string Value { get; } = text;

        public override string ToString()
        {
            return Value;
        }
    }
}
