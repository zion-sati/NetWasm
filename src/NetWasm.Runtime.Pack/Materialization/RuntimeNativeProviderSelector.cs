using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;

namespace NetWasm.Runtime.Pack.Materialization;

internal sealed class RuntimeNativeProviderSelector : IRuntimeNativeProviderSelector
{
    public ImmutableArray<RuntimeNativeBinding> Select(RuntimeNativeProviderSelectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Target is not ("wasm32" or "wasm64") || request.Imports.IsDefault || request.Providers.IsDefault)
            throw new InvalidOperationException("The NetWasm native provider selection request is invalid.");
        var providers = new Dictionary<string, RuntimeNativeLibrary>(StringComparer.Ordinal);
        var needed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var import in request.Imports)
        {
            if (import is null || !ValidName(import.LibraryName) || !ValidName(import.EntryPoint) ||
                import.Parameters.IsDefault || import.Parameters.Any(type => !Enum.IsDefined(type)) ||
                import.ReturnType is { } result && !Enum.IsDefined(result))
                throw new InvalidOperationException("The NetWasm native import contract is invalid.");
            needed.Add(import.LibraryName);
        }
        foreach (var provider in request.Providers)
        {
            if (provider is null || !ValidName(provider.LibraryName) ||
                provider.Target is not ("wasm32" or "wasm64") || string.IsNullOrWhiteSpace(provider.Path) ||
                !Path.IsPathFullyQualified(provider.Path) || HasLineControl(provider.Path) || provider.Sha256 is not { Length: 64 } ||
                !provider.Sha256.All(Uri.IsHexDigit))
                throw new InvalidOperationException("The NetWasm native library descriptor is invalid.");
            if (provider.Target != request.Target || !needed.Contains(provider.LibraryName))
                continue;
            var normalized = provider with { Path = Path.GetFullPath(provider.Path), Sha256 = provider.Sha256.ToLowerInvariant() };
            if (providers.TryGetValue(provider.LibraryName, out var previous))
            {
                if (previous != normalized)
                    throw new InvalidOperationException("Multiple native providers conflict for the same logical library and target.");
            }
            else
                providers.Add(provider.LibraryName, normalized);
        }

        var bindings = new Dictionary<string, RuntimeNativeBinding>(StringComparer.Ordinal);
        foreach (var import in request.Imports.OrderBy(import => import.EntryPoint, StringComparer.Ordinal)
            .ThenBy(import => import.LibraryName, StringComparer.Ordinal))
        {
            if (!providers.TryGetValue(import.LibraryName, out var provider))
                throw new InvalidOperationException("A reached native library has no provider for the selected target.");
            var binding = new RuntimeNativeBinding(import, provider);
            if (bindings.TryGetValue(import.EntryPoint, out var previous))
            {
                if (previous.Provider.Path != provider.Path || previous.Provider.Sha256 != provider.Sha256 ||
                    previous.Import.ReturnType != import.ReturnType || !previous.Import.Parameters.SequenceEqual(import.Parameters))
                    throw new InvalidOperationException("A native global symbol has conflicting providers or physical signatures.");
            }
            else
                bindings.Add(import.EntryPoint, binding);
        }
        return [.. bindings.Values];
    }

    private static bool ValidName(string? name) => !string.IsNullOrWhiteSpace(name) && !HasLineControl(name) &&
        !name.Contains(": definition of ", StringComparison.Ordinal) &&
        !name.Contains(": lazy definition of ", StringComparison.Ordinal) &&
        !name.Contains(": reference to ", StringComparison.Ordinal);

    private static bool HasLineControl(string value) => value.IndexOfAny(['\0', '\r', '\n']) >= 0;
}
