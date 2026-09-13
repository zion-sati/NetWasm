using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Support;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class NullableGetUnderlyingTypeIntrinsicEmitter(
    ITypeDescriptorSource descriptors,
    INullableTypeResolver nullableTypes,
    ITypeLayoutProvider typeLayouts,
    IRuntimeImportResolver runtimeImports,
    IAddressInstructionEmitter addresses,
    ITypeObjectIdReader typeIds) : IRuntimeIntrinsicEmitter
{
    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        var input = request.Local(0, CliValueKind.ManagedReference);
        var result = request.Local(0, CliValueKind.ManagedReference);
        var typeId = request.Instruction.Context.NumericTemporaryI4;
        typeIds.Read(code, input, typeId);
        addresses.Emit(code, 0);
        Set(code, result);

        foreach (var descriptor in descriptors.ConstructedTypeDescriptors)
        {
            var underlyingType = nullableTypes.Resolve(descriptor.Type);
            if (underlyingType is null)
            {
                continue;
            }

            Get(code, typeId);
            WriteI32(code, descriptor.TypeId);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.If,
                WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            WriteI32(code, typeLayouts.GetObjectLayout(underlyingType).TypeId);
            WriteI32(code, typeLayouts.TypeTypeId);
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.Call,
                WasmInstructionOperand.Unsigned(
                    (uint)runtimeImports.Resolve(RuntimeImportSymbol.GetTypeObject))));
            Set(code, result);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        }
    }

    private static void Get(IWasmInstructionWriter code, int local) => code.Write(
        WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)local)));

    private static void Set(IWasmInstructionWriter code, int local) => code.Write(
        WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)local)));

    private static void WriteI32(IWasmInstructionWriter code, int value) => code.Write(
        WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(value)));
}
