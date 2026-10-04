using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class EphemeronHandleIntrinsicEmitterTests
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 2)]
    public void CreatePreservesKeyAndValueAtEveryArgumentBase(
        bool memory64,
        int argumentBase)
    {
        var imports = WasmRuntimeImports.CreateCatalog();
        var emitter = new EphemeronHandleCreateIntrinsicEmitter(imports);
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.EphemeronHandleCreate,
            [
                .. Enumerable.Repeat(CliValueKind.I4, argumentBase),
                CliValueKind.ManagedReference,
                CliValueKind.ManagedReference,
            ]);
        request = request with
        {
            Target = memory64 ? WasmTargetLayout.Wasm64 : WasmTargetLayout.Wasm32,
            Call = request.Call with { ArgumentBase = argumentBase, Consumed = 2 },
        };
        var locals = request.Instruction.Context.StackLocals;
        var writer = new RecordingWriter();

        emitter.Emit(request, writer);

        Assert.Equal(
            [WasmOpcodes.LocalGet, WasmOpcodes.LocalGet, WasmOpcodes.Call, WasmOpcodes.LocalSet],
            writer.Instructions.Select(instruction => instruction.Opcode));
        Assert.Equal(
            (uint)((memory64 ? locals.ReferenceBase : locals.I4Base) + argumentBase),
            writer.Instructions[0].Operand.UnsignedValue);
        Assert.Equal(
            (uint)((memory64 ? locals.ReferenceBase : locals.I4Base) + argumentBase + 1),
            writer.Instructions[1].Operand.UnsignedValue);
        Assert.Equal(
            (uint)imports.Resolve(
                RuntimeImportSymbol.EphemeronHandleCreate,
                request.Instruction.Target.RuntimeImportSelection),
            writer.Instructions[2].Operand.UnsignedValue);
        Assert.Equal(
            (uint)(locals.I4Base + argumentBase),
            writer.Instructions[3].Operand.UnsignedValue);
    }

    public static TheoryData<RuntimeIntrinsic, byte[]> Cases() => new()
    {
        {
            RuntimeIntrinsic.EphemeronHandleGetKey,
            [WasmOpcodes.LocalGet, WasmOpcodes.Call, WasmOpcodes.LocalSet]
        },
        {
            RuntimeIntrinsic.EphemeronHandleGetValue,
            [WasmOpcodes.LocalGet, WasmOpcodes.Call, WasmOpcodes.LocalSet]
        },
        {
            RuntimeIntrinsic.EphemeronHandleRelease,
            [WasmOpcodes.LocalGet, WasmOpcodes.Call]
        },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void EmitsGetAndReleaseOperationsThroughTheirCapabilities(
        RuntimeIntrinsic intrinsic,
        byte[] expectedOpcodes)
    {
        var imports = WasmRuntimeImports.CreateCatalog();
        IRuntimeIntrinsicEmitter emitter = intrinsic switch
        {
            RuntimeIntrinsic.EphemeronHandleGetKey =>
                new EphemeronHandleGetKeyIntrinsicEmitter(imports),
            RuntimeIntrinsic.EphemeronHandleGetValue =>
                new EphemeronHandleGetValueIntrinsicEmitter(imports),
            RuntimeIntrinsic.EphemeronHandleRelease =>
                new EphemeronHandleReleaseIntrinsicEmitter(imports),
            _ => throw new ArgumentOutOfRangeException(nameof(intrinsic)),
        };
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(
            intrinsic,
            [CliValueKind.I4]);
        var writer = new RecordingWriter();

        emitter.Emit(request, writer);

        Assert.Equal(expectedOpcodes, writer.Instructions.Select(instruction => instruction.Opcode));
        var call = Assert.Single(
            writer.Instructions,
            instruction => instruction.Opcode == WasmOpcodes.Call);
        var symbol = intrinsic switch
        {
            RuntimeIntrinsic.EphemeronHandleGetKey =>
                RuntimeImportSymbol.EphemeronHandleGetKey,
            RuntimeIntrinsic.EphemeronHandleGetValue =>
                RuntimeImportSymbol.EphemeronHandleGetValue,
            RuntimeIntrinsic.EphemeronHandleRelease =>
                RuntimeImportSymbol.EphemeronHandleRelease,
            _ => throw new ArgumentOutOfRangeException(nameof(intrinsic)),
        };
        Assert.Equal(
            (uint)imports.Resolve(symbol, request.Instruction.Target.RuntimeImportSelection),
            call.Operand.UnsignedValue);
    }

    private sealed class RecordingWriter : IWasmInstructionWriter
    {
        private readonly List<WasmInstruction> _instructions = [];

        internal List<WasmInstruction> Instructions => _instructions;

        public void Write(WasmInstruction instruction) => _instructions.Add(instruction);
    }
}
