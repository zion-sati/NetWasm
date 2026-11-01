namespace NetWasm.Compiler.Tests.Correctness;

public sealed class PolyTypeGeneratedAccessorTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void ActualGeneratedExternsPreservePrivateAndGenericAccess()
    {
        using var stream = typeof(PolyTypeGeneratedAccessorTests).Assembly.GetManifestResourceStream(
            "Correctness/HostFixtures/PolyType/GeneratedAccessors.cs.txt");
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream);
        runner.Run(new("PolyTypeGeneratedAccessors", "PolyTypeGeneratedAccessors", reader.ReadToEnd(), [0])
        {
            ExpectedReturnValue = 255,
            Matrix = new(CorpusMatrixProfile.Extended, Backend: CorpusExecutionBackend.Linked),
        });
    }
}
