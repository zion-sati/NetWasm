using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Support;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class EnumGetUnderlyingTypeEmitter(
    IEnumMetadataSource metadata,
    ITypeRepository types,
    ITypeLayoutProvider typeLayouts,
    IRuntimeImportResolver runtimeImports,
    IAddressInstructionEmitter addresses,
    IEnumTypeArgumentValidator typeArguments) : IEnumGetUnderlyingTypeEmitter
{
    public void EmitGetUnderlyingType(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        var input = request.Local(0, CliValueKind.ManagedReference);
        var result = request.Local(0, CliValueKind.ManagedReference);
        var typeId = request.Instruction.Context.NumericTemporaryI4;
        typeArguments.Validate(code, input, typeId);
        addresses.Emit(code, 0);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)result)));
        foreach (var entry in metadata.EnumMetadata)
        {
            var underlying = types.GetTypeDefinition(entry.Type).EnumUnderlyingType;
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)typeId)));
            WriteI32(code, entry.TypeId);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            WriteI32(code, typeLayouts.GetObjectLayout(underlying).TypeId);
            WriteI32(code, typeLayouts.TypeTypeId);
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.Call,
                WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(RuntimeImportSymbol.GetTypeObject))));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)result)));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        }
    }

    private static void WriteI32(IWasmInstructionWriter code, int value) => code.Write(
        WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(value)));
}
