using static VB6Parser.VisualBasic6Parser;

namespace VB6Parser;
public interface IControlPropertiesContext
{
    AmbiguousIdentifierContext ambiguousIdentifier();

    Cp_PropertiesContext[] cp_Properties();
}

partial class VisualBasic6Parser
{
    partial class ControlPropertiesContext : IControlPropertiesContext
    {
        public AmbiguousIdentifierContext ambiguousIdentifier() => cp_ControlIdentifier().ambiguousIdentifier();
    }

    partial class Cp_NestedPropertyContext : IControlPropertiesContext 
    {
    }
}
