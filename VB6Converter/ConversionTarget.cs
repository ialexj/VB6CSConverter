using static VB6Parser.VisualBasic6Parser;

namespace VB6Converter;

public record struct ConversionTarget(ModuleContext Module, string Name, bool Static) { }
