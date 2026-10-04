using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class EnumGetMetadataIntrinsicEmitter(
    IEnumMetadataSource metadata,
    IEnumTypeArgumentValidator typeArguments,
    IAddressInstructionEmitter addresses) : IRuntimeIntrinsicEmitter
{
    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        if (request.Method.MethodArguments.Length == 1)
        {
            var enumType = request.Method.MethodArguments[0];
            var entry = metadata.EnumMetadata
                .Single(candidate => candidate.EnumType.Equals(enumType));

            addresses.Emit(code, entry.Address);
            SetResult(request, code);
            return;
        }

        var type = request.Local(0, CliValueKind.ManagedReference);
        var typeId = request.Instruction.Context.NumericTemporaryI4;
        typeArguments.Validate(code, type, typeId);
        addresses.Emit(code, 0);
        SetResult(request, code);
        foreach (var entry in metadata.EnumMetadata)
        {
            Get(code, typeId);
            WriteI32(code, entry.TypeId);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.If,
                WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            addresses.Emit(code, entry.Address);
            SetResult(request, code);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        }
    }

    private static void SetResult(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)request.Local(0, CliValueKind.NativeInt))));

    private static void Get(IWasmInstructionWriter code, int local) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)local)));

    private static void WriteI32(IWasmInstructionWriter code, int value) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(value)));
}
