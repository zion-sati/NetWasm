// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions.Dispatch;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

// PtrToStructure creates a structure by its runtime type and runs its parameterless constructor: the type's semantic
// ID picks the constructor among the marshalled types that declare one, a class's with the object and a value type's
// with its boxed payload. A type without one is left as allocated.
internal sealed class StructMarshallingConstructIntrinsicEmitter(
    IStructMarshallingSource descriptors,
    IAddressInstructionEmitter addresses,
    IKeyedDispatchEmitter keyedDispatch) : IRuntimeIntrinsicEmitter
{
    private readonly IStructMarshallingSource _descriptors = descriptors ??
        throw new ArgumentNullException(nameof(descriptors));
    private readonly IAddressInstructionEmitter _addresses = addresses ??
        throw new ArgumentNullException(nameof(addresses));
    private readonly IKeyedDispatchEmitter _keyedDispatch = keyedDispatch ??
        throw new ArgumentNullException(nameof(keyedDispatch));

    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(code);
        var typeId = request.Local(0, CliValueKind.I4);
        var instance = request.Local(1, CliValueKind.ManagedReference);
        var cases = new List<(long Key, Action Emit)>();
        foreach (var descriptor in _descriptors.StructMarshalling)
        {
            if (descriptor.Constructor is not { } constructor)
            {
                continue;
            }
            cases.Add((descriptor.TypeId, () =>
            {
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)instance)));
                if (descriptor.Type.IsValueType)
                {
                    _addresses.Emit(code, descriptor.PayloadOffset);
                    _addresses.Emit(code, AddressOperation.Add);
                }
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call,
                    WasmInstructionOperand.Unsigned((uint)request.FunctionIndices.Resolve(constructor))));
            }
            ));
        }
        _keyedDispatch.Emit(code, KeyedDispatch.Of(typeId, false, cases, () => { }));
    }
}
