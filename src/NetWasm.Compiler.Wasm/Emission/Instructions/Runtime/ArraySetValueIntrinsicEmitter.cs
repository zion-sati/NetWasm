using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class ArraySetValueIntrinsicEmitter(
    ITypeDescriptorSource descriptors,
    INullableTypeResolver nullableTypes,
    ITypeLayoutProvider types,
    IValueLayoutProvider values,
    IRuntimeObjectLayout objects,
    IRuntimeImportResolver imports,
    IAddressInstructionEmitter addresses) : IRuntimeIntrinsicEmitter
{
    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        var element = request.Instruction.Context.NumericTemporaryI4;
        var underlying = request.Instruction.Context.NumericTemporaryI4Second;
        var offset = request.Instruction.Context.ObjectTemporary;
        Get(code, request.Local(0, CliValueKind.ManagedReference));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Load,
            WasmInstructionOperand.Memory(2, (uint)objects.ArrayElementTypeIdOffset)));
        Set(code, element);
        I32(code, 0); Set(code, underlying);
        addresses.Emit(code, 0); Set(code, offset);
        foreach (var descriptor in descriptors.ConstructedTypeDescriptors)
        {
            // A Nullable<T> that no array holds has no boxed layout and cannot be an element here.
            var type = nullableTypes.Resolve(descriptor.Type);
            if (type is null || !types.GetObjectLayout(type, out var boxed)) continue;
            Get(code, element); I32(code, descriptor.TypeId);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.If,
                WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            I32(code, boxed.TypeId); Set(code, underlying);
            addresses.Emit(code, WasmTargetLayout.Align(sizeof(byte), values.GetValueLayout(type).Alignment));
            Set(code, offset);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        }
        Get(code, request.Local(0, CliValueKind.ManagedReference));
        Get(code, request.Local(1, CliValueKind.I4));
        Get(code, request.Local(2, CliValueKind.ManagedReference));
        Get(code, underlying); Get(code, offset);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)imports.Resolve(RuntimeImportSymbol.ArraySetValue,
                request.Instruction.Target.RuntimeImportSelection))));
        Set(code, request.Local(0, CliValueKind.I4));
    }

    private static void Get(IWasmInstructionWriter code, int local) => code.Write(
        WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)local)));
    private static void Set(IWasmInstructionWriter code, int local) => code.Write(
        WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)local)));
    private static void I32(IWasmInstructionWriter code, int value) => code.Write(
        WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(value)));
}
