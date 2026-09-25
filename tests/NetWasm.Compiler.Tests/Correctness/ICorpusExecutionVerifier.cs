using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Correctness;

internal interface ICorpusExecutionVerifier
{
    void Verify(CorpusFixture fixture, WasmTarget requestedTarget, NetWasmExecution execution);
}
