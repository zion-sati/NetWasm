// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

// An entry readies the runtime state, then runs the module initializers in their planned order.
// Each call is to the initializer's guard function, so a module initializer runs once, in the
// first entry, where it may wait as that entry's own code may (as on desktop .NET, before any
// code of its module runs).
internal sealed class EntryInitializerFunctionEmitter(IGeneratedFunctionWriterFactory writers)
    : IEntryInitializerFunctionEmitter
{
    public byte[] Emit(int stateInitializerIndex, ImmutableArray<ModuleInitializerCall> moduleInitializers)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(stateInitializerIndex);
        if (moduleInitializers.IsDefaultOrEmpty)
        {
            throw new ArgumentException("An entry initializer runs at least one module initializer.",
                nameof(moduleInitializers));
        }

        var code = writers.Create();
        code.Bytes.Write([0]);
        Call(code, stateInitializerIndex);
        foreach (var initializer in moduleInitializers)
        {
            Call(code, initializer.FunctionIndex);
        }

        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        return code.Snapshots.Read();
    }

    private static void Call(GeneratedFunctionWriterLease code, int functionIndex) =>
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)functionIndex)));
}
