using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class DiagnosticOutputCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, false)]
    [InlineData(WasmTarget.Wasm32, true)]
    [InlineData(WasmTarget.Wasm64, false)]
    [InlineData(WasmTarget.Wasm64, true)]
    public void ConditionalCallsAndArgumentEvaluationFollowTheConsumerBuild(WasmTarget target, bool debug)
    {
        using var assets = TestAssets.Create();
        var source = File.ReadAllText(Path.Combine(assets.Root, "tests", "end-to-end", "diagnostic-output", "Program.cs"));
        source += "\npublic static class EntryPoint { public static int Run(int input) => Program.Main(); }";
        var assembly = assets.CompileSource("DiagnosticOutput", (debug ? "#define DEBUG\n" : "") + source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "EntryPoint", "Run", [], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/async-platform@1.0.0"));
        var methods = result.Program.Methods.Values.Concat(result.Program.ConstructedMethods.Values)
            .Select(body => body.Method).ToArray();
        var output = methods.Where(method => method.DeclaringType.FullName == "System.Diagnostics.Debug").ToArray();
        if (debug)
        {
            Assert.Equal(5, output.Length);
            Assert.All(output, method => Assert.Equal("WriteLine", method.Definition.Name));
            Assert.Contains(methods, method => method.DeclaringType.FullName == "Program" && method.Definition.Name == "Evaluate");
            Assert.Contains(methods, method => method.DeclaringType.FullName == "System.Console" && method.Definition.Name == "get_Error");
        }
        else
        {
            Assert.Empty(output);
            Assert.DoesNotContain(methods, method => method.DeclaringType.FullName == "Program" && method.Definition.Name == "Evaluate");
            Assert.DoesNotContain(methods, method => method.DeclaringType.FullName == "System.Console" && method.Definition.Name == "get_Error");
        }
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
    }
}
