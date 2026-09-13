using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Objects;

internal sealed class NullableBoxEmitter(
    ITargetLayout layouts,
    IAddressInstructionEmitter addresses,
    ITypeLayoutProvider typeLayouts,
    IValueLayoutProvider values,
    IRuntimeImportResolver runtimeImports,
    IImplicitExceptionEmitter exceptions) : INullableBoxEmitter
{
    private static readonly CliTypeIdentity BooleanType =
        CliTypeIdentity.Primitive("bool", CliValueKind.I4);

    public void Emit(
        InstructionEmissionRequest request,
        IWasmInstructionWriter code,
        CliTypeIdentity underlyingType)
    {
        var slot = request.Stack.Count - 1;
        var sourceLocal = GetStackLocal(request, slot, CliValueKind.ValueType);
        var targetLocal = GetStackLocal(request, slot, CliValueKind.ManagedReference);
        var underlying = values.GetValueLayout(underlyingType);
        var boxed = typeLayouts.GetObjectLayout(underlyingType);

        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)sourceLocal)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)request.Context.ObjectTemporary)));
        addresses.Emit(code, 0);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)targetLocal)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)request.Context.ObjectTemporary)));
        ManagedMemoryEmitter.EmitLoadByType(
            code,
            layouts.Target,
            0,
            BooleanType,
            sizeof(byte));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));

        addresses.Emit(code, boxed.Size);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(boxed.TypeId)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(
                RuntimeImportSymbol.Allocate))));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalTee,
            WasmInstructionOperand.Unsigned((uint)targetLocal)));
        addresses.Emit(code, AddressOperation.EqualZero);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code, ManagedExceptionKind.OutOfMemory);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));

        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)targetLocal)));
        addresses.Emit(code, WasmTargetLayout.Align(
            layouts.Target.ObjectHeaderSize,
            underlying.Alignment));
        addresses.Emit(code, AddressOperation.Add);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)request.Context.ObjectTemporary)));
        addresses.Emit(code, Align(sizeof(byte), underlying.Alignment));
        addresses.Emit(code, AddressOperation.Add);
        if (underlyingType.StackKind == CliValueKind.ValueType)
        {
            addresses.Emit(code, underlying.Size);
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.Prefixed,
                WasmInstructionOperand.PrefixedTriple(
                    WasmOpcodes.MemoryCopy,
                    0,
                    0)));
        }
        else
        {
            ManagedMemoryEmitter.EmitLoadByType(
                code,
                layouts.Target,
                0,
                underlyingType,
                underlying.Size);
            ManagedMemoryEmitter.EmitStoreByType(
                code,
                layouts.Target,
                0,
                underlyingType,
                underlying.Size);
        }
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        request.Stack[slot] = CliValueKind.ManagedReference;
    }

    private int GetStackLocal(
        InstructionEmissionRequest request,
        int slot,
        CliValueKind type) => WasmLocalLayoutPlanner.GetEvaluationStackLocal(
        request.Context.StackLocals,
        slot,
        type,
        layouts.Target);

    private static int Align(int value, int alignment) =>
        checked((value + alignment - 1) / alignment * alignment);
}
