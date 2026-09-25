// Compatibility coverage for the dotnet/runtime GCHandle and GCHandleType contract.
// The upstream implementation is licensed under the MIT license.

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class GCHandleCompatibilityCompilationTests
{
    [Fact]
    public void CompilerExecutesGCHandleContract()
    {
        using var assets = TestAssets.Create();
        var coreLib = assets.CompileCoreLibVariant("GCHandleCompatibilityCoreLib", string.Empty);
        var assembly = assets.CompileSourceAgainstCoreLib(
            "GCHandleCompatibilityFixture",
            Source,
            coreLib);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [coreLib],
            "GCHandleCompatibilityFixture.EntryPoint",
            "Run",
            [],
            WasmTarget.Wasm32));

        Assert.Equal(0, ExecuteWithNode(compilation.ApplicationModule, assets.Directory, 0));
    }

    private const string Source = """
        using System;
        using System.Runtime.InteropServices;

        namespace GCHandleCompatibilityFixture;

        public sealed class Payload(int value)
        {
            public int Value { get; } = value;
        }

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var first = new Payload(7);
                var normal = GCHandle.Alloc(first);
                if (!normal.IsAllocated || !ReferenceEquals(first, normal.Target))
                {
                    return 1;
                }

                var pointer = GCHandle.ToIntPtr(normal);
                var roundTrip = GCHandle.FromIntPtr(pointer);
                if (!roundTrip.IsAllocated ||
                    roundTrip != normal ||
                    !normal.Equals((object)roundTrip) ||
                    normal.GetHashCode() != pointer.GetHashCode())
                {
                    return 2;
                }

                var second = new Payload(11);
                normal.Target = second;
                if (!ReferenceEquals(second, roundTrip.Target))
                {
                    return 3;
                }

                var weak = GCHandle.Alloc(second, GCHandleType.Weak);
                var tracked = GCHandle.Alloc(second, GCHandleType.WeakTrackResurrection);
                if (!ReferenceEquals(second, weak.Target) || !ReferenceEquals(second, tracked.Target))
                {
                    return 4;
                }

                var pinned = GCHandle.Alloc(second, GCHandleType.Pinned);
                if (!pinned.IsAllocated || pinned.AddrOfPinnedObject() == IntPtr.Zero)
                {
                    return 5;
                }

                try
                {
                    _ = normal.AddrOfPinnedObject();
                    return 6;
                }
                catch (InvalidOperationException)
                {
                }

                normal.Free();
                weak.Free();
                tracked.Free();
                pinned.Free();
                if (normal.IsAllocated || weak.IsAllocated || tracked.IsAllocated || pinned.IsAllocated)
                {
                    return 7;
                }

                try
                {
                    normal.Free();
                    return 8;
                }
                catch (InvalidOperationException)
                {
                }

                var empty = GCHandle.Alloc(null, GCHandleType.Normal);
                if (!empty.IsAllocated || empty.Target is not null)
                {
                    return 9;
                }

                empty.Free();
                return input;
            }
        }
        """;
}
