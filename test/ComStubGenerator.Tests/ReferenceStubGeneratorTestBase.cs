using ComStubGenerator;

namespace ComStubGenerator.Tests;

public abstract class ReferenceStubGeneratorTestBase
{
    protected static readonly Guid TestGuid = new("12345678-0000-0000-0000-000000000001");

    protected static ComQueryLibrary MakeLibrary(string safeName, params ComQueryType[] types)
        => new(safeName, TestGuid, 1, 0, Types: types);

    // A library whose DiscoveredDependencies include mscorlib, triggering the
    // normalization + event-collapsing pipeline (DotnetLibraryGuids.RequiresNormalization).
    protected static readonly Guid MscorlibGuid = new("BED7F4EA-1A96-11d2-8F08-00A0C9A6186D");

    protected static ComQueryLibrary MakeDotnetLibrary(string safeName, params ComQueryType[] types)
        => new(safeName, TestGuid, 1, 0,
            Types: types,
            DiscoveredDependencies: [new ComQueryDiscoveredDep(MscorlibGuid, 2, 4)]);
}
