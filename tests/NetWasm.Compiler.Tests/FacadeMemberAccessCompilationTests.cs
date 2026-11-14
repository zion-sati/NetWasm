using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class FacadeMemberAccessCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, "method", false)]
    [InlineData(WasmTarget.Wasm64, "method", false)]
    [InlineData(WasmTarget.Wasm32, "field", false)]
    [InlineData(WasmTarget.Wasm64, "field", false)]
    [InlineData(WasmTarget.Wasm32, "constraint", false)]
    [InlineData(WasmTarget.Wasm64, "constraint", false)]
    [InlineData(WasmTarget.Wasm32, "method", true)]
    [InlineData(WasmTarget.Wasm64, "method", true)]
    [InlineData(WasmTarget.Wasm32, "field", true)]
    [InlineData(WasmTarget.Wasm64, "field", true)]
    [InlineData(WasmTarget.Wasm32, "constraint", true)]
    [InlineData(WasmTarget.Wasm64, "constraint", true)]
    public void RedirectedMemberMustPreserveItsOriginalContract(WasmTarget target, string kind, bool incompatible)
    {
        using var assets = TestAssets.Create();
        var originalMember = kind switch
        {
            "field" => "public static int Value = 42;",
            "constraint" => "public static int Read<T>() => 42;",
            _ => "public static int Read() => 42;",
        };
        var targetMember = !incompatible ? originalMember : kind switch
        {
            "field" => "private static int Value = 42; public static int Anchor() => Value;",
            "constraint" => "public static int Read<T>() where T : struct => 42;",
            _ => "private static int Read() => 42; public static int Anchor() => Read();",
        };
        var contract = assets.CompileSource("Probe.Contract", "namespace Shared; public static class Api { " + originalMember + " }");
        var call = kind switch
        {
            "field" => "Shared.Api.Value",
            "constraint" => "Shared.Api.Read<string>()",
            _ => "Shared.Api.Read()",
        };
        var caller = assets.CompileSource("Probe.Caller", "public static class Caller { public static int Read() => " + call + "; }", contract);
        var app = assets.CompileSource("Probe.Application", "public static class Entry { public static int Run(int input) => Caller.Read(); }", caller);
        var implementation = assets.CompileSource("Probe.Implementation", "namespace Shared; public static class Api { " + targetMember + " }");
        assets.CompileSource("Probe.Contract", "[assembly: System.Runtime.CompilerServices.TypeForwardedTo(typeof(Shared.Api))]", implementation);
        var options = new CompilerOptions(app, [assets.CoreLib, caller, implementation], "Entry", "Run", [], target,
            Optimization: FinalWasmOptimization.None, MetadataReferencePaths: [contract]);
        if (incompatible)
            Assert.Equal(DiagnosticCode.AssemblyResolution,
                Assert.Throws<CompilerException>(() => NetWasmCompiler.Compile(options)).Diagnostic.Code);
        else
            Assert.NotEmpty(NetWasmCompiler.Compile(options).ApplicationModule);
    }
}
