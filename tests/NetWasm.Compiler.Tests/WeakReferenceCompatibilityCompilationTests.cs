// Compatibility coverage adapted from
// dotnet/runtime src/libraries/System.Runtime/tests/System.Runtime.Tests/System/WeakReferenceTests.cs.
// The upstream test is licensed under the MIT license.

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class WeakReferenceCompatibilityCompilationTests
{
    [Fact]
    public void CompilerExecutesNonGenericWeakReferenceContract()
    {
        using var assets = TestAssets.Create();
        var coreLib = assets.CompileCoreLibVariant("WeakReferenceCompatibilityCoreLib", string.Empty);
        var assembly = assets.CompileSourceAgainstCoreLib(
            "WeakReferenceCompatibilityFixture",
            Source,
            coreLib);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [coreLib],
            "WeakReferenceCompatibilityFixture.EntryPoint",
            "Run",
            [],
            WasmTarget.Wasm32));

        Assert.Equal(0, ExecuteWithNode(compilation.ApplicationModule, assets.Directory, 0));
    }

    private const string Source = """
        using System;

        namespace WeakReferenceCompatibilityFixture;

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var first = new object();
                var weak = new WeakReference(first);
                if (!weak.IsAlive || !ReferenceEquals(first, weak.Target) || weak.TrackResurrection)
                {
                    return 1;
                }

                var second = new object();
                weak.Target = second;
                if (!weak.IsAlive || !ReferenceEquals(second, weak.Target))
                {
                    return 2;
                }

                var tracked = new WeakReference(second, true);
                if (!tracked.IsAlive || !ReferenceEquals(second, tracked.Target) || !tracked.TrackResurrection)
                {
                    return 3;
                }

                var empty = new WeakReference(null);
                if (empty.IsAlive || empty.Target is not null || empty.TrackResurrection)
                {
                    return 4;
                }

                empty.Target = first;
                return empty.IsAlive && ReferenceEquals(first, empty.Target) ? input : 5;
            }
        }
        """;
}
