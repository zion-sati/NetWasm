// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions.Dispatch;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Objects;

// A boxed value unboxes to its own type, its underlying primitive, or any enum of that primitive: its type id, read from
// the object's header, is searched among theirs, which grow with the program's enums.
internal sealed class BoxedValueTypeValidator(
    ITypeLayoutProvider typeLayouts,
    IEnumStorageResolver enumStorages,
    IImplicitExceptionEmitter exceptions,
    IKeyedDispatchEmitter keyedDispatch) : IBoxedValueTypeValidator
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

        keyedDispatch.Emit(code, KeyedDispatch.Membership(
            objectLocal,
            compatibleTypeIds,
            whenIn: () => { },
            whenOut: () => exceptions.Emit(code, mismatchException, throwFunction),
            loadKey: writer =>
            {
                writer.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.LocalGet,
                    WasmInstructionOperand.Unsigned((uint)objectLocal)));
                writer.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.I32Load,
                    WasmInstructionOperand.Memory(2, 0)));
            }));
    }
}
