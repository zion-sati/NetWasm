using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// Issue #123: the entry point and every JSExport wrapper call the module's one runtime
// initializer, so the type, value-type and static-root registrations exist once however
// many exports the module has.
public sealed partial class RuntimeInitializerCompilationTests
{
    private const string Source = """
        using System.Collections.Generic;
        using System.Runtime.InteropServices.JavaScript;

        public abstract class Shape { public abstract int Area(); }
        public sealed class S1 : Shape { public override int Area() => 1; }
        public sealed class S2 : Shape { public override int Area() => 2; }
        public sealed class S3 : Shape { public override int Area() => 3; }
        public sealed class S4 : Shape { public override int Area() => 4; }

        public static class EntryPoint
        {
            private static readonly List<Shape> Shapes = [new S1(), new S2(), new S3(), new S4()];

            private static int Total()
            {
                var total = 0;
                foreach (var shape in Shapes)
                {
                    total += shape.Area();
                }
                return total;
            }

            public static int Run(int input) => input + Total();

            [JSExport("a")]
            public static int A() => Total();

            [JSExport("b")]
            public static int B() => Total() + 1;

            [JSExport("c")]
            public static int C() => Total() + 2;
        }
        """;

    [Theory]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.Oz)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.O3)]
    public void EveryEntryCallsTheOneRuntimeInitializer(WasmTarget target, FinalWasmOptimization preset)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("SharedRuntimeInitializer", Source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            Target: target, Optimization: preset));
        var module = Path.Combine(assets.Directory, "application.wasm");
        File.WriteAllBytes(module, result.ApplicationModule);
        using var services = new ServiceCollection().AddNetWasmCompiler().BuildServiceProvider();
        var printed = services.GetRequiredService<IWasmTools>().Run("print", module);
        Assert.True(printed.ExitCode == 0, printed.StandardError);

        var registerType = RegisterTypeImport().Match(printed.StandardOutput);
        Assert.True(registerType.Success, "The module imports no register_type.");
        var functions = FunctionHeader().Split(printed.StandardOutput).Skip(1)
            .Select(static function => (Name: function[..function.IndexOf(' ')], Body: function))
            .ToArray();
        bool Calls(string body, string callee) =>
            Regex.IsMatch(body, $@"^\s*call {Regex.Escape(callee)}\s*$", RegexOptions.Multiline);
        Assert.Equal(["$runtime.initialize"], functions
            .Where(function => Calls(function.Body, registerType.Groups[1].Value))
            .Select(static function => function.Name));
        foreach (var export in new[] { "a", "b", "c" })
        {
            var wrapper = Assert.Single(functions, function => function.Name == $"$netwasm.export.{export}");
            Assert.True(Calls(wrapper.Body, "$runtime.initialize"), $"Export '{export}' does not initialize the runtime.");
        }

        Assert.Equal(15, ExecuteWithStandardWasiNode(
            result.ApplicationModule, assets.Directory, 5, target, result.StaticDataEnd));
    }

    [GeneratedRegex(@"\n\s*\(func ")]
    private static partial Regex FunctionHeader();

    [GeneratedRegex(@"\(import ""[^""]+"" ""register_type"" \(func (\$\S+)")]
    private static partial Regex RegisterTypeImport();
}
