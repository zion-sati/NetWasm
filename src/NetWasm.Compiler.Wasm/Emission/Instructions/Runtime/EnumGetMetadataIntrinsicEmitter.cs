// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions.Dispatch;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class EnumGetMetadataIntrinsicEmitter(
    IEnumMetadataSource metadata,
    IEnumTypeArgumentValidator typeArguments,
    IImplicitExceptionEmitter exceptions,
    IAddressInstructionEmitter addresses,
    IKeyedDispatchEmitter keyedDispatch) : IRuntimeIntrinsicEmitter
{
    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        if (request.Method.MethodArguments.Length == 1)
        {
            var enumType = request.Method.MethodArguments[0];
            var entry = metadata.EnumMetadata
                .SingleOrDefault(candidate => candidate.EnumType.Equals(enumType));
            // Enum.Parse<TEnum> and its siblings constrain TEnum only to a struct,
            // so it may name no enum: that throws, as a Type naming none does.
            if (entry.TypeId == 0)
            {
                exceptions.Emit(code, ManagedExceptionKind.Argument, request.Instruction.Context.ManagedThrowFunction);
                return;
            }

            addresses.Emit(code, entry.Address);
            SetResult(request, code);
            return;
        }

        var type = request.Local(0, CliValueKind.ManagedReference);
        var typeId = request.Instruction.Context.NumericTemporaryI4;
        typeArguments.Validate(code, type, typeId, request.Instruction.Context.ManagedThrowFunction);
        addresses.Emit(code, 0);
        SetResult(request, code);
        // The enum type's id picks its metadata, searched among the enums the program describes.
        var cases = new List<(long Key, Action Emit)>();
        foreach (var entry in metadata.EnumMetadata)
        {
            cases.Add((entry.TypeId, () =>
            {
                addresses.Emit(code, entry.Address);
                SetResult(request, code);
            }
            ));
        }
        keyedDispatch.Emit(code, KeyedDispatch.Of(typeId, false, cases, () => { }));
    }

    private static void SetResult(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)request.Local(0, CliValueKind.NativeInt))));
}
