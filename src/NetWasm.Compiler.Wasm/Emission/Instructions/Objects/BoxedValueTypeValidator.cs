using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Objects;

internal sealed class BoxedValueTypeValidator(
    ITypeLayoutProvider typeLayouts,
    IEnumStorageResolver enumStorages,
    IImplicitExceptionEmitter exceptions) : IBoxedValueTypeValidator
{
    public void Validate(
        IWasmInstructionWriter code,
        int objectLocal,
        CliTypeIdentity targetType)
    {
        var targetTypeId = typeLayouts.GetObjectLayout(targetType).TypeId;
        var storages = enumStorages.Resolve();
        var targetStorage = storages.FirstOrDefault(
            storage => storage.EnumType.Equals(targetType));
        var underlyingType = targetStorage?.UnderlyingType ?? targetType;
        var compatibleTypeIds = storages
            .Where(storage => storage.UnderlyingType.Equals(underlyingType))
            .Select(storage => storage.Descriptor.TypeId)
            .Append(targetTypeId);

        if (targetStorage is not null &&
            typeLayouts.GetObjectLayout(targetStorage.UnderlyingType, out var underlyingLayout))
        {
            compatibleTypeIds = compatibleTypeIds.Append(underlyingLayout.TypeId);
        }

        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Block,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        foreach (var typeId in compatibleTypeIds.Distinct())
        {
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalGet,
                WasmInstructionOperand.Unsigned((uint)objectLocal)));
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.I32Load,
                WasmInstructionOperand.Memory(2, 0)));
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.I32Constant,
                WasmInstructionOperand.Signed(typeId)));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.BranchIf,
                WasmInstructionOperand.Unsigned(0)));
        }

        exceptions.Emit(code, ManagedExceptionKind.InvalidCast);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }
}
