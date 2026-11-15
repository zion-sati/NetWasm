// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

// Compatibility coverage for the dotnet/runtime WeakGCHandle<T> contract.
// The upstream implementation is licensed under the MIT license.

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class WeakGCHandleCompatibilityCompilationTests
{
    [Fact]
    public void CompilerExecutesWeakGCHandleContract()
    {
        using var assets = TestAssets.Create();
        var coreLib = assets.CompileCoreLibVariant("WeakGCHandleCompatibilityCoreLib", string.Empty);
        var assembly = assets.CompileSourceAgainstCoreLib(
            "WeakGCHandleCompatibilityFixture",
            Source,
            coreLib);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [coreLib],
            "WeakGCHandleCompatibilityFixture.EntryPoint",
            "Run",
            [],
            WasmTarget.Wasm32));

        Assert.Equal(0, ExecuteWithNode(compilation.ApplicationModule, assets.Directory, 0));
    }

    private const string Source = """
        using System;
        using System.Runtime.InteropServices;

        namespace WeakGCHandleCompatibilityFixture;

        public sealed class Payload(int value)
        {
            public int Value { get; } = value;
        }

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var first = new Payload(7);
                var handle = new WeakGCHandle<Payload>(first);
                if (!handle.IsAllocated ||
                    !handle.TryGetTarget(out var firstTarget) ||
                    !ReferenceEquals(first, firstTarget))
                {
                    return 1;
                }

                var alias = WeakGCHandle<Payload>.FromIntPtr(
                    WeakGCHandle<Payload>.ToIntPtr(handle));
                if (!alias.IsAllocated ||
                    !alias.Equals(handle) ||
                    alias.GetHashCode() != handle.GetHashCode())
                {
                    return 2;
                }

                var second = new Payload(11);
                handle.SetTarget(second);
                if (!handle.TryGetTarget(out var secondTarget) ||
                    !ReferenceEquals(second, secondTarget))
                {
                    return 3;
                }

                var tracked = new WeakGCHandle<Payload>(second, true);
                if (!tracked.IsAllocated ||
                    !tracked.TryGetTarget(out var trackedTarget) ||
                    !ReferenceEquals(second, trackedTarget))
                {
                    return 4;
                }

                var empty = new WeakGCHandle<Payload>(null);
                if (!empty.IsAllocated || empty.TryGetTarget(out _))
                {
                    return 5;
                }

                empty.SetTarget(first);
                if (!empty.TryGetTarget(out var finalTarget) ||
                    !ReferenceEquals(first, finalTarget))
                {
                    return 6;
                }

                handle.Dispose();
                tracked.Dispose();
                empty.Dispose();
                handle.Dispose();
                return handle.IsAllocated ? 7 : input;
            }
        }
        """;
}
