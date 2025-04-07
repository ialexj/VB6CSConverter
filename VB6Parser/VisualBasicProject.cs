namespace VB6Parser;

public class VisualBasicProject
{
    public string Name { get; set; }

    public List<VisualBasicProjectFile> Files { get; set; } = [];

    public static VisualBasicProject Load(string path)
    {
        const string FormMarker = "Form";
        const string ModuleMarker = "Module";
        const string ClassMarker = "Class";
        const string UserControlMarker = "UserControl";

        using var reader = new StreamReader(path, VisualBasic6Encoding.Encoding);

        var basePath = Path.GetDirectoryName(path);
        string GetFullPath(string path) => Path.Combine(basePath, path.Trim());

        var project = new VisualBasicProject {
            Name = Path.GetFileNameWithoutExtension(path)
        };

        string line;
        while ((line = reader.ReadLine()) != null) {
            var bits = line.Split('=', 2);
            if (bits.Length == 2) {
                VisualBasicFileType? type = bits[0] switch {
                    FormMarker => VisualBasicFileType.Form,
                    ModuleMarker => VisualBasicFileType.Module,
                    ClassMarker => VisualBasicFileType.Class,
                    UserControlMarker => VisualBasicFileType.Control,
                    _ => null
                };

                if (type != null) {
                    string name, filePath;

                    var bits2 = bits[1].Split(";", 2);
                    if (bits2.Length == 2) {
                        name = bits2[0];
                        filePath = GetFullPath(bits2[1]);
                    }
                    else {
                        name = Path.GetFileNameWithoutExtension(bits2[0]);
                        filePath = GetFullPath(bits2[0]);
                    }

                    project.Files.Add(new VisualBasicProjectFile(filePath, name, type.Value));
                }
            }
        }

        return project;
    }
}

