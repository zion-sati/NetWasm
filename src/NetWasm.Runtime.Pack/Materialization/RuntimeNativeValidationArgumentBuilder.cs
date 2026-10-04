using System;
using System.Collections.Immutable;
using System.IO;

namespace NetWasm.Runtime.Pack.Materialization;

internal sealed class RuntimeNativeValidationArgumentBuilder(IRuntimeNativeValidationProfileValidator profiles) : IRuntimeNativeValidationArgumentBuilder
{
    private readonly IRuntimeNativeValidationProfileValidator _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));

    public ImmutableArray<string> Build(RuntimeNativeValidationProfile profile, string target, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _profiles.Validate(profile, target);
        if (!Path.IsPathFullyQualified(path))
            throw new InvalidOperationException("The static-native Wasm validation profile is unsupported.");
        return ["validate", path, "--features=" + string.Join(',', profile.Features)];
    }
}
