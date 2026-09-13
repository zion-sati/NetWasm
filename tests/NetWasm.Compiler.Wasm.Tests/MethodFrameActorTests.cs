using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Methods;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class MethodFrameActorTests
{
    [Fact]
    public void EntersAndLeavesEveryRequiredFrameInOwnershipOrder()
    {
        var program = new FakeProgram();
        var imports = WasmRuntimeImports.CreateCatalog();
        var context = CreateContext();
        var outputBuffer = new WasmBinaryBuffer();
        var output = new WasmBinaryWriter(outputBuffer);
        var code = new WasmInstructionWriter(output);
        var layouts = new RecordingLayoutProvider();
        var entry = CreateMethodFrameEntry(program, layouts, imports);
        var exit = CreateMethodFrameExit(imports);

        entry.Emit(code, CreateBody(program), context);
        exit.Emit(code, context);

        Assert.Equal(
            [
                imports.Resolve(RuntimeImportSymbol.RootFrameEnter),
                imports.Resolve(RuntimeImportSymbol.ValueFrameEnter),
                imports.Resolve(RuntimeImportSymbol.RootFrameEnter),
                imports.Resolve(RuntimeImportSymbol.RootFrameLeave),
                imports.Resolve(RuntimeImportSymbol.ValueFrameLeave),
                imports.Resolve(RuntimeImportSymbol.RootFrameLeave),
            ],
            Calls(new WasmBinarySnapshotReader(outputBuffer).Read()));
    }

    [Fact]
    public void Memory64UsesI64ForFilterEnvironmentAddress()
    {
        var program = new FakeProgram();
        var outputBuffer = new WasmBinaryBuffer();
        var output = new WasmBinaryWriter(outputBuffer);
        var code = new WasmInstructionWriter(output);
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.Wasm64);

        CreateMethodFrameEntry(
            program,
            layouts,
            WasmRuntimeImports.CreateCatalog()).Emit(
                code,
                CreateBody(program),
                CreateContext());

        Assert.Contains(
            WasmOpcodes.I64Constant,
            new WasmBinarySnapshotReader(outputBuffer).Read());
    }

    private static StructuredMethodHeader CreateBody(FakeProgram program) => new(
        program.GetMethod(EntryKey),
        null,
        1,
        [],
        [],
        []);

    private static MethodEmissionContext CreateContext()
    {
        var roots = new MethodRootMap(
            EntryKey,
            ImmutableDictionary<RootSource, int>.Empty.Add(
                new RootSource(RootSourceKind.Argument, 0),
                0),
            []);
        var values = new ValueFrameLayout(
            8,
            [],
            [],
            [],
            []);
        var filters = new FilterEnvironmentLayout(
            8,
            1,
            4,
            []);
        return new MethodEmissionContext(
            roots,
            1,
            1,
            WasmLocalLayoutPlanner.CreateEvaluationStack(1, 1),
            7,
            8,
            9,
            ImmutableDictionary<StructuredExceptionGroupId, int>.Empty,
            ImmutableDictionary<StructuredExceptionGroupId, int>.Empty,
            10,
            values,
            11,
            filters,
            0,
            12,
            13,
            14,
            15,
            16,
            17);
    }

    private static int[] Calls(byte[] code) => [.. code
        .Select((value, index) => (value, index))
        .Where(item => item.value == WasmOpcodes.Call && item.index + 1 < code.Length)
        .Select(item => (int)code[item.index + 1])];
}
