using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class TypeConstructionContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void TypeConstructionReturnsTheProgramsTypes(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileUnsafeSource("TypeConstructionFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "TypeConstructionFixture.EntryPoint",
            "Run", [new("run", "TypeConstructionFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    private const string Source = """
        using System;
        namespace TypeConstructionFixture;
        public static class EntryPoint
        {
            public static unsafe int Run(int input)
            {
                // The type table returns the types the program uses; one it does not use cannot be made at run time.
                if (typeof(int).MakeArrayType() != typeof(int[]) || typeof(int).MakeArrayType(2) != typeof(int[,]) ||
                    typeof(int).MakePointerType() != typeof(int*) || typeof(Pair<>).MakeGenericType(typeof(int)) != typeof(Pair<int>)) return 3;
                var caught = 0;
                try { typeof(long).MakeByRefType(); } catch (PlatformNotSupportedException e) { if (e.Message.Length > 0) caught++; }
                try { Type.MakeGenericMethodParameter(0); } catch (PlatformNotSupportedException) { caught++; }
                try { Type.MakeGenericMethodParameter(-1); } catch (ArgumentOutOfRangeException e) { if (e.ParamName == "position") caught++; }
                if (!typeof(int[]).IsArray || !typeof(int[,]).IsArray || !typeof(int*).IsPointer || !typeof(Pair<int>).IsConstructedGenericType) return 1;
                return caught == 3 ? input + 1 : 2;
            }
            private class Pair<T> { }
        }
        """;
}
