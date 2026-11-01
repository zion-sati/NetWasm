using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Tests.Correctness;

namespace NetWasm.Compiler.Tests;

public sealed class NullableArrayReadbackLinkedTests(CorrectnessTestRunner runner)
{
    [Theory]
    [InlineData("Release-Wasm32-Direct-Linked")]
    [InlineData("Release-Wasm64-Direct-Linked")]
    public void NullableReadbackUsesNativeBoxingAndCollector(string cell)
    {
        var fixture = new CorpusFixture("NullableReadbackFixture", "NullableReadbackFixture",
            NullableArrayReadbackCompilationTests.Source, [41])
        {
            CaseId = "array.nullable-readback",
            ExpectedReturnValue = 42,
            RequiredRuntimeCapabilities = OracleRuntimeCapabilities.GarbageCollection,
            Matrix = new(CorpusMatrixProfile.Extended, cell, CorpusExecutionBackend.Linked),
            ExposesLegacyTrace = false,
            OracleMode = OracleMode.SameSource,
            SameSourceReason = "Compare public nullable array boxing semantics against the desktop runtime.",
            Optimization = FinalWasmOptimization.None,
        };
        runner.Run(fixture);
    }
}
