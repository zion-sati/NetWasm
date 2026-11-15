// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Objects;

internal sealed class BoxedValueTypeValidator(
    ITypeLayoutProvider typeLayouts,
    IEnumStorageResolver enumStorages,
    IImplicitExceptionEmitter exceptions) : IBoxedValueTypeValidator
{
    public void Validate(
        IWasmInstructionWriter code,
        int objectLocal,
        CliTypeIdentity targetType,
        OptionalFunctionIndex throwFunction,
        ManagedExceptionKind mismatchException = ManagedExceptionKind.InvalidCast)
    {
        var targetTypeId = typeLayouts.GetObjectLayout(targetType).TypeId;
        var storages = enumStorages.Resolve();
        var targetStorage = storages.FirstOrDefault(
            storage => storage.EnumType.Equals(targetType));
        var underlyingType = targetStorage?.UnderlyingType ?? targetType;
        var compatibleTypeIds = storages
            .Where(storage => storage.UnderlyingType.Equals(underlyingType))
            .Select(storage => storage.TypeId)
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

        exceptions.Emit(code, mismatchException, throwFunction);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }
}
