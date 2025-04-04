namespace VB6Converter.CSharpModel
{
    public readonly record struct CSharpType(string Name, bool IsArray = false)
    {
        public static readonly CSharpType Unknown = new(null);

        public static readonly CSharpType Void = new("void");

        public static readonly CSharpType String = new("string");

        public static readonly CSharpType Object = new("object");

        public override string ToString() => Name ?? "object";
    }
}
