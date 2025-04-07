namespace VB6Parser;

public enum VisualBasicFileType
{
    Module,
    Class,
    Form,
    Control
}

public static class VisualBasicFileTypes
{
    public static VisualBasicFileType GetFromExtension(string filename) => Path.GetExtension(filename) switch {
        ".frm" => VisualBasicFileType.Form,
        ".cls" => VisualBasicFileType.Class,
        ".bas" => VisualBasicFileType.Module,
        ".ctl" => VisualBasicFileType.Control,
        _ => VisualBasicFileType.Module
    };
}

