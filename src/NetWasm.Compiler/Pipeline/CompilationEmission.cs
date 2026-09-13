using System;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Pipeline;

internal sealed class CompilationEmission(
    WasmModuleEmissionResult result,
    WasmMethodLoweringResult lowering)
{
    public WasmModuleEmissionResult Result { get; } = result ??
        throw new ArgumentNullException(nameof(result));

    public WasmMethodLoweringResult Lowering { get; } = lowering ??
        throw new ArgumentNullException(nameof(lowering));
}
