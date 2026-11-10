using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class ObjectCloneIntrinsicEmitterTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void TranslatesTheRuntimeAllocationFailureSentinel(WasmTarget target)
    {
        var receiver = new RecordingReceiver();
        var addresses = new RecordingAddresses();
        var exceptions = new RecordingExceptions();
        var imports = WasmRuntimeImports.CreateCatalog();
        var emitter = Assert.IsAssignableFrom<IRuntimeIntrinsicEmitter>(
            new ObjectCloneIntrinsicEmitter(receiver, imports, addresses, exceptions));
        var request = CreateRuntimeIntrinsicRequest(RuntimeIntrinsic.ObjectClone,
            [CliValueKind.ManagedReference]) with
        { Target = WasmTargetLayout.For(target) };
        var writer = new RecordingInstructionWriter();

        emitter.Emit(request, writer);

        var code = writer.ToInstructions();
        Assert.Equal(request.Local(0, CliValueKind.ManagedReference), receiver.Local);
        Assert.Same(writer, receiver.Writer);
        Assert.Equal(0, receiver.InstructionCount);
        Assert.Equal([WasmOpcodes.LocalGet, WasmOpcodes.Call,
            WasmOpcodes.LocalTee, WasmOpcodes.If, WasmOpcodes.End], code.Select(item => item.Opcode));
        Assert.Equal((uint)request.Local(0, CliValueKind.ManagedReference), code[0].Operand!.UnsignedValue);
        Assert.Equal((uint)imports.Resolve(RuntimeImportSymbol.ObjectClone, request.Instruction.Target.RuntimeImportSelection), code[1].Operand!.UnsignedValue);
        Assert.Equal((uint)request.Local(0, CliValueKind.ManagedReference), code[2].Operand!.UnsignedValue);
        Assert.Equal([0], addresses.Constants);
        Assert.Equal([AddressOperation.Equal], addresses.Operations);
        Assert.Equal([ManagedExceptionKind.OutOfMemory], exceptions.Kinds);
    }

    private sealed class RecordingReceiver : IArrayReceiverValidator
    {
        public int? Local { get; private set; }
        public IWasmInstructionWriter? Writer { get; private set; }
        public int InstructionCount { get; private set; }

        public void Validate(IWasmInstructionWriter code, int receiverLocal, OptionalFunctionIndex throwFunction)
        {
            Local = receiverLocal;
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
