using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// A field address taken through an unmanaged pointer (bs->rep[i], as SharpCompress's ZStandard port writes) stays on the
// stack across a static field load, whose type initialization is a collection safepoint. It points into native memory
// or a pinned object and needs no root; the compiler rejected it as an address of unknown storage (NW1002).
public sealed class UnmanagedPointerFieldSafepointCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void FieldAddressesThroughPointersCrossInitializationSafepoints(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileUnsafeSource("PointerFieldSafepointFixture", Source, []);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "PointerFieldSafepointFixture.EntryPoint",
            "Run", [new("run", "PointerFieldSafepointFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));

        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    private const string Source = """
        using System.Runtime.InteropServices;

        namespace PointerFieldSafepointFixture;

        public unsafe struct State
        {
            public fixed uint Rep[3];
            public int Mode;
        }

        public static unsafe class Tables
        {
            public static readonly uint* Start = Create();

            private static uint* Create()
            {
                var values = (uint*)Marshal.AllocHGlobal(3 * sizeof(uint));
                values[0] = 1;
                values[1] = 4;
                values[2] = 8;
                return values;
            }
        }

        public static unsafe class EntryPoint
        {
            private static void Reset(State* state)
            {
                for (var index = 0; index < 3; index++)
                {
                    state->Rep[index] = Tables.Start[index];
                }
                state->Mode = 0;
            }

            public static int Run(int input)
            {
                var state = new State { Mode = 5 };
                Reset(&state);
                var buffer = (State*)Marshal.AllocHGlobal(sizeof(State));
                Reset(buffer);
                var total = state.Rep[0] + state.Rep[1] + state.Rep[2] + (uint)state.Mode +
                    buffer->Rep[0] + buffer->Rep[2];
                Marshal.FreeHGlobal((System.IntPtr)buffer);
                return total == 22 ? input + 1 : 1;
            }
        }
        """;
}
