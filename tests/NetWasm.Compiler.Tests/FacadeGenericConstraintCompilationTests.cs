using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class FacadeGenericConstraintCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, "struct", "int", true, false)]
    [InlineData(WasmTarget.Wasm32, "struct", "int?", false, false)]
    [InlineData(WasmTarget.Wasm32, "struct", "string", false, true)]
    [InlineData(WasmTarget.Wasm32, "struct", "int", true, true)]
    [InlineData(WasmTarget.Wasm32, "class", "string", true, false)]
    [InlineData(WasmTarget.Wasm32, "class", "int", false, false)]
    [InlineData(WasmTarget.Wasm32, "class", "int[]", true, false)]
    [InlineData(WasmTarget.Wasm32, "new()", "Good", true, false)]
    [InlineData(WasmTarget.Wasm32, "new()", "Hidden", false, false)]
    [InlineData(WasmTarget.Wasm32, "new()", "Abstract", false, false)]
    [InlineData(WasmTarget.Wasm32, "new()", "string", false, false)]
    [InlineData(WasmTarget.Wasm32, "new()", "int", true, false)]
    [InlineData(WasmTarget.Wasm32, "new()", "int?", true, false)]
    [InlineData(WasmTarget.Wasm32, "new()", "int[]", false, false)]
    [InlineData(WasmTarget.Wasm64, "struct", "int", true, false)]
    [InlineData(WasmTarget.Wasm64, "struct", "int?", false, false)]
    [InlineData(WasmTarget.Wasm64, "struct", "string", false, true)]
    [InlineData(WasmTarget.Wasm64, "struct", "int", true, true)]
    [InlineData(WasmTarget.Wasm64, "class", "string", true, false)]
    [InlineData(WasmTarget.Wasm64, "class", "int", false, false)]
    [InlineData(WasmTarget.Wasm64, "class", "int[]", true, false)]
    [InlineData(WasmTarget.Wasm64, "new()", "Good", true, false)]
    [InlineData(WasmTarget.Wasm64, "new()", "Hidden", false, false)]
    [InlineData(WasmTarget.Wasm64, "new()", "Abstract", false, false)]
    [InlineData(WasmTarget.Wasm64, "new()", "string", false, false)]
    [InlineData(WasmTarget.Wasm64, "new()", "int", true, false)]
    [InlineData(WasmTarget.Wasm64, "new()", "int?", true, false)]
    [InlineData(WasmTarget.Wasm64, "new()", "int[]", false, false)]
    public void ChecksTheActualArgumentAfterFacadeBinding(
        WasmTarget target, string constraint, string argument, bool accepted, bool nested)
    {
        using var assets = TestAssets.Create();
        var contract = assets.CompileSource("Generic.Contract",
            "namespace Shared; public static class Api { public static int Read<T>() => 42; }");
        var call = nested ? $"Wrap<{argument}>()" : $"Shared.Api.Read<{argument}>()";
        var caller = assets.CompileSource("Generic.Caller", $$"""
            public sealed class Good { public Good() {} }
            public sealed class Hidden { private Hidden() {} }
            public abstract class Abstract { public Abstract() {} }
            public static class Caller
            {
                private static int Wrap<T>() => Shared.Api.Read<T>();
                public static int Read() => {{call}};
            }
            """, contract);
        var app = assets.CompileSource("Generic.Application",
            "public static class Entry { public static int Run(int input) => Caller.Read(); }", caller);
        var implementation = assets.CompileSource("Generic.Implementation",
            "namespace Shared; public static class Api { public static int Read<T>() where T : " + constraint + " => 42; }");
        assets.CompileSource("Generic.Contract",
            "[assembly: System.Runtime.CompilerServices.TypeForwardedTo(typeof(Shared.Api))]", implementation);
        var options = new CompilerOptions(app, [assets.CoreLib, caller, implementation], "Entry", "Run", [], target,
            Optimization: FinalWasmOptimization.None, MetadataReferencePaths: [contract]);
        if (accepted)
            Assert.NotEmpty(NetWasmCompiler.Compile(options).ApplicationModule);
        else
            Assert.Equal(DiagnosticCode.AssemblyResolution,
                Assert.Throws<CompilerException>(() => NetWasmCompiler.Compile(options)).Diagnostic.Code);
    }
}
