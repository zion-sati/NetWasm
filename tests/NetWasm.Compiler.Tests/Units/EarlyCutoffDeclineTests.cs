using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Units;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests.Units;

// The early cutoff patches the last compile's module only when it can prove the result is
// the full compile's; each premise it cannot prove declines it, and the compile then runs
// in full. Either way the module is the one a plain compile gives.
public sealed class EarlyCutoffDeclineTests
{
    private const string Plain = """
        public static class EntryPoint
        {
            public static int Run(int input) => Twice(input) + 1;

            private static int Twice(int value) => value * 2;
        }
        """;

    private const string Filtered = """
        public static class EntryPoint
        {
            public static int Run(int input) => Filtered(input);

            private static int Filtered(int input)
            {
                try
                {
                    return input > 50 ? throw new System.ArgumentException("large") : input + 1;
                }
                catch (System.ArgumentException) when (input > 60)
                {
                    return -1;
                }
            }
        }
        """;

    private const string Guarded = """
        public static class EntryPoint
        {
            public static int Run(int input) => Guarded(input);

            private static int Guarded(int input)
            {
                try
                {
                    return checked(input * 1000);
                }
                catch (System.OverflowException)
                {
                    return -1;
                }
            }
        }
        """;

    // Two guarded regions side by side; the edit nests the second in the first, which keeps
    // what the method adds (the same regions and catch types) but not its exception groups.
    private const string Siblings = """
        public static class EntryPoint
        {
            public static int Run(int input) => Guarded(input);

            private static int Guarded(int input)
            {
                var total = 0;
                try { total += checked(input * 1000); } catch (System.OverflowException) { total -= 1; }
                try { total += checked(input * 2000); } catch (System.OverflowException) { total -= 2; }
                return total;
            }
        }
        """;

    private const string Nested = """
        public static class EntryPoint
        {
            public static int Run(int input) => Guarded(input);

            private static int Guarded(int input)
            {
                var total = 0;
                try
                {
                    total += checked(input * 1000);
                    try { total += checked(input * 2000); } catch (System.OverflowException) { total -= 2; }
                }
                catch (System.OverflowException) { total -= 1; }
                return total;
            }
        }
        """;

    private const string References = """
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                int first = input, second = input;
                Touch(ref first, ref second);
                return first + second + Loop(ref first, input & 3);
            }

            private static void Touch(ref int first, ref int second)
            {
                first = 7;
            }

            private static int Loop(ref int value, int count) => count > 0 ? Loop(ref value, count - 1) : value + 1;
        }
        """;

    private const string Generic = """
        public static class EntryPoint
        {
            public static int Run(int input) => Echo(input, input) + Echo("text", input).Length;

            private static T Echo<T>(T value, int count)
            {
                if (count > 1) return value;
                return value;
            }
        }
        """;

    private const string Sites = """
        public interface IShape { int Area(); }
        public sealed class Square(int side) : IShape { public int Area() => side * side; }

        public static class EntryPoint
        {
            public static int Run(int input) => Measure(new Square(input), input);

            private static int Measure(IShape shape, object value) => (value is int ? 1 : 0) + shape.Area();
        }
        """;

    private const string Native = """
        using System.Runtime.InteropServices;

        public static class EntryPoint
        {
            public static int Run(int input) => Add(input, 1);

            [DllImport("native_math", EntryPoint = "add")]
            private static extern int Add(int a, int b);
        }
        """;

    public static TheoryData<string, string, string, string> Declines => new()
    {
        // The edit no longer calls a method it called.
        { "contribution", Plain, Plain.Replace("Twice(input) + 1", "input + 1", StringComparison.Ordinal),
            $"{Run} changed what it adds to the program" },
        { "filter", Filtered, Filtered.Replace("input + 1", "input + 2", StringComparison.Ordinal), "has an exception filter" },
        { "groups", Siblings, Nested, "changed its exception groups" },
        // The same writes, to the other argument.
        { "effects", References, References.Replace("        first = 7;", "        second = 7;", StringComparison.Ordinal),
            "changed its by-ref effects" },
        { "cycle", References, References.Replace("value + 1", "value + 2", StringComparison.Ordinal),
            "reads its by-ref effects back through a call cycle" },
        { "generic", Generic, Generic.Replace("count > 1", "count > 2", StringComparison.Ordinal),
            "has no instance of its own to analyse alone" },
        { "native", Native, Native.Replace("Add(input, 1)", "Add(input, 2)", StringComparison.Ordinal),
            "the last compile's plans cannot be replayed" },
    };

    [Theory]
    [MemberData(nameof(Declines))]
    public void AnEditWhosePremiseCannotBeProvenIsDeclined(string name, string first, string second, string reason)
    {
        using var assets = TestAssets.Create();

        var report = Compile(assets, first, second, _ => { });

        Assert.True(report.Contains($"early cutoff: declined (", StringComparison.Ordinal) &&
            report.Contains(reason, StringComparison.Ordinal), $"{name}: {report}");
    }

    // A kept compile another process removed, replaced or damaged costs the cutoff, never
    // the compile.
    // Methods are named by token: the edit's is Run.
    private const string Run = "[UnitCutoff]EntryPoint::0x06000001";

    public static TheoryData<string, string, string> Damage => new()
    {
        { "module", Plain, "no compile was kept" },
        { "facts", Plain, "the last compile kept no facts" },
        { "plans", Plain, "the kept compile is damaged" },
        { "functions", Plain, "the last compile kept no function indices" },
        { "units", Guarded, "has no unit to compare its exception groups with" },
        { "patching", Plain, "the last compile's module cannot be patched" },
        { "function", Plain, $"{Run} has no function in the last compile's module" },
    };

    [Theory]
    [MemberData(nameof(Damage))]
    public void AKeptCompileThatIsNotWhatItWasIsDeclined(string damage, string program, string reason)
    {
        using var assets = TestAssets.Create();
        var edit = program.Replace("+ 1", "+ 3", StringComparison.Ordinal).Replace("* 1000", "* 2000", StringComparison.Ordinal);

        var report = Compile(assets, program, edit, units =>
        {
            switch (damage)
            {
                case "module":
                    File.Delete(Path.Combine(units, "module.wasm"));
                    break;
                case "facts":
                    File.WriteAllText(Path.Combine(units, "facts.json"), "{");
                    break;
                case "plans":
                    File.AppendAllText(Path.Combine(units, "plans.bin"), "x");
                    break;
                case "functions":
                    File.WriteAllBytes(Path.Combine(units, "functions.bin"), []);
                    break;
                case "patching":
                    File.WriteAllBytes(Path.Combine(units, "module.wasm"), [0, 0x61, 0x73, 0x6D]);
                    break;
                case "function":
                    var path = Path.Combine(units, "functions.bin");
                    var functions = UnitFunctionIndices.Read(File.ReadAllBytes(path));
                    File.WriteAllBytes(path, UnitFunctionIndices.Write(
                        [.. functions.Functions.Where(pair => pair.Key != Run)],
                        functions.ImportedFunctions));
                    break;
                default:
                    foreach (var segment in Directory.GetFiles(units, "units-*.seg")) File.Delete(segment);
                    File.Delete(Path.Combine(units, "units.manifest"));
                    break;
            }
        });

        Assert.True(report.Contains(reason, StringComparison.Ordinal), $"{damage}: {report}");
    }

    // Stack traces name source lines: a line that moved, though no body changed, numbers the
    // symbols as the last compile did not.
    [Fact]
    public void AMovedLineChangesTheStackTraceSymbols()
    {
        using var assets = TestAssets.Create();
        var moved = Plain.Replace("    private static int Twice", "\n    private static int Twice", StringComparison.Ordinal);

        var report = Compile(assets, Plain, moved, _ => { }, options => options with { EmitStackTrace = true }, pdb: true);

        Assert.True(report.Contains("early cutoff: declined (the stack-trace symbols changed)", StringComparison.Ordinal), report);
    }

    // A compile with type names is served with the last compile's names.
    [Fact]
    public void AnUnchangedCompileWithTypeNamesIsServed()
    {
        using var assets = TestAssets.Create();

        var report = Compile(assets, Plain, Plain, _ => { }, options => options with { EmitTypeNames = true });

        Assert.Contains("early cutoff: nothing changed", report, StringComparison.Ordinal);
    }

    // An edited method's own dispatch and type-test sites answer the cutoff's replay.
    [Fact]
    public void AnEditedMethodsOwnSitesAnswerItsReplay()
    {
        using var assets = TestAssets.Create();

        var report = Compile(assets, Sites, Sites.Replace("? 1 : 0", "? 2 : 0", StringComparison.Ordinal), _ => { });

        Assert.Contains("early cutoff: patched 1 of the last compile's functions", report, StringComparison.Ordinal);
    }

    // The last compile's report, after the second program compiled into its store; both
    // modules are a plain compile's.
    private static string Compile(TestAssets assets, string first, string second, Action<string> between,
        Func<CompilerOptions, CompilerOptions>? configure = null, bool pdb = false)
    {
        var obj = Path.Combine(assets.Directory, "obj");
        var units = Path.Combine(obj, "netwasm-units");
        foreach (var (step, source) in new[] { ("first", first), ("second", second) })
        {
            var assembly = pdb
                ? assets.CompileSourceWithPortablePdb("UnitCutoff", source, optimize: true)
                : assets.CompileOptimizedSource("UnitCutoff", source);
            var options = new CompilerOptions(assembly, [assets.CoreLib],
                "EntryPoint", "Run", [], Optimization: FinalWasmOptimization.O0);
            options = configure?.Invoke(options) ?? options;
            var plain = NetWasmCompiler.Compile(options).ApplicationModule;
            using var services = new ServiceCollection().AddNetWasmCompiler().BuildServiceProvider();
            var module = services.GetRequiredService<INetWasmModuleCompiler>().Compile(options with
            {
                IntermediateOutputPath = obj,
                CompiledUnits = true,
            }).ApplicationModule;
            Assert.True(plain.AsSpan().SequenceEqual(module), $"{step}: the module differs from a plain compile");
            if (step == "first") between(units);
        }
        return File.ReadAllText(Path.Combine(units, "last-inputs.txt"));
    }
}
