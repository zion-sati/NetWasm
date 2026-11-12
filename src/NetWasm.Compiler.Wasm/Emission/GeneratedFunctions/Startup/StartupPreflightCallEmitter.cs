using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions.Startup;

internal sealed class StartupPreflightCallEmitter : IStartupPreflightCallEmitter
{
    public void Emit(
        GeneratedFunctionWriterLease code,
        ImmutableArray<EntityKey> preflights,
        IFunctionIndexResolver functionIndices)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(functionIndices);

        foreach (var method in preflights)
        {
            code.Instructions.Write(WasmInstruction.WithOperand(
                WasmOpcodes.Call,
                WasmInstructionOperand.Unsigned((uint)functionIndices.Resolve(method))));
        }
    }
}
