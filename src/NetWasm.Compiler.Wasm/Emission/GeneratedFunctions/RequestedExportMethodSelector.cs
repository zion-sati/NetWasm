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

        // The direct instances by definition, once, rather than every instance for every export.
        var direct = new Dictionary<EntityKey, (MethodInstanceModel First, bool Repeated)>();
        foreach (var candidate in methodInstances.Values)
        {
            if (candidate.IsConstructed)
            {
                continue;
            }
            direct[candidate.Definition.Key] = direct.TryGetValue(candidate.Definition.Key, out var found)
                ? (found.First, true)
                : (candidate, false);
        }

        var selected = ImmutableDictionary.CreateBuilder<string,
            MethodInstanceModel>(StringComparer.Ordinal);
        foreach ((var name, var methodKey) in requestedExports)
        {
            if (!direct.TryGetValue(methodKey, out var match))
            {
                throw Invariant(
                    methodKey,
                    $"requested export '{name}' has no direct selected method instance");
            }
            if (match.Repeated)
            {
                throw Invariant(
                    methodKey,
                    $"requested export '{name}' has multiple direct selected method instances");
            }
            selected.Add(name, match.First);
        }
        return selected.ToImmutable();
    }

    private static CompilerException Invariant(EntityKey method, string message) =>
        new(new CompilerDiagnostic(
            DiagnosticCode.CompilerInvariant,
            message,
            method.ToString()));
}
