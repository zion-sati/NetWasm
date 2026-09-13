using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Methods;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class MethodExceptionBoundaryEmitterTests
{
    [Fact]
    public void CatchesEscapingPayloadOnlyToReleaseFramesAndRethrow()
    {
        var layouts = new RecordingLayoutProvider();
        var imports = WasmRuntimeImports.CreateCatalog();
        var frameExit = CreateMethodFrameExit(imports);
        var outputBuffer = new WasmBinaryBuffer();
        var output = new WasmBinaryWriter(outputBuffer);
        var code = new WasmInstructionWriter(output);
        var context = CreateContext();
        MethodEmissionContext? bodyContext = null;

        IMethodExceptionBoundaryEmitter boundary =
            new[]
            {
                new MethodExceptionBoundaryEmitter(
                    new ExceptionPayloadBlockEmitter(layouts),
                    frameExit),
            }
            .Cast<IMethodExceptionBoundaryEmitter>()
            .Single();
        boundary.Emit(
            code,
            context,
            emittedContext =>
            {
                bodyContext = emittedContext;
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.I32Constant,
                    WasmInstructionOperand.Signed(7)));
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.Drop));
            });

        Assert.NotNull(bodyContext);
        Assert.False(bodyContext.LeaveFrameOnExceptionalExit);
        Assert.True(context.LeaveFrameOnExceptionalExit);
        var body = new WasmBinarySnapshotReader(outputBuffer).Read();
        Assert.Contains(WasmOpcodes.TryTable, body);
        Assert.Contains(WasmOpcodes.Throw, body);
    }

    private static MethodEmissionContext CreateContext()
    {
        var roots = new MethodRootMap(
            EntryKey,
            [],
            []);
        var values = new ValueFrameLayout(
            0,
            [],
            [],
            [],
            []);
        return new MethodEmissionContext(
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
            values,
            4,
            FilterEnvironmentLayout.Empty,
            0,
            5,
            6,
            7,
            8,
            9,
            10);
    }
}
