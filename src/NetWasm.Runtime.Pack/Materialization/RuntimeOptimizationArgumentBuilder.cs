using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;

namespace NetWasm.Runtime.Pack.Materialization;

internal sealed class RuntimeOptimizationArgumentBuilder(IRuntimeOptimizationPolicyResolver policies) :
    IRuntimeOptimizationArgumentBuilder
{
    private readonly IRuntimeOptimizationPolicyResolver _policies = policies ?? throw new ArgumentNullException(nameof(policies));

    // Binaryen's flags for each feature of the static-native profile, which uses wasm-tools' names.
    private static readonly FrozenDictionary<string, string[]> FeatureFlags = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["mvp"] = [],
        ["mutable-global"] = ["--enable-mutable-globals"],
        ["saturating-float-to-int"] = ["--enable-nontrapping-float-to-int"],
        ["sign-extension"] = ["--enable-sign-ext"],
        ["reference-types"] = ["--enable-reference-types", "--enable-call-indirect-overlong"],
        ["multi-value"] = ["--enable-multivalue"],
        ["bulk-memory"] = ["--enable-bulk-memory", "--enable-bulk-memory-opt"],
        ["simd"] = ["--enable-simd"],
        ["memory64"] = ["--enable-memory64"],
    }.ToFrozenDictionary(StringComparer.Ordinal);

    public ImmutableArray<string> Build(RuntimeOptimizationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var policy = _policies.Resolve(request.Optimization);
        if (policy.BinaryenFlag is null)
        {
            return [];
        }
        // The runtime and any static native code may use the features of the pack's static-native profile.
        var features = request.Target.NativeValidation?.Features ?? default;
        if (features.IsDefaultOrEmpty || features[0] != "mvp" || features.Any(feature => !FeatureFlags.ContainsKey(feature)))
            throw new InvalidOperationException("Runtime optimization requires the pack's static-native profile with known features.");
        var outputPath = Path.GetFullPath(request.OutputPath);
        var arguments = ImmutableArray.CreateBuilder<string>();
        arguments.Add("--strip-target-features");
        arguments.Add("--post-emscripten");
        arguments.Add(policy.BinaryenFlag);
        // Match Emscripten's optimized non-stack-first link. Binaryen may
        // treat the first KiB as unreachable only when static data starts at
        // or above the boundary enforced by that pass.
        if (request.RuntimeGlobalBase >= 1024)
        {
            arguments.Add("--low-memory-unused");
        }
        arguments.Add("--zero-filled-memory");
        arguments.Add("--pass-arg=directize-initial-contents-immutable");
        arguments.Add("--no-stack-ir");
        arguments.Add("--strip-debug");
        arguments.Add("--strip-producers");
        arguments.Add(outputPath);
        arguments.Add("-o");
        arguments.Add(outputPath);
        arguments.Add("--mvp-features");
        arguments.AddRange(features.SelectMany(feature => FeatureFlags[feature]).Order(StringComparer.Ordinal));
        return arguments.ToImmutable();
    }
}
