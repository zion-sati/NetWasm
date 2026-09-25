// Compatibility coverage for dotnet/runtime MemoryHandle and
// CriticalFinalizerObject contracts. The upstream implementations are licensed
// under the MIT license.

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class MemoryHandleCriticalFinalizerCompatibilityCompilationTests
{
    [Fact]
    public void CompilerExecutesMemoryHandleAndCriticalFinalizerObjectContracts()
    {
        using var assets = TestAssets.Create();
        var coreLib = assets.CompileCoreLibVariant(
            "MemoryHandleCriticalFinalizerCompatibilityCoreLib",
            ProbeSource);
        var assembly = assets.CompileSourceAgainstCoreLib(
            "MemoryHandleCriticalFinalizerCompatibilityFixture",
            Source,
            coreLib);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [coreLib],
            "MemoryHandleCriticalFinalizerCompatibilityFixture.EntryPoint",
            "Run",
            [],
            WasmTarget.Wasm32));

        Assert.Equal(0, ExecuteWithNode(compilation.ApplicationModule, assets.Directory, 0));
    }

    private const string ProbeSource = """
        using System;
        using System.Buffers;
        using System.Runtime.ConstrainedExecution;
        using System.Runtime.InteropServices;

        namespace MemoryHandleCriticalFinalizerCompatibilityFixture;

        public sealed class FinalizableProbe : CriticalFinalizerObject
        {
        }

        public sealed class PinnableProbe : IPinnable
        {
            public int UnpinCount { get; private set; }

            public MemoryHandle Pin(int elementIndex) => default;

            public void Unpin() => UnpinCount++;
        }

        public static unsafe class MemoryHandleProbe
        {
            public static int CreateAndDispose()
            {
                var pinnable = new PinnableProbe();
                var handle = new MemoryHandle(null, default(GCHandle), pinnable);
                if (handle.Pointer != null)
                {
                    return 1;
                }

                handle.Dispose();
                return pinnable.UnpinCount == 1 ? 0 : 2;
            }

            public static int CreateFinalizable() =>
                new FinalizableProbe() is CriticalFinalizerObject ? 0 : 3;
        }
        """;

    private const string Source = """
        using MemoryHandleProbe = MemoryHandleCriticalFinalizerCompatibilityFixture.MemoryHandleProbe;

        namespace MemoryHandleCriticalFinalizerCompatibilityFixture;

        public static class EntryPoint
        {
            public static int Run(int input) =>
                MemoryHandleProbe.CreateAndDispose() +
                MemoryHandleProbe.CreateFinalizable() + input;
        }
        """;
}
