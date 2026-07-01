using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using VB6Parser.Frx;

namespace VB6Converter;

/// <summary>
/// Exports an <see cref="FrxItem"/> to the <c>_Resources/</c> subdirectory of the
/// converter output folder, using the naming convention
/// <c>_Resources/{formName}_{offset:X4}{ext}</c>.
/// </summary>
public static class FrxResourceExporter
{
    /// <summary>
    /// Writes the item's data to the output directory and returns the relative
    /// resource path (e.g. <c>_Resources/Form1_0000.ico</c>).
    /// </summary>
    /// <param name="item">Parsed FRX item.</param>
    /// <param name="formName">Base name of the form/FRX file without extension (e.g. <c>Form1</c>).</param>
    /// <param name="outputDirectory">Absolute path to the converter output directory.</param>
    public static string Export(FrxItem item, string formName, string outputDirectory)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(formName);
        ArgumentNullException.ThrowIfNull(outputDirectory);

        var ext = GetExtension(item);
        var offsetHex = item.Offset.ToString("X4");
        var relativePath = Path.Combine("_Resources", $"{formName}_{offsetHex}{ext}");

        var resourcesDir = Path.Combine(outputDirectory, "_Resources");
        Directory.CreateDirectory(resourcesDir);

        var fullPath = Path.Combine(outputDirectory, relativePath);

        if (item is FrxStringList stringList) {
            File.WriteAllText(fullPath, JsonSerializer.Serialize(stringList.Strings));
        }
        else if (item is FrxBindings bindings) {
            File.WriteAllText(fullPath, JsonSerializer.Serialize(new {
                DataSource = bindings.DataSource,
                DataField = bindings.DataField,
            }));
        }
        else if (item is FrxOleObjectBlob oleBlob) {
            File.WriteAllText(fullPath, JsonSerializer.Serialize(new {
                version = oleBlob.Version,
                properties = oleBlob.Properties.Select(p => new { id = p.Id, type = p.Type, value = p.Value }),
            }));
        }
        else {
            File.WriteAllBytes(fullPath, item.GetPayloadData());
        }

        // Return with forward-slashes for use in comments
        return relativePath.Replace('\\', '/');
    }

    private static string GetExtension(FrxItem item)
    {
        if (item is FrxStringList)
            return ".json";

        if (item is FrxBindings)
            return ".bindings.json";

        if (item is FrxRtfText)
            return ".rtf";

        if (item is FrxOleObjectBlob)
            return ".msoleps.json";

        if (item is FrxBinaryBlob blob && blob.Payload is FrxImagePayload img)
            return DetectImageExtension(img.ImageBytes);

        return ".dat";
    }

    private static string DetectImageExtension(byte[] data)
    {
        if (data.Length < 2) return ".dat";

        // BMP
        if (data[0] == 0x42 && data[1] == 0x4D) return ".bmp";
        // JPEG
        if (data[0] == 0xFF && data[1] == 0xD8) return ".jpg";
        // GIF
        if (data.Length >= 3 && data[0] == 0x47 && data[1] == 0x49 && data[2] == 0x46) return ".gif";

        if (data.Length < 4) return ".dat";

        // ICO
        if (data[0] == 0x00 && data[1] == 0x00 && data[2] == 0x01 && data[3] == 0x00) return ".ico";
        // CUR
        if (data[0] == 0x00 && data[1] == 0x00 && data[2] == 0x02 && data[3] == 0x00) return ".cur";
        // EMF
        if (data[0] == 0x01 && data[1] == 0x00 && data[2] == 0x00 && data[3] == 0x00) return ".emf";
        // WMF
        if (data[0] == 0xD7 && data[1] == 0xCD && data[2] == 0xC6 && data[3] == 0x9A) return ".wmf";

        return ".dat";
    }
}
