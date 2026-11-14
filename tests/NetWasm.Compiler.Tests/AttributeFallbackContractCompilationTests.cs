using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class AttributeFallbackContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void UnfilteredQueriesUseCatchableManagedFallbacks(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("AttributeFallbackFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "AttributeFallbackFixture.EntryPoint",
            "Run", [new("run", "AttributeFallbackFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    private const string Source = """
        using System;
        using System.Reflection;
        namespace AttributeFallbackFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var caught = 0;
                try { typeof(Target).GetCustomAttributes(true); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { CustomAttributeExtensions.GetCustomAttributes(typeof(Target)); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { CustomAttributeExtensions.GetCustomAttributes(typeof(Target), false); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { ((ICustomAttributeProvider)typeof(Target)).GetCustomAttributes(false); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                return caught == 4 ? input + 1 : 1;
            }
            private sealed class Target { }
        }
        """;
}
