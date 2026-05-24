#nullable enable
namespace VB6Parser;

public static class VisualBasicImplicitReferences
{
    public static readonly VisualBasicProjectReference VBObjects = new(
        ProjectReferenceKind.TypeLibrary,
        new("FCFB3D2E-A0FA-1068-A738-08002B3371B5"), 6, 0, 0,
        "Visual Basic objects and procedures",
        "VB6.OLB");

    public static readonly VisualBasicProjectReference VB6Runtime = new(
        ProjectReferenceKind.TypeLibrary,
        new("000204EF-0000-0000-C000-000000000046"), 6, 0, 9,
        "Visual Basic For Applications",
        "MSVBVM60.DLL");

    public static readonly VisualBasicProjectReference StdOle = new(
        ProjectReferenceKind.TypeLibrary,
        new("00020430-0000-0000-C000-000000000046"), 2, 0, 0,
        "OLE Automation",
        "stdole2.tlb");

    public static readonly VisualBasicProjectReference[] All = [
        VBObjects,
        VB6Runtime,
        StdOle,
    ];
}

