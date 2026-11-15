// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

// Compatibility coverage for the dotnet/runtime PinnedGCHandle<T> contract.
// The upstream implementation is licensed under the MIT license.

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class PinnedGCHandleCompatibilityCompilationTests
{
    [Fact]
    public void CompilerExecutesPinnedGCHandleContract()
    {
        using var assets = TestAssets.Create();
        var coreLib = assets.CompileCoreLibVariant(
            "PinnedGCHandleCompatibilityCoreLib",
            ProbeSource);
        var assembly = assets.CompileSourceAgainstCoreLib(
            "PinnedGCHandleCompatibilityFixture",
            Source,
            coreLib);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [coreLib],
            "PinnedGCHandleCompatibilityFixture.EntryPoint",
            "Run",
            [],
            WasmTarget.Wasm32));

        Assert.Equal(0, ExecuteWithNode(compilation.ApplicationModule, assets.Directory, 0));
    }

    private const string ProbeSource = """
        using System.Runtime.InteropServices;

        namespace PinnedGCHandleCompatibilityFixture;

        public static unsafe class PinnedGCHandleProbe
        {
            public static int ObjectAddress<T>(PinnedGCHandle<T> handle)
                where T : class? => handle.GetAddressOfObjectData() is null ? 0 : 1;

            public static int ArrayAddress<T>(PinnedGCHandle<T[]> handle) =>
                handle.GetAddressOfArrayData() is null ? 0 : 1;

            public static int StringAddress(PinnedGCHandle<string> handle) =>
                handle.GetAddressOfStringData() is null ? 0 : 1;
        }
        """;

    private const string Source = """
        using System;
        using System.Runtime.InteropServices;

        namespace PinnedGCHandleCompatibilityFixture;

        public sealed class Payload(int value)
        {
            public int Value { get; } = value;
        }

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var first = new Payload(7);
                var handle = new PinnedGCHandle<Payload>(first);
                if (!handle.IsAllocated ||
                    !ReferenceEquals(first, handle.Target) ||
                    PinnedGCHandleProbe.ObjectAddress(handle) == 0)
                {
                    return 1;
                }

                var pointer = PinnedGCHandle<Payload>.ToIntPtr(handle);
                var roundTrip = PinnedGCHandle<Payload>.FromIntPtr(pointer);
                if (!roundTrip.IsAllocated ||
                    !roundTrip.Equals(handle) ||
                    !handle.Equals((object)roundTrip) ||
                    handle.GetHashCode() != pointer.GetHashCode() ||
                    PinnedGCHandle<Payload>.ToIntPtr(roundTrip) != pointer)
                {
                    return 2;
                }

                var second = new Payload(11);
                handle.Target = second;
                if (!ReferenceEquals(second, roundTrip.Target))
                {
                    return 3;
                }

                var array = new PinnedGCHandle<int[]>(new[] { 3, 5 });
                if (PinnedGCHandleProbe.ArrayAddress(array) == 0)
                {
                    return 4;
                }

                var text = new PinnedGCHandle<string>("pinned");
                if (PinnedGCHandleProbe.StringAddress(text) == 0)
                {
                    return 5;
                }

                var empty = new PinnedGCHandle<Payload>(null);
                if (!empty.IsAllocated ||
                    empty.Target is not null ||
                    PinnedGCHandleProbe.ObjectAddress(empty) != 0)
                {
                    return 6;
                }

                handle.Dispose();
                if (handle.IsAllocated)
                {
                    return 7;
                }

                try
                {
                    _ = handle.Target;
                    return 8;
                }
                catch (NullReferenceException)
                {
                }

                array.Dispose();
                text.Dispose();
                empty.Dispose();
                handle.Dispose();
                return input;
            }
        }
        """;
}
