using AwesomeAssertions;
using ComStubGenerator;

namespace ComStubGenerator.Tests;

[TestClass]
public class ReferenceNamingTests : ReferenceStubGeneratorTestBase
{
    [TestMethod]
    public void MakeSafeName_WithSpaces_ProducesCamelCase()
    {
        ReferenceNaming.MakeSafeName("Microsoft Scripting Runtime")
            .Should().Be("MicrosoftScriptingRuntime");
    }

    [TestMethod]
    public void MakeSafeName_Empty_ReturnsUnknownLib()
    {
        ReferenceNaming.MakeSafeName("").Should().Be("UnknownLib");
    }

    [TestMethod]
    public void MakeSafeName_WithDots_ProducesCamelCase()
    {
        ReferenceNaming.MakeSafeName("stdole2.tlb")
            .Should().Be("stdole2Tlb");
    }

    [TestMethod]
    public void MakeSafeIdentifier_CSharpKeyword_GetsAtPrefix()
    {
        ReferenceStubGenerator.MakeSafeIdentifier("object").Should().Be("@object");
        ReferenceStubGenerator.MakeSafeIdentifier("string").Should().Be("@string");
        ReferenceStubGenerator.MakeSafeIdentifier("ref").Should().Be("@ref");
    }

    [TestMethod]
    public void MakeSafeIdentifier_NormalName_Unchanged()
    {
        ReferenceStubGenerator.MakeSafeIdentifier("Count").Should().Be("Count");
    }
}
