using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class OriginalContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, false)]
    [InlineData(WasmTarget.Wasm64, false)]
    [InlineData(WasmTarget.Wasm32, true)]
    [InlineData(WasmTarget.Wasm64, true)]
    public void UnchangedOriginalImagesPreservePrivateGenericHelpers(WasmTarget target, bool field)
    {
        using var assets = TestAssets.Create();
        var members = field
            ? "private static int value = 42; public static int Read() => value;"
            : "private static int Hidden() => 42; public static int Read() => Hidden();";
        var library = assets.CompileSource("Private.Library",
            "public static class Box<T> { " + members + " }");
        var app = assets.CompileSource("Private.Application",
            "public static class Entry { public static int Run(int input) => Box<int>.Read(); }", library);
        var options = new CompilerOptions(app, [assets.CoreLib, library], "Entry", "Run", [], target,
            Optimization: FinalWasmOptimization.None)
        {
            OriginalContractReferencePaths = [library],
        };
        Assert.NotEmpty(NetWasmCompiler.Compile(options).ApplicationModule);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, "method", false, false)]
    [InlineData(WasmTarget.Wasm32, "method", false, true)]
    [InlineData(WasmTarget.Wasm32, "method", true, false)]
    [InlineData(WasmTarget.Wasm32, "method", true, true)]
    [InlineData(WasmTarget.Wasm32, "field", false, false)]
    [InlineData(WasmTarget.Wasm32, "field", false, true)]
    [InlineData(WasmTarget.Wasm32, "field", true, false)]
    [InlineData(WasmTarget.Wasm32, "field", true, true)]
    [InlineData(WasmTarget.Wasm32, "constraint", false, false)]
    [InlineData(WasmTarget.Wasm32, "constraint", false, true)]
    [InlineData(WasmTarget.Wasm32, "constraint", true, false)]
    [InlineData(WasmTarget.Wasm32, "constraint", true, true)]
    [InlineData(WasmTarget.Wasm64, "method", false, false)]
    [InlineData(WasmTarget.Wasm64, "method", false, true)]
    [InlineData(WasmTarget.Wasm64, "method", true, false)]
    [InlineData(WasmTarget.Wasm64, "method", true, true)]
    [InlineData(WasmTarget.Wasm64, "field", false, false)]
    [InlineData(WasmTarget.Wasm64, "field", false, true)]
    [InlineData(WasmTarget.Wasm64, "field", true, false)]
    [InlineData(WasmTarget.Wasm64, "field", true, true)]
    [InlineData(WasmTarget.Wasm64, "constraint", false, false)]
    [InlineData(WasmTarget.Wasm64, "constraint", false, true)]
    [InlineData(WasmTarget.Wasm64, "constraint", true, false)]
    [InlineData(WasmTarget.Wasm64, "constraint", true, true)]
    public void OriginalDeclarationsValidateTheSelectedImplementation(
        WasmTarget target, string kind, bool incompatible, bool sameIdentity)
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
        var contract = assets.CompileSource("Contract.Library",
            "namespace Shared; public static class Api { " + originalMember + " }");
        var original = Path.Combine(Path.GetDirectoryName(contract)!, "original-contract.dll");
        File.Copy(contract, original);
        var call = kind switch
        {
            "field" => "Shared.Api.Value",
            "constraint" => "Shared.Api.Read<string>()",
            _ => "Shared.Api.Read()",
        };
        var caller = assets.CompileSource("Contract.Caller",
            "public static class Caller { public static int Read() => " + call + "; }", contract);
        var app = assets.CompileSource("Contract.Application",
            "public static class Entry { public static int Run(int input) => Caller.Read(); }", caller);
        var implementation = assets.CompileSource(sameIdentity ? "Contract.Library" : "Contract.Implementation",
            "namespace Shared; public static class Api { " + targetMember + " }");
        if (!sameIdentity)
            assets.CompileSource("Contract.Library",
                "[assembly: System.Runtime.CompilerServices.TypeForwardedTo(typeof(Shared.Api))]", implementation);
        var options = new CompilerOptions(app, [assets.CoreLib, caller, implementation], "Entry", "Run", [], target,
            Optimization: FinalWasmOptimization.None, MetadataReferencePaths: sameIdentity ? [] : [contract])
        {
            OriginalContractReferencePaths = [original],
        };

        if (incompatible)
            Assert.Equal(DiagnosticCode.AssemblyResolution,
                Assert.Throws<CompilerException>(() => NetWasmCompiler.Compile(options)).Diagnostic.Code);
        else
            Assert.NotEmpty(NetWasmCompiler.Compile(options).ApplicationModule);
    }
}
