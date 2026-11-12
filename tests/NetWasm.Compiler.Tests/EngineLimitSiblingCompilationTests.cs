using System.Globalization;
using System.Text;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// Siblings of issue 93, where a function body over the 7,654,321 bytes engines accept failed only
// after linking, with a byte offset: the other limits browsers, Node and Wasmtime put on one
// function, 50,000 locals (its parameters among them) and 1,000 parameters. A method past each,
// which desktop .NET runs, fails as it is compiled, with NW1013 naming the method and the limit.
public sealed class EngineLimitSiblingCompilationTests
{
    public static TheoryData<string, bool, WasmTarget> Cases
    {
        get
        {
            var cases = new TheoryData<string, bool, WasmTarget>();
            foreach (var name in Programs.Keys)
                foreach (var optimize in new[] { false, true })
                    foreach (var target in new[] { WasmTarget.Wasm32, WasmTarget.Wasm64 })
                        cases.Add(name, optimize, target);
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void AMethodPastAnEngineLimitIsNamedAsItCompiles(string name, bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var (source, method, limit) = Programs[name];
        var assemblyName = $"EngineLimit{name}{(optimize ? "Release" : "Debug")}{target}";
        var assembly = optimize ? assets.CompileOptimizedSource(assemblyName, source) : assets.CompileSource(assemblyName, source);

        var error = Assert.Throws<CompilerException>(() => NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EngineLimits.EntryPoint", "Run", [], Target: target)));

        Assert.Equal(DiagnosticCode.FunctionLimit, error.Diagnostic.Code);
        Assert.Contains(method, error.Diagnostic.Method, StringComparison.Ordinal);
        Assert.Contains(limit, error.Message, StringComparison.Ordinal);
    }

    private static readonly Dictionary<string, (string Source, string Method, string Limit)> Programs = new()
    {
        ["Locals"] = (Locals(50_001), "EntryPoint::Run", "over the 50,000"),
        ["Parameters"] = (Parameters(1_001), "EntryPoint::Many", "1,001 parameters, over the 1,000"),
    };

    // Every local is assigned before any is read, so all are live at once and stay locals.
    private static string Locals(int count)
    {
        var body = new StringBuilder();
        for (var index = 0; index < count; index++) body.Append(CultureInfo.InvariantCulture, $"        int v{index} = F(input + {index});\n");
        body.Append("        var sum = 0;\n");
        for (var index = 0; index < count; index++) body.Append(CultureInfo.InvariantCulture, $"        sum = sum * 31 + v{index};\n");
        body.Append("        return sum;\n");
        return Program($$"""
                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int F(int value) => value * 3 + 1;

                public static int Run(int input)
                {
            {{body}}    }
            """);
    }

    private static string Parameters(int count)
    {
        var parameters = string.Join(", ", Enumerable.Range(0, count).Select(index => $"int p{index}"));
        var arguments = string.Join(", ", Enumerable.Range(0, count).Select(index => $"input + {index}"));
        var last = count - 1;
        return Program($$"""
                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int Many({{parameters}}) => p0 * 3 + p{{last / 2}} * 5 + p{{last}} * 7 + (p{{last - 1}} ^ p1);

                public static int Run(int input) => Many({{arguments}});
            """);
    }

    private static string Program(string members) => $$"""
        using System.Runtime.CompilerServices;
        namespace EngineLimits;

        public static class EntryPoint
        {
        {{members}}
        }
        """;
}
