using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Objects;

internal sealed class RectangularArrayElementStoreEmitter(
    ITargetLayout layouts,
    IAddressInstructionEmitter addressInstructions,
    IValueLayoutProvider values,
    IRuntimeObjectLayout objects,
    IRuntimeImportResolver runtimeImports,
    IImplicitExceptionEmitter exceptions,
    IRectangularArrayElementAddressEmitter addresses) : InstructionCommandProvider
{
    public override ImmutableArray<InstructionCommand> Commands =>
    [
        new(
            CilOperation.StoreRectangularArrayElement,
            InstructionFamily.ArraysFieldsStatics,
            Emit),
    ];

    private void Emit(InstructionEmissionRequest request, IWasmInstructionWriter code)
    {
        var arrayType = ((CilOperand.TypeIdentity)request.Instruction.Operand).Value;
        var elementType = arrayType.ElementType!;
        var arraySlot = request.Stack.Count - arrayType.ArrayRank - 2;
        var valueSlot = request.Stack.Count - 1;
        var valueLocal = GetStackLocal(
            request.Context,
            valueSlot,
            elementType.StackKind);
        if (elementType.StackKind == CliValueKind.ManagedReference)
        {
            EmitReferenceTypeCheck(request, code, arraySlot, valueLocal);
        }
        addresses.Emit(new(request, arraySlot), code);
        Get(code, valueLocal);
        if (elementType.StackKind == CliValueKind.ValueType)
        {
            EmitAddressConstant(code, values.GetValueLayout(elementType).Size);
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.Prefixed,
                WasmInstructionOperand.PrefixedTriple(WasmOpcodes.MemoryCopy, 0, 0)));
        }
        else
        {
            ManagedMemoryEmitter.EmitStoreByType(
                code,
                layouts.Target,
                0,
                elementType,
                GetElementSize(elementType));
        }
        request.Stack.RemoveRange(arraySlot, arrayType.ArrayRank + 2);
    }

    private void EmitReferenceTypeCheck(
        InstructionEmissionRequest request,
        IWasmInstructionWriter code,
        int arraySlot,
        int valueLocal)
    {
        Get(code, valueLocal);
        addressInstructions.Emit(code, AddressOperation.EqualZero);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        Get(code, valueLocal);
        Get(code, GetStackLocal(
            request.Context,
            arraySlot,
            CliValueKind.ManagedReference));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Load,
            WasmInstructionOperand.Memory(
                2,
                (uint)objects.ArrayElementTypeIdOffset)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(
                RuntimeImportSymbol.IsAssignable))));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code, ManagedExceptionKind.ArrayTypeMismatch);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
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

    private static void Get(IWasmInstructionWriter code, int local) => code.Write(
        WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)local)));
}
