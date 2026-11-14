using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Tests.Correctness;

namespace NetWasm.Compiler.Tests;

public sealed class ArrayCreateInstanceLinkedTests(CorrectnessTestRunner runner)
{
    [Theory]
    [InlineData("Release-Wasm32-Direct-Linked")]
    [InlineData("Release-Wasm64-Direct-Linked")]
    public void RuntimeTypesCreateExactArraysWithNativeCollector(string cell)
    {
        var fixture = new CorpusFixture("ArrayCreationFixture", "ArrayCreationFixture",
            ArrayCreateInstanceContractCompilationTests.Source, [41])
        {
            CaseId = "array.runtime-creation",
            ExpectedReturnValue = 42,
            AllowUnsafe = true,
            RequiredRuntimeCapabilities = OracleRuntimeCapabilities.GarbageCollection,
            Matrix = new(CorpusMatrixProfile.Extended, cell, CorpusExecutionBackend.Linked),
            ExposesLegacyTrace = false,
            OracleMode = OracleMode.SameSource,
            SameSourceReason = "Compare runtime-typed array allocation semantics against the desktop runtime.",
            Optimization = FinalWasmOptimization.None,
        };
        runner.Run(fixture);
    }
}
