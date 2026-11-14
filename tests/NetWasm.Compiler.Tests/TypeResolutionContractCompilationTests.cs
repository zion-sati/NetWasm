using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class TypeResolutionContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void NameResolutionRejectsWithCatchablePlatformExceptions(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("TypeResolutionFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "TypeResolutionFixture.EntryPoint",
            "Run", [new("run", "TypeResolutionFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    private const string Source = """
        using System;
        namespace TypeResolutionFixture;
        public static class EntryPoint
        {
            private static int callbacks;
            public static int Run(int input)
            {
                Func<System.Reflection.AssemblyName, System.Reflection.Assembly> assembly = name => { callbacks++; return null; };
                Func<System.Reflection.Assembly, string, bool, Type> type = (owner, name, ignore) => { callbacks++; return typeof(int); };
                var caught = 0;
                try { Type.GetType("System.Int32"); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { Type.GetType("missing", false); } catch (PlatformNotSupportedException) { caught++; }
                try { Type.GetType("missing", true, true); } catch (PlatformNotSupportedException) { caught++; }
                try { Type.GetType("name", assembly, type); } catch (PlatformNotSupportedException) { caught++; }
                try { Type.GetType("name", assembly, type, true); } catch (PlatformNotSupportedException) { caught++; }
                try { Type.GetType("name", assembly, type, true, true); } catch (PlatformNotSupportedException) { caught++; }
                try { Type.ReflectionOnlyGetType("name", true, true); } catch (PlatformNotSupportedException) { caught++; }
                try { Type.GetType(null); } catch (ArgumentNullException e) { if (e.ParamName == "typeName") caught++; }
                try { Type.GetType(null, false); } catch (ArgumentNullException) { caught++; }
                try { Type.GetType(null, false, false); } catch (ArgumentNullException) { caught++; }
                try { Type.GetType(null, assembly, type); } catch (ArgumentNullException) { caught++; }
                try { Type.GetType(null, assembly, type, false); } catch (ArgumentNullException) { caught++; }
                try { Type.GetType(null, assembly, type, false, false); } catch (ArgumentNullException) { caught++; }
                return caught == 13 && callbacks == 0 && 42.GetType() == typeof(int) ? input + 1 : 1;
            }
        }
        """;
}
