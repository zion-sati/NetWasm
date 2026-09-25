using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class WeakHandleIntrinsicEmitterTests
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 2)]
    public void CreatePreservesTargetAndRequestedLifetimeAtEveryArgumentBase(bool memory64, int argumentBase)
    {
        var imports = WasmRuntimeImports.CreateCatalog();
        var emitter = new WeakHandleCreateIntrinsicEmitter(imports);
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.WeakHandleCreate,
            [.. Enumerable.Repeat(CliValueKind.I4, argumentBase), CliValueKind.ManagedReference, CliValueKind.I4]);
        request = request with
        {
            Target = memory64 ? WasmTargetLayout.Wasm64 : WasmTargetLayout.Wasm32,
            Call = request.Call with { ArgumentBase = argumentBase, Consumed = 2 },
        };
        var locals = request.Instruction.Context.StackLocals;
        var writer = new RecordingWriter();

        Emit(emitter, request, writer);

        Assert.Equal(
            [WasmOpcodes.LocalGet, WasmOpcodes.LocalGet, WasmOpcodes.Call, WasmOpcodes.LocalSet],
            writer.Instructions.Select(instruction => instruction.Opcode));
        Assert.Equal(
            (uint)((memory64 ? locals.ReferenceBase : locals.I4Base) + argumentBase),
            writer.Instructions[0].Operand.UnsignedValue);
        Assert.Equal((uint)(locals.I4Base + argumentBase + 1), writer.Instructions[1].Operand.UnsignedValue);
        Assert.Equal((uint)imports.Resolve(RuntimeImportSymbol.WeakHandleCreate), writer.Instructions[2].Operand.UnsignedValue);
        Assert.Equal((uint)(locals.I4Base + argumentBase), writer.Instructions[3].Operand.UnsignedValue);
    }

    public static TheoryData<RuntimeIntrinsic, CliValueKind[], byte[]> Cases() => new()
    {
        {
            RuntimeIntrinsic.WeakHandleCreate,
            [CliValueKind.ManagedReference, CliValueKind.I4],
            [WasmOpcodes.LocalGet, WasmOpcodes.LocalGet, WasmOpcodes.Call, WasmOpcodes.LocalSet]
        },
        {
            RuntimeIntrinsic.WeakHandleGet,
            [CliValueKind.I4],
            [WasmOpcodes.LocalGet, WasmOpcodes.Call, WasmOpcodes.LocalSet]
        },
        {
            RuntimeIntrinsic.WeakHandleSet,
            [CliValueKind.I4, CliValueKind.ManagedReference],
            [WasmOpcodes.LocalGet, WasmOpcodes.LocalGet, WasmOpcodes.Call]
        },
        {
            RuntimeIntrinsic.WeakHandleRelease,
            [CliValueKind.I4],
            [WasmOpcodes.LocalGet, WasmOpcodes.Call]
        },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void EmitsEachWeakHandleOperationThroughItsCapability(
        RuntimeIntrinsic intrinsic,
        CliValueKind[] stack,
        byte[] expectedOpcodes)
    {
        var runtimeImports = WasmRuntimeImports.CreateCatalog();
        IRuntimeIntrinsicEmitter emitter = intrinsic switch
        {
            RuntimeIntrinsic.WeakHandleCreate => new WeakHandleCreateIntrinsicEmitter(runtimeImports),
            RuntimeIntrinsic.WeakHandleGet => new WeakHandleGetIntrinsicEmitter(runtimeImports),
            RuntimeIntrinsic.WeakHandleSet => new WeakHandleSetIntrinsicEmitter(runtimeImports),
            RuntimeIntrinsic.WeakHandleRelease => new WeakHandleReleaseIntrinsicEmitter(runtimeImports),
            _ => throw new ArgumentOutOfRangeException(nameof(intrinsic)),
        };
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(intrinsic, stack);
        var writer = new RecordingWriter();

        emitter.Emit(request, writer);

        var instructions = writer.Instructions;
        Assert.Equal(expectedOpcodes, instructions.Select(instruction => instruction.Opcode));
        var call = Assert.Single(instructions, instruction => instruction.Opcode == WasmOpcodes.Call);
        var symbol = intrinsic switch
        {
            RuntimeIntrinsic.WeakHandleCreate => RuntimeImportSymbol.WeakHandleCreate,
            RuntimeIntrinsic.WeakHandleGet => RuntimeImportSymbol.WeakHandleGet,
            RuntimeIntrinsic.WeakHandleSet => RuntimeImportSymbol.WeakHandleSet,
            RuntimeIntrinsic.WeakHandleRelease => RuntimeImportSymbol.WeakHandleRelease,
            _ => throw new ArgumentOutOfRangeException(nameof(intrinsic)),
        };
        Assert.Equal((uint)runtimeImports.Resolve(symbol), call.Operand.UnsignedValue);

        if (intrinsic == RuntimeIntrinsic.WeakHandleSet)
        {
            Assert.Equal(
                [0u, 1u],
                writer.Instructions.Take(2).Select(instruction => instruction.Operand.UnsignedValue));
        }
    }

    private static void Emit<TEmitter>(
        TEmitter emitter,
        RuntimeIntrinsicEmissionRequest request,
        RecordingWriter writer)
        where TEmitter : IRuntimeIntrinsicEmitter => emitter.Emit(request, writer);

    private sealed class RecordingWriter : IWasmInstructionWriter
    {
        private readonly List<WasmInstruction> _instructions = [];

        public List<WasmInstruction> Instructions => _instructions;

        public void Write(WasmInstruction instruction) => _instructions.Add(instruction);
    }
}
