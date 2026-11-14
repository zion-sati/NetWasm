using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class RefLikeGenericConstraintCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, false)]
    [InlineData(WasmTarget.Wasm32, true)]
    [InlineData(WasmTarget.Wasm64, false)]
    [InlineData(WasmTarget.Wasm64, true)]
    public void RequiresPermissionForRefLikeMethodArguments(WasmTarget target, bool allowsRefStruct)
    {
        using var assets = TestAssets.Create();
        var contract = assets.CompileSource("RefLike.Contract",
            "namespace Shared; public static class Api { public static int Read<T>() where T : allows ref struct => 42; }");
        var caller = assets.CompileSource("RefLike.Caller", """
            public ref struct Argument { }
            public static class Caller { public static int Read() => Shared.Api.Read<Argument>(); }
            """, contract);
        var app = assets.CompileSource("RefLike.Application",
            "public static class Entry { public static int Run(int input) => Caller.Read(); }", caller);
        var constraint = allowsRefStruct ? "where T : allows ref struct" : "";
        var implementation = assets.CompileSource("RefLike.Implementation",
            "namespace Shared; public static class Api { public static int Read<T>() " + constraint + " => 42; }");
        assets.CompileSource("RefLike.Contract",
            "[assembly: System.Runtime.CompilerServices.TypeForwardedTo(typeof(Shared.Api))]", implementation);
        var options = new CompilerOptions(app, [assets.CoreLib, caller, implementation], "Entry", "Run", [], target,
            Optimization: FinalWasmOptimization.None, MetadataReferencePaths: [contract]);
        if (allowsRefStruct)
            Assert.NotEmpty(NetWasmCompiler.Compile(options).ApplicationModule);
        else
            Assert.Equal(DiagnosticCode.AssemblyResolution,
                Assert.Throws<CompilerException>(() => NetWasmCompiler.Compile(options)).Diagnostic.Code);
    }
}
