using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class TypePlatformContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void UnavailablePlatformTypesRejectAtRuntime(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("TypePlatformFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "TypePlatformFixture.EntryPoint",
            "Run", [new("run", "TypePlatformFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    private const string Source = """
        using System;
        namespace TypePlatformFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                Type[] types = { typeof(int), typeof(string), typeof(object), typeof(E), typeof(int[]), typeof(Pair<>), typeof(Pair<int>) };
                foreach (var type in types)
                    if (type.IsCOMObject || type.IsContextful) return 1;
                var id = Guid.Empty;
                var caught = 0;
                try { Type.GetTypeFromCLSID(id); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { Type.GetTypeFromCLSID(id, false); } catch (PlatformNotSupportedException) { caught++; }
                try { Type.GetTypeFromCLSID(id, "server"); } catch (PlatformNotSupportedException) { caught++; }
                try { Type.GetTypeFromCLSID(id, null, true); } catch (PlatformNotSupportedException) { caught++; }
                try { Type.GetTypeFromProgID("name"); } catch (PlatformNotSupportedException) { caught++; }
                try { Type.GetTypeFromProgID("name", false); } catch (PlatformNotSupportedException) { caught++; }
                try { Type.GetTypeFromProgID("name", "server"); } catch (PlatformNotSupportedException) { caught++; }
                try { Type.GetTypeFromProgID("name", null, true); } catch (PlatformNotSupportedException) { caught++; }
                return caught == 8 ? input + 1 : 2;
            }
            private enum E { One }
            private class Pair<T> { }
        }
        """;
}
