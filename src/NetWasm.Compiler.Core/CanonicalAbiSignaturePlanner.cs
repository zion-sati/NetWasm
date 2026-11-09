using System;
using System.Collections.Immutable;
using System.Linq;

namespace NetWasm.Compiler.Core;

public sealed record CanonicalAbiCoreSignature(
    ImmutableArray<CliValueKind> Parameters,
    CliValueKind Result,
    ImmutableArray<CliValueKind> FlatParameters,
    ImmutableArray<CliValueKind> FlatResults,
    bool IndirectParameters,
    bool IndirectResult);

public interface ICanonicalAbiSignaturePlanner
{
    CanonicalAbiCoreSignature Plan(
        CanonicalAbiFunction abiFunction,
        CanonicalAbiDirection direction);
}

public sealed class CanonicalAbiSignaturePlanner(
    ICanonicalAbiTypeFlattener flattener) : ICanonicalAbiSignaturePlanner
{
    private const int MaximumFlatParameters = 16;
    private const int MaximumFlatAsyncParameters = 4;
    private const int MaximumFlatResults = 1;
    private readonly ICanonicalAbiTypeFlattener _flattener = flattener ??
        throw new ArgumentNullException(nameof(flattener));

    public CanonicalAbiCoreSignature Plan(
        CanonicalAbiFunction abiFunction,
        CanonicalAbiDirection direction)
    {
        ArgumentNullException.ThrowIfNull(abiFunction);
        var flatParameters = abiFunction.Parameters
            .SelectMany(parameter => _flattener.Flatten(parameter.Type))
            .ToImmutableArray();
        var flatResults = abiFunction.Result is null
            ? ImmutableArray<CliValueKind>.Empty
            : _flattener.Flatten(abiFunction.Result);
        var asynchronous = direction == CanonicalAbiDirection.AsyncLoweredImport;
        var indirectParameters = flatParameters.Length >
            (asynchronous ? MaximumFlatAsyncParameters : MaximumFlatParameters);
        var indirectResult = asynchronous
            ? !flatResults.IsEmpty
            : flatResults.Length > MaximumFlatResults;
        var parameters = indirectParameters
            ? ImmutableArray.Create(CliValueKind.ManagedAddress)
            : flatParameters;
        if (indirectResult && direction != CanonicalAbiDirection.LiftedExport)
        {
            parameters = parameters.Add(CliValueKind.ManagedAddress);
        }
        return new(
            parameters,
            asynchronous
                ? CliValueKind.I4
                : indirectResult
                    ? direction == CanonicalAbiDirection.LoweredImport
                        ? CliValueKind.Void
                        : CliValueKind.ManagedAddress
                    : flatResults.IsEmpty
                        ? CliValueKind.Void
                        : flatResults[0],
            flatParameters,
            flatResults,
            indirectParameters,
            indirectResult);
    }

    public static ImmutableArray<CliValueKind> PostReturnParameters(
        CanonicalAbiCoreSignature signature)
    {
        ArgumentNullException.ThrowIfNull(signature);
        return signature.IndirectResult
            ? [CliValueKind.ManagedAddress]
            : signature.Result == CliValueKind.Void
                ? []
                : [signature.Result];
    }
}
