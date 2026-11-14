using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class StructuralGenericConstraintCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, false, false)]
    [InlineData(WasmTarget.Wasm32, false, true)]
    [InlineData(WasmTarget.Wasm32, true, false)]
    [InlineData(WasmTarget.Wasm32, true, true)]
    [InlineData(WasmTarget.Wasm64, false, false)]
    [InlineData(WasmTarget.Wasm64, false, true)]
    [InlineData(WasmTarget.Wasm64, true, false)]
    [InlineData(WasmTarget.Wasm64, true, true)]
    public void ValidatesGenericDependenciesWithoutCallingTheirMembers(
        WasmTarget target, bool fieldOnly, bool incompatible)
    {
        using var assets = TestAssets.Create();
        var contract = assets.CompileSource("Structural.Contract",
            "namespace Shared; public class Box<T> { }");
        var argument = incompatible ? "string" : "System.ArgumentException";
        var callerSource = fieldOnly ? $$"""
            public sealed class Holder { public Shared.Box<{{argument}}> Value; }
            public static class Caller
            {
                public static int Read() { var holder = new Holder(); return holder.Value == null ? 42 : 0; }
            }
            """ : $$"""
            public sealed class Derived : Shared.Box<{{argument}}> { }
            public static class Caller
            {
                public static int Read() => Identity<Derived>();
                private static int Identity<T>() => 42;
            }
            """;
        var caller = assets.CompileSource("Structural.Caller", callerSource, contract);
        var app = assets.CompileSource("Structural.Application",
            "public static class Entry { public static int Run(int input) => Caller.Read(); }", caller);
        var implementation = assets.CompileSource("Structural.Implementation",
            "namespace Shared; public class Box<T> where T : System.Exception { }");
        assets.CompileSource("Structural.Contract",
            "[assembly: System.Runtime.CompilerServices.TypeForwardedTo(typeof(Shared.Box<>))]", implementation);
        var options = new CompilerOptions(app, [assets.CoreLib, caller, implementation], "Entry", "Run", [], target,
            Optimization: FinalWasmOptimization.None, MetadataReferencePaths: [contract]);
        if (incompatible)
            Assert.Equal(DiagnosticCode.AssemblyResolution,
                Assert.Throws<CompilerException>(() => NetWasmCompiler.Compile(options)).Diagnostic.Code);
        else
        {
            var result = NetWasmCompiler.Compile(options);
            Assert.NotEmpty(result.ApplicationModule);
            Assert.DoesNotContain(result.Program.MethodInstances.Values,
                method => method.Definition.Key.Assembly.Name == "Structural.Implementation");
        }
    }
}
