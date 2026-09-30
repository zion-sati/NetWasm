using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal static class RequestedExportMethodSelector
{
    public static ImmutableDictionary<string, MethodInstanceModel> Select(
        IReadOnlyDictionary<string, EntityKey> requestedExports,
        IReadOnlyDictionary<string, MethodInstanceModel> methodInstances)
    {
        ArgumentNullException.ThrowIfNull(requestedExports);
        ArgumentNullException.ThrowIfNull(methodInstances);

        var selected = ImmutableDictionary.CreateBuilder<string,
            MethodInstanceModel>(StringComparer.Ordinal);
        foreach ((var name, var methodKey) in requestedExports)
        {
            MethodInstanceModel? match = null;
            foreach (var candidate in methodInstances.Values)
            {
                if (candidate.IsConstructed ||
                    candidate.Definition.Key != methodKey)
                {
                    continue;
                }
                if (match is not null)
                {
                    throw Invariant(
                        methodKey,
                        $"requested export '{name}' has multiple direct selected method instances");
                }
                match = candidate;
            }

            selected.Add(
                name,
                match ?? throw Invariant(
                    methodKey,
                    $"requested export '{name}' has no direct selected method instance"));
        }
        return selected.ToImmutable();
    }

    private static CompilerException Invariant(EntityKey method, string message) =>
        new(new CompilerDiagnostic(
            DiagnosticCode.CompilerInvariant,
            message,
            method.ToString()));
}
