// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

// Compatibility coverage adapted from dotnet/runtime System.Runtime.Tests GC handle semantics.
// The upstream test is licensed under the MIT license.

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class GCHandleGenericCompatibilityCompilationTests
{
    [Fact]
    public void CompilerExecutesGenericGCHandleContract()
    {
        using var assets = TestAssets.Create();
        var coreLib = assets.CompileCoreLibVariant("GCHandleGenericCompatibilityCoreLib", string.Empty);
        var assembly = assets.CompileSourceAgainstCoreLib(
            "GCHandleGenericCompatibilityFixture",
            Source,
            coreLib);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [coreLib],
            "GCHandleGenericCompatibilityFixture.EntryPoint",
            "Run",
            [],
            WasmTarget.Wasm32));

        Assert.Equal(0, ExecuteWithNode(compilation.ApplicationModule, assets.Directory, 0));
    }

    private const string Source = """
        using System;
        using System.Runtime.InteropServices;

        namespace GCHandleGenericCompatibilityFixture;

        public sealed class Payload
        {
        }

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var first = new Payload();
                var handle = new GCHandle<Payload>(first);
                if (!handle.IsAllocated)
                {
                    return 1;
                }

                var firstTarget = handle.Target;
                if (firstTarget is null)
                {
                    return 2;
                }

                if (!ReferenceEquals(first, firstTarget))
                {
                    return 3;
                }

                var pointer = GCHandle<Payload>.ToIntPtr(handle);
                var roundTrip = GCHandle<Payload>.FromIntPtr(pointer);
                if (!roundTrip.IsAllocated ||
                    !roundTrip.Equals(handle) ||
                    !handle.Equals((object)roundTrip) ||
                    handle.GetHashCode() != pointer.GetHashCode() ||
                    GCHandle<Payload>.ToIntPtr(roundTrip) != pointer)
                {
                    return 4;
                }

                var second = new Payload();
                handle.Target = second;
                if (!ReferenceEquals(second, roundTrip.Target))
                {
                    return 5;
                }

                handle.Dispose();
                if (handle.IsAllocated)
                {
                    return 6;
                }

                try
                {
                    _ = handle.Target;
                    return 7;
                }
                catch (NullReferenceException)
                {
                }

                var empty = new GCHandle<Payload>(null);
                if (!empty.IsAllocated || empty.Target is not null)
                {
                    return 8;
                }

                empty.Dispose();
                return input;
            }
        }
        """;
}
