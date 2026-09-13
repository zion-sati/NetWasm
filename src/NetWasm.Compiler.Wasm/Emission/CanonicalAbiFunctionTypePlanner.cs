using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission;

internal sealed record CanonicalAbiFunctionLayout(
    WasmFunctionType CoreType,
    WasmFunctionType PostReturnType,
    ImmutableArray<CliValueKind> FlatParameters,
    ImmutableArray<CliValueKind> FlatResults,
    bool IndirectParameters,
    bool IndirectResult);

internal sealed class CanonicalAbiFunctionTypePlanner(
    ICanonicalAbiSignaturePlanner signatures) : ICanonicalAbiFunctionTypePlanner
{
    private readonly ICanonicalAbiSignaturePlanner _signatures = signatures ??
        throw new ArgumentNullException(nameof(signatures));

    public CanonicalAbiFunctionLayout Plan(
        CanonicalAbiFunction function,
        CanonicalAbiDirection direction)
    {
        ArgumentNullException.ThrowIfNull(function);
        var signature = _signatures.Plan(function, direction);
        var postParameters = CanonicalAbiSignaturePlanner.PostReturnParameters(signature);
        return new(
            new(signature.Parameters, signature.Result),
            new(postParameters, CliValueKind.Void),
            signature.FlatParameters,
            signature.FlatResults,
            signature.IndirectParameters,
            signature.IndirectResult);
    }

}
