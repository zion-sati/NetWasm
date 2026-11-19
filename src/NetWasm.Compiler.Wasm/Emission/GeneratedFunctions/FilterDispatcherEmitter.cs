// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Instructions.Dispatch;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal sealed class FilterDispatcherEmitter(
    IGeneratedFunctionWriterFactory writers,
    IKeyedDispatchEmitter keyedDispatch) : IFilterDispatcherEmitter
{
    public byte[] Emit(
        ImmutableArray<FilterFunclet> filters,
        IReadOnlyDictionary<int, int> functionIndices)
    {
        var code = writers.Create();
        code.Bytes.Write([0]);
        // The filter's id picks its funclet, searched among the method's filters.
        keyedDispatch.Emit(code.Instructions, KeyedDispatch.Of(
            keyLocal: 0,
            wide: false,
            filters.Select(filter => ((long)filter.Id, (Action)(() =>
            {
                code.Instructions.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.LocalGet,
                    WasmInstructionOperand.Unsigned(1)));
                code.Instructions.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.LocalGet,
                    WasmInstructionOperand.Unsigned(2)));
                code.Instructions.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.Call,
                    WasmInstructionOperand.Unsigned((uint)functionIndices[filter.Id])));
                code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.Return));
            }))),
            () => { }));
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(0)));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        return code.Snapshots.Read();
    }
}
