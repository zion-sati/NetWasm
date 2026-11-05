using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class DelegateCloneIntrinsicEmitter(
    ITargetLayout layouts,
    ITypeLayoutProvider types,
    IRuntimeObjectLayout objects,
    IRuntimeImportResolver runtimeImports,
    IImplicitExceptionEmitter exceptions) : IRuntimeIntrinsicEmitter
{
    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        var source = request.Local(0, CliValueKind.ManagedReference);
        foreach (var delegateType in request.Instruction.Target.DelegateTypes)
        {
            var layout = types.GetObjectLayout(delegateType);
            WriteLocalGet(code, source);
            WriteI32Load(code, 0);
            WriteI32Constant(code, layout.TypeId);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.If,
                WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));

            WriteAddressConstant(code, layout.Size);
            WriteI32Constant(code, layout.TypeId);
            WriteCall(code, runtimeImports.Resolve(RuntimeImportSymbol.Allocate));
            WriteLocalSet(code, request.Instruction.Context.ObjectTemporary);
            EmitAllocationFailureCheck(
                code,
                request.Instruction.Context.ObjectTemporary, request.Instruction.Context.ManagedThrowFunction);

            CopyField(
                code,
                source,
                request.Instruction.Context.ObjectTemporary,
                objects.DelegateTargetOffset,
                layouts.Target.ObjectReferenceSize);
            CopyField(
                code,
                source,
                request.Instruction.Context.ObjectTemporary,
                objects.DelegateMethodIdOffset,
                sizeof(int));
            CopyField(
                code,
                source,
                request.Instruction.Context.ObjectTemporary,
                objects.DelegateInvocationListOffset,
                layouts.Target.ObjectReferenceSize);
            CopyField(
                code,
                source,
                request.Instruction.Context.ObjectTemporary,
                objects.DelegateInvocationCountOffset,
                sizeof(int));
            WriteLocalGet(code, request.Instruction.Context.ObjectTemporary);
            WriteLocalSet(code, source);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
        }

        exceptions.Emit(code, ManagedExceptionKind.InvalidCast, request.Instruction.Context.ManagedThrowFunction);
        for (var index = 0;
             index < request.Instruction.Target.DelegateTypes.Count;
             index++)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        }
    }

    private void CopyField(
        IWasmInstructionWriter code,
        int source,
        int destination,
        int offset,
        int size)
    {
        WriteLocalGet(code, destination);
        WriteLocalGet(code, source);
        ManagedMemoryEmitter.EmitLoadBySize(code, layouts.Target, offset, size);
        ManagedMemoryEmitter.EmitStoreBySize(code, layouts.Target, offset, size);
    }

    private void EmitAllocationFailureCheck(
        IWasmInstructionWriter code,
        int objectLocal,
        OptionalFunctionIndex throwFunction)
    {
        WriteLocalGet(code, objectLocal);
        code.Write(WasmInstruction.NoOperand(
            layouts.Target.UsesMemory64
                ? WasmOpcodes.I64EqualZero
                : WasmOpcodes.I32EqualZero));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code, ManagedExceptionKind.OutOfMemory, throwFunction);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private void WriteAddressConstant(IWasmInstructionWriter code, int value) =>
        code.Write(WasmInstruction.WithOperand(
            layouts.Target.UsesMemory64
                ? WasmOpcodes.I64Constant
                : WasmOpcodes.I32Constant,
            layouts.Target.UsesMemory64
                ? WasmInstructionOperand.Signed64(value)
                : WasmInstructionOperand.Signed(value)));

    private static void WriteI32Load(IWasmInstructionWriter code, int offset) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Load,
            WasmInstructionOperand.Memory(2, checked((uint)offset))));

    private static void WriteI32Constant(IWasmInstructionWriter code, int value) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(value)));

    private static void WriteLocalGet(IWasmInstructionWriter code, int index) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned(checked((uint)index))));

    private static void WriteLocalSet(IWasmInstructionWriter code, int index) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned(checked((uint)index))));

    private static void WriteCall(IWasmInstructionWriter code, int index) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned(checked((uint)index))));
}
