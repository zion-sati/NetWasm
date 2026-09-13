using System;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions.LocalTime;

internal sealed class LocalTimePreflightCallEmitter(
    ILocalTimePreflightMethodSelector methods) : ILocalTimePreflightCallEmitter
{
    private readonly ILocalTimePreflightMethodSelector _methods =
        methods ?? throw new ArgumentNullException(nameof(methods));

    public bool Emit(
        GeneratedFunctionWriterLease code,
        WasmEmissionRequest request,
        IFunctionIndexResolver functionIndices)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(functionIndices);

        var method = _methods.Select(request);
        if (method is null)
        {
            return false;
        }

        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)functionIndices.Resolve(method.Value))));
        return true;
    }
}
