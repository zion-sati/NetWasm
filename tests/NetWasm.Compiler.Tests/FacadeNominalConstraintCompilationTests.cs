using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class FacadeNominalConstraintCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, "interface", false)]
    [InlineData(WasmTarget.Wasm32, "interface", true)]
    [InlineData(WasmTarget.Wasm32, "base", false)]
    [InlineData(WasmTarget.Wasm32, "base", true)]
    [InlineData(WasmTarget.Wasm64, "interface", false)]
    [InlineData(WasmTarget.Wasm64, "interface", true)]
    [InlineData(WasmTarget.Wasm64, "base", false)]
    [InlineData(WasmTarget.Wasm64, "base", true)]
    [InlineData(WasmTarget.Wasm32, "variance", false)]
    [InlineData(WasmTarget.Wasm64, "variance", false)]
    [InlineData(WasmTarget.Wasm32, "variance", true)]
    [InlineData(WasmTarget.Wasm64, "variance", true)]
    public void FacadeMethodArgumentsMustSatisfyBaseAndInterfaceConstraints(
        WasmTarget target, string kind, bool incompatible)
    {
        using var assets = TestAssets.Create();
        var contract = assets.CompileSource("Nominal.Contract",
            "namespace Shared; public static class Api { public static int Read<T>() => 42; }");
        var argument = kind == "variance" ? (incompatible ? "int[]" : "string[]")
            : incompatible ? "string" : kind == "interface" ? "Disposable" : "System.ArgumentException";
        var caller = assets.CompileSource("Nominal.Caller", $$"""
            public sealed class Disposable : System.IDisposable { public void Dispose() {} }
            public static class Caller { public static int Read() => Shared.Api.Read<{{argument}}>(); }
            """, contract);
        var app = assets.CompileSource("Nominal.Application",
            "public static class Entry { public static int Run(int input) => Caller.Read(); }", caller);
        var constraint = kind == "variance" ? "System.Collections.Generic.IEnumerable<object>"
            : kind == "interface" ? "System.IDisposable" : "System.Exception";
        var implementation = assets.CompileSource("Nominal.Implementation",
            "namespace Shared; public static class Api { public static int Read<T>() where T : " + constraint + " => 42; }");
        assets.CompileSource("Nominal.Contract",
            "[assembly: System.Runtime.CompilerServices.TypeForwardedTo(typeof(Shared.Api))]", implementation);
        var options = new CompilerOptions(app, [assets.CoreLib, caller, implementation], "Entry", "Run", [], target,
            Optimization: FinalWasmOptimization.None, MetadataReferencePaths: [contract]);
        if (incompatible)
            Assert.Equal(DiagnosticCode.AssemblyResolution,
                Assert.Throws<CompilerException>(() => NetWasmCompiler.Compile(options)).Diagnostic.Code);
        else
            Assert.NotEmpty(NetWasmCompiler.Compile(options).ApplicationModule);
    }
}
