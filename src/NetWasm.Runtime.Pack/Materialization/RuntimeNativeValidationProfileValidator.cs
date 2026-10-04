using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;

namespace NetWasm.Runtime.Pack.Materialization;

internal sealed class RuntimeNativeValidationProfileValidator : IRuntimeNativeValidationProfileValidator
{
    private static readonly FrozenSet<string> SupportedFeatures = new[]
    {
        "mvp", "mutable-global", "saturating-float-to-int", "sign-extension", "reference-types",
        "multi-value", "bulk-memory", "memory64",
    }.ToFrozenSet(StringComparer.Ordinal);

    public void Validate(RuntimeNativeValidationProfile profile, string target)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (target is not ("wasm32" or "wasm64") || profile.Version != 1 ||
            profile.Features.IsDefaultOrEmpty || profile.Features[0] != "mvp" ||
            profile.Features.Any(feature => !SupportedFeatures.Contains(feature)) ||
            profile.Features.Distinct(StringComparer.Ordinal).Count() != profile.Features.Length ||
            profile.Features.Contains("memory64") != (target == "wasm64") || profile.Imports.IsDefaultOrEmpty)
            throw Invalid();
        var identities = new HashSet<(string Module, string Name)>();
        foreach (var import in profile.Imports)
        {
            if (import is null || string.IsNullOrWhiteSpace(import.Module) || string.IsNullOrWhiteSpace(import.Name) ||
                import.Module.IndexOfAny(['\0', '\r', '\n']) >= 0 || import.Name.IndexOfAny(['\0', '\r', '\n']) >= 0 ||
                import.Parameters.IsDefault || import.Results.IsDefault ||
                import.Parameters.Concat(import.Results).Any(type => type is not (0x7f or 0x7e or 0x7d or 0x7c)) ||
                !identities.Add((import.Module, import.Name)))
                throw Invalid();
        }
    }

    private static InvalidOperationException Invalid() => new("The static-native validation profile is unsupported or malformed.");
}
