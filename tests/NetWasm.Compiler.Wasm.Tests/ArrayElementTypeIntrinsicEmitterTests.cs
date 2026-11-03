using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class ArrayElementTypeIntrinsicEmitterTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ReadsElementIdentityPublishesRootsAndChecksAllocation(WasmTarget target)
    {
        var layout = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var addresses = new RecordingAddresses();
        var exceptions = new RecordingExceptions();
        var roots = new RecordingRoots();
        var imports = WasmRuntimeImports.CreateCatalog();
        var emitter = Assert.IsAssignableFrom<IRuntimeIntrinsicEmitter>(
            new ArrayElementTypeIntrinsicEmitter(layout, layout, imports, roots, addresses, exceptions));
        var request = CreateRuntimeIntrinsicRequest(RuntimeIntrinsic.ArrayElementType,
            [CliValueKind.ManagedReference]) with
        { Target = WasmTargetLayout.For(target) };
        var writer = new RecordingInstructionWriter();

        emitter.Emit(request, writer);

        var code = writer.ToInstructions();
        Assert.Same(request.Instruction, roots.Request);
        Assert.Same(writer, roots.Writer);
        Assert.Equal(0, roots.InstructionCount);
        Assert.Equal([WasmOpcodes.LocalGet, WasmOpcodes.I32Load, WasmOpcodes.I32Constant, WasmOpcodes.Call,
            WasmOpcodes.LocalTee, WasmOpcodes.If, WasmOpcodes.End], code.Select(item => item.Opcode));
        Assert.Equal((uint)request.Local(0, CliValueKind.ManagedReference), code[0].Operand!.UnsignedValue);
        Assert.Equal((uint)imports.Resolve(RuntimeImportSymbol.GetTypeObject, request.Instruction.Target.RuntimeImportSelection), code[3].Operand!.UnsignedValue);
        Assert.Equal((uint)request.Local(0, CliValueKind.ManagedReference), code[4].Operand!.UnsignedValue);
        Assert.Equal((uint)layout.ArrayElementTypeIdOffset, code[1].Operand.Offset);
        Assert.Equal(layout.TypeTypeId, code[2].Operand.SignedValue);
        Assert.Empty(addresses.Constants);
        Assert.Equal([AddressOperation.EqualZero], addresses.Operations);
        Assert.Equal([ManagedExceptionKind.OutOfMemory], exceptions.Kinds);
    }

    private sealed class RecordingRoots : IRootPublicationEmitter
    {
        public InstructionEmissionRequest? Request { get; private set; }
        public IWasmInstructionWriter? Writer { get; private set; }
        public int InstructionCount { get; private set; }

        public void Emit(InstructionEmissionRequest request, IWasmInstructionWriter code, bool constructorCall = false)
        {
            Assert.False(constructorCall);
            Request = request;
            Writer = code;
            InstructionCount = Assert.IsType<RecordingInstructionWriter>(code).ToInstructions().Length;
        }
    }

    private sealed class RecordingAddresses : IAddressInstructionEmitter
    {
        public List<int> Constants { get; } = [];
        public List<AddressOperation> Operations { get; } = [];

        public void Emit(IWasmInstructionWriter code, int constant) => Constants.Add(constant);
        public void Emit(IWasmInstructionWriter code, AddressOperation operation) => Operations.Add(operation);
    }

    private sealed class RecordingExceptions : IImplicitExceptionEmitter
    {
        public List<ManagedExceptionKind> Kinds { get; } = [];
        public void Emit(IWasmInstructionWriter code, ManagedExceptionKind kind, OptionalFunctionIndex throwFunctionIndex) => Kinds.Add(kind);
    }
}
