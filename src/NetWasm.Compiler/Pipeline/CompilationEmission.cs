using System;
using NetWasm.Compiler.Symbols;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Pipeline;

internal sealed class CompilationEmission(
    WasmModuleEmissionResult result,
    WasmMethodLoweringResult lowering,
    RuntimeTypeSymbolTable typeSymbols)
{
    public WasmModuleEmissionResult Result { get; } = result ??
        throw new ArgumentNullException(nameof(result));

    public WasmMethodLoweringResult Lowering { get; } = lowering ??
        throw new ArgumentNullException(nameof(lowering));

    // Every runtime type's names, which the type-symbols sidecar takes.
    public RuntimeTypeSymbolTable TypeSymbols { get; } = typeSymbols ??
        throw new ArgumentNullException(nameof(typeSymbols));
}
