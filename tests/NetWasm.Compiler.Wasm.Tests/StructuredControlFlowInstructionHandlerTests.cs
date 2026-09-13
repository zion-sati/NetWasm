using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Methods;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class StructuredControlFlowInstructionHandlerTests
{
    [Fact]
    public void EveryStructuredBranchRejectsAccidentalScalarLowering()
    {
        var handler = new StructuredControlFlowInstructionHandler();
        foreach (var operation in handler.Operations)
        {
            var code = new RecordingInstructionWriter();
            var exception = Assert.Throws<InvalidOperationException>(() =>
                handler.Emit(CreateRequest(operation), code));
            Assert.Equal(
                "Control-flow instruction reached scalar lowering.",
                exception.Message);
        }
    }

    private static InstructionEmissionRequest CreateRequest(CilOperation operation)
    {
        var roots = new MethodRootMap(
            EntryKey,
            [],
            []);
        var context = new MethodEmissionContext(
            roots,
            0,
            0,
            WasmLocalLayoutPlanner.CreateEvaluationStack(0, 0),
            0,
            1,
            2,
            ImmutableDictionary<StructuredExceptionGroupId, int>.Empty,
            ImmutableDictionary<StructuredExceptionGroupId, int>.Empty,
            3,
            new ValueFrameLayout(
                0,
                [],
                [],
                [],
                []),
            4,
            FilterEnvironmentLayout.Empty,
            0,
            5,
            6,
            7,
            8,
            9,
            10);
        var method = new FakeProgram().GetMethod(EntryKey);
        return new InstructionEmissionRequest(
            Header(new CilMethodBody(method, 0, [], [])),
            I(0, operation),
            [],
            context,
            CreateInstructionModuleTarget());
    }
}
