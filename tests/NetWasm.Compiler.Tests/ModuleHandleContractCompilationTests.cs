using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class ModuleHandleContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ModuleHandlesPreserveEmptyValueAndRejectResolution(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("ModuleHandleFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "ModuleHandleFixture.EntryPoint",
            "Run", [new("run", "ModuleHandleFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    private const string Source = """
        using System;
        using System.Reflection;
        namespace ModuleHandleFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                if (Portable(input) != 42) return 20;
                var handle = new EmptyModule().ModuleHandle;
                var types = new[] { typeof(int).TypeHandle };
                Action[] calls = {
                    () => handle.GetRuntimeFieldHandleFromMetadataToken(1),
                    () => handle.GetRuntimeMethodHandleFromMetadataToken(1),
                    () => handle.GetRuntimeTypeHandleFromMetadataToken(1),
                    () => { _ = handle.MDStreamVersion; },
                    () => handle.ResolveFieldHandle(1), () => handle.ResolveFieldHandle(1, types, types),
                    () => handle.ResolveMethodHandle(1), () => handle.ResolveMethodHandle(1, types, types),
                    () => handle.ResolveTypeHandle(1), () => handle.ResolveTypeHandle(1, types, types)
                };
                var rejected = 0;
                foreach (var call in calls) { try { call(); } catch (PlatformNotSupportedException e) { if (e.Message.Length == 0) return 21; rejected++; } }
                if (rejected != 10 || handle != ModuleHandle.EmptyHandle || !types[0].Equals(typeof(int).TypeHandle)) return 22;
                return input + 1;
            }
            public static int Portable(int input)
            {
                var empty = default(ModuleHandle);
                var handle = new EmptyModule().ModuleHandle;
                if (handle != ModuleHandle.EmptyHandle || !(handle == empty) || !handle.Equals(empty)) return 1;
                object boxed = handle;
                if (!empty.Equals(boxed) || empty.Equals(null) || empty.Equals(new object())) return 2;
                IEquatable<ModuleHandle> comparable = handle;
                if (!comparable.Equals(empty) || handle.GetHashCode() != 0) return 3;
                var copies = new ModuleHandle[2]; copies[1] = handle;
                if (copies[0] != copies[1]) return 4;
                return input + 1;
            }
            private sealed class EmptyModule : Module { }
        }
        """;
}
