using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class ValueFrameInitializationTests(CorrectnessTestRunner runner) : EmittedAssemblyTestBase(runner)
{
    public static TheoryData<string, string> MatrixCells =>
        CorpusCaseTestData.Cells(CorpusCaseTestData.ValueFrameInitialization);

    [Theory]
    [MemberData(nameof(MatrixCells))]
    public void ReusedValueFramesPreserveInitializedLocalSemantics(string caseId, string cell) =>
        Run(CorpusCaseTestData.Get(caseId), cell, new ValueFrameInitializationFixtureBuilder());

    [Fact]
    public void EmittedObserverDeclaresInitializedLocals()
    {
        var builder = Assert.IsAssignableFrom<IEmittedAssemblyBuilder>(new ValueFrameInitializationFixtureBuilder());
        using var stream = new MemoryStream(builder.Build().ToArray());
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        var entryPoint = metadata.TypeDefinitions
            .Select(metadata.GetTypeDefinition)
            .Single(type => metadata.GetString(type.Name) == "EntryPoint");
        var observe = entryPoint.GetMethods()
            .Select(metadata.GetMethodDefinition)
            .Single(method => metadata.GetString(method.Name) == "Observe");
        var body = pe.GetMethodBody(observe.RelativeVirtualAddress);

        Assert.True(body.LocalVariablesInitialized);
        Assert.False(body.LocalSignature.IsNil);
    }
}
