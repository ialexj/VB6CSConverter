namespace VB6Converter.CSharpModel
{
    public class CSharpArgumentCall(ICSharpExpression value, ICSharpExpression name = null)
    {
        public ICSharpExpression Value { get; set; } = value;

        public ICSharpExpression Name { get; set; } = name;

        public override string ToString()
        {
            if (Name != null) {
                return $"{Name}: {Value}";
            }
            else {
                return Value.ToString();
            }
        }
    }
}
