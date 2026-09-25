using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace NetWasm.Compiler.Tests.Correctness;

[Collection(CorrectnessTestGroup.Name)]
public sealed class EmittedDelegateReceiverTests(CorrectnessTestRunner runner) : EmittedAssemblyTestBase(runner)
{
    public static TheoryData<string, string> ClosedCells => CorpusCaseTestData.Cells(CorpusCaseTestData.ClosedDelegateReceiver);
    public static TheoryData<string, string> OpenCells => CorpusCaseTestData.Cells(CorpusCaseTestData.OpenDelegateReceiver);
    public static TheoryData<string, string> NullCells => CorpusCaseTestData.Cells(CorpusCaseTestData.NullDelegateReceiver);

    [Theory]
    [MemberData(nameof(ClosedCells))]
    public void ClosedInstanceDelegateKeepsItsCapturedReceiver(string caseId, string cell) =>
        Run(CorpusCaseTestData.Get(caseId), cell, new DelegateReceiverFixtureBuilder());

    [Theory(Skip = KnownNonBugSkipReasons.EmittedOpenDelegate)]
    [MemberData(nameof(OpenCells))]
    public void OpenInstanceDelegateTakesItsReceiverFromTheInvokeArgument(string caseId, string cell) =>
        Run(CorpusCaseTestData.Get(caseId), cell, new DelegateReceiverFixtureBuilder());

    [Theory(Skip = KnownNonBugSkipReasons.EmittedOpenDelegate)]
    [MemberData(nameof(NullCells))]
    public void OpenInstanceDelegateWithNullReceiverThrowsManagedNullReference(string caseId, string cell) =>
        Run(CorpusCaseTestData.Get(caseId), cell, new DelegateReceiverFixtureBuilder());

    [Fact]
    public void EmittedDelegateSignaturesKeepOpenReceiverSeparateFromCapturedTarget()
    {
        var builder = Assert.IsAssignableFrom<IEmittedAssemblyBuilder>(new DelegateReceiverFixtureBuilder());
        using var stream = new MemoryStream(builder.Build().ToArray());
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        var types = metadata.TypeDefinitions.Select(metadata.GetTypeDefinition)
            .ToDictionary(type => metadata.GetString(type.Name));
        foreach (var (type, parameters) in new[] { ("OpenReader", 1), ("ClosedReader", 0) })
        {
            var method = Assert.Single(types[type].GetMethods().Select(metadata.GetMethodDefinition),
                method => metadata.GetString(method.Name) == "Invoke");
            var signature = metadata.GetBlobReader(method.Signature);
            Assert.True(signature.ReadSignatureHeader().IsInstance);
            Assert.Equal(parameters, signature.ReadCompressedInteger());
            Assert.Equal(0, method.RelativeVirtualAddress);
        }
        var entries = types["EntryPoint"].GetMethods().Select(metadata.GetMethodDefinition)
            .Select(method => metadata.GetString(method.Name)).Order().ToArray();
        Assert.Equal(["RunClosed", "RunOpen", "RunOpenNull"], entries);
    }
}
