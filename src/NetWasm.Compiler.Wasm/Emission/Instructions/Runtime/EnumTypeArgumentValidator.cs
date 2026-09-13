using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class EnumTypeArgumentValidator(
    IEnumMetadataSource metadata,
    ITypeObjectIdReader typeIds,
    IImplicitExceptionEmitter exceptions) : IEnumTypeArgumentValidator
{
    public void Validate(IWasmInstructionWriter code, int typeLocal, int typeIdLocal)
    {
        typeIds.Read(code, typeLocal, typeIdLocal);

        WriteI32(code, 0);
        foreach (var entry in metadata.EnumMetadata)
        {
            Get(code, typeIdLocal);
            WriteI32(code, entry.TypeId);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Or));
        }
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code, ManagedExceptionKind.Argument);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private static void Get(IWasmInstructionWriter code, int local) => code.Write(
        WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)local)));

    private static void WriteI32(IWasmInstructionWriter code, int value) => code.Write(
        WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(value)));
}
