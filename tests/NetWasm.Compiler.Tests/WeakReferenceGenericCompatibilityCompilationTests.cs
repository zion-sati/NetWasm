// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

// Compatibility coverage adapted from
// dotnet/runtime src/libraries/System.Runtime/tests/System.Runtime.Tests/System/WeakReferenceTests.cs.
// The upstream test is licensed under the MIT license.

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class WeakReferenceGenericCompatibilityCompilationTests
{
    [Fact]
    public void CompilerReportsWeakHandleRuntimeFeature()
    {
        using var assets = TestAssets.Create();
        var coreLib = assets.CompileCoreLibVariant("WeakReferenceFeatureCoreLib", string.Empty);
        var assembly = assets.CompileSourceAgainstCoreLib(
            "WeakReferenceFeatureFixture",
            Source,
            coreLib);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [coreLib],
            "WeakReferenceGenericCompatibilityFixture.EntryPoint",
            "Run",
            [],
            WasmTarget.Wasm32));

        Assert.Contains(NetWasmRuntimeFeatureIds.WeakHandles, compilation.RuntimeFeatures);
    }

    [Fact]
    public void CompilerExecutesGenericWeakReferenceContract()
    {
        using var assets = TestAssets.Create();
        var coreLib = assets.CompileCoreLibVariant("WeakReferenceGenericCompatibilityCoreLib", string.Empty);
        var assembly = assets.CompileSourceAgainstCoreLib(
            "WeakReferenceGenericCompatibilityFixture",
            Source,
            coreLib);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [coreLib],
            "WeakReferenceGenericCompatibilityFixture.EntryPoint",
            "Run",
            [],
            WasmTarget.Wasm32));

        Assert.Contains(NetWasmRuntimeFeatureIds.WeakHandles, compilation.RuntimeFeatures);
        Assert.Equal(0, ExecuteWithNode(compilation.ApplicationModule, assets.Directory, 0));
    }

    private const string Source = """
        using System;

        namespace WeakReferenceGenericCompatibilityFixture;

        public sealed class Payload(int value)
        {
            public int Value { get; } = value;
        }

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var first = new Payload(7);
                var weak = new WeakReference<Payload>(first);
                if (!weak.TryGetTarget(out var firstTarget) ||
                    !ReferenceEquals(first, firstTarget))
                {
                    return 1;
                }

                var second = new Payload(11);
                weak.SetTarget(second);
                if (!weak.TryGetTarget(out var secondTarget) ||
                    !ReferenceEquals(second, secondTarget))
                {
                    return 2;
                }

                var tracked = new WeakReference<Payload>(second, true);
                if (!tracked.TryGetTarget(out var trackedTarget) ||
                    !ReferenceEquals(second, trackedTarget))
                {
                    return 3;
                }

                var empty = new WeakReference<Payload>(null);
                if (empty.TryGetTarget(out _))
                {
                    return 4;
                }

                empty.SetTarget(first);
                return empty.TryGetTarget(out var finalTarget) &&
                    ReferenceEquals(first, finalTarget) ? input : 5;
            }
        }
        """;
}
