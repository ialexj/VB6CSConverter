using System.Text.Json;
using System.Text.Json.Serialization;
using ComQuery;

namespace ComQuery.Tests;

public abstract class TypeLibraryInspectorIntegrationTestBase
{
    protected static ComReference MakeReference(
        Guid guid, int major, int minor, string description, string path) =>
        new(guid, major, minor, 0, description);

    protected static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
        PropertyNameCaseInsensitive = true,
    };

    protected static ComStubGenerator.ComQueryLibrary ToStubModel(ComQueryLibrary library)
    {
        var json = JsonSerializer.Serialize(library, JsonOptions);
        return JsonSerializer.Deserialize<ComStubGenerator.ComQueryLibrary>(json, JsonOptions)!;
    }
}
