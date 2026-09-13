using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Objects;

internal sealed class RectangularArrayElementLoadEmitter(
    ITargetLayout layouts,
    IValueLayoutProvider values,
    IRectangularArrayElementAddressEmitter addresses) : InstructionCommandProvider
{
    public override ImmutableArray<InstructionCommand> Commands =>
    [
        new(
            CilOperation.LoadRectangularArrayElement,
            InstructionFamily.ArraysFieldsStatics,
            Emit),
    ];

    private void Emit(InstructionEmissionRequest request, IWasmInstructionWriter code)
    {
        var arrayType = ((CilOperand.TypeIdentity)request.Instruction.Operand).Value;
        var elementType = arrayType.ElementType!;
        var arraySlot = request.Stack.Count - arrayType.ArrayRank - 1;
        addresses.Emit(new(request, arraySlot), code);
        if (elementType.StackKind == CliValueKind.ValueType)
        {
            var offset = request.Context.ValueLayout.TemporaryOffsets[
                request.Instruction.Offset];
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalSet,
                WasmInstructionOperand.Unsigned((uint)request.Context.ObjectTemporary)));
            Get(code, request.Context.ValueFrame);
            if (offset != 0)
            {
                EmitAddressConstant(code, offset);
                EmitAddressAdd(code);
            }
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalTee,
                WasmInstructionOperand.Unsigned((uint)GetStackLocal(
                    request.Context,
                    arraySlot,
                    CliValueKind.ValueType))));
            Get(code, request.Context.ObjectTemporary);
            EmitAddressConstant(code, values.GetValueLayout(elementType).Size);
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.Prefixed,
                WasmInstructionOperand.PrefixedTriple(WasmOpcodes.MemoryCopy, 0, 0)));
        }
        else
        {
            ManagedMemoryEmitter.EmitLoadByType(
                code,
                layouts.Target,
                0,
                elementType,
                GetElementSize(elementType));
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalSet,
                WasmInstructionOperand.Unsigned((uint)GetStackLocal(
                    request.Context,
                    arraySlot,
                    elementType.StackKind))));
        }
        request.Stack.RemoveRange(arraySlot + 1, arrayType.ArrayRank);
        request.Stack[arraySlot] = elementType.StackKind;
    }

    private int GetElementSize(CliTypeIdentity elementType) =>
        elementType.StackKind == CliValueKind.ManagedReference
            ? layouts.Target.ObjectReferenceSize
            : values.GetValueLayout(elementType).Size;

    private int GetStackLocal(
        MethodEmissionContext context,
        int slot,
        CliValueKind type) => WasmLocalLayoutPlanner.GetEvaluationStackLocal(
        context.StackLocals,
        slot,
        type,
        layouts.Target);

    private void EmitAddressConstant(IWasmInstructionWriter code, int value) =>
        code.Write(WasmInstruction.WithOperand(
            layouts.Target.AddressSize == sizeof(long)
                ? WasmOpcodes.I64Constant
                : WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(value)));

    private void EmitAddressAdd(IWasmInstructionWriter code) => code.Write(
        WasmInstruction.NoOperand(
            layouts.Target.AddressSize == sizeof(long)
                ? WasmOpcodes.I64Add
                : WasmOpcodes.I32Add));

    private static void Get(IWasmInstructionWriter code, int local) => code.Write(
        WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)local)));
}
