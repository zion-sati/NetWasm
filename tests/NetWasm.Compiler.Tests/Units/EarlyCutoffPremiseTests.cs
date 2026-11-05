using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Units;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests.Units;

// A method's contribution is a set: an edit that only reorders what a body names keeps it.
// Discovery order then changes, and nothing the whole program derives may follow it, or the
// early cutoff would patch a module that differs elsewhere.
public sealed class EarlyCutoffPremiseTests
{
    private const string Types = """
        public abstract class Shape { public abstract int Area(); }
        public sealed class Square : Shape { public override int Area() => 4; }
        public sealed class Circle : Shape { public override int Area() => 3; }
        public sealed class Line : Shape { public override int Area() => 1; }
        public static class Uses
        {
            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
            public static int Sum(Shape shape) => shape.Area();
        }
        """;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReorderingAllocationsChangesOnlyTheEditedBody(bool declarationFirst)
    {
        using var assets = TestAssets.Create();
        var units = Path.Combine(assets.Directory, "obj");
        var kept = Path.Combine(units, "netwasm-units");
        CompilerOptions Options(string source) =>
            new(assets.CompileOptimizedSource("UnitOrder", source), [assets.CoreLib], "EntryPoint", "Run", [],
                Optimization: FinalWasmOptimization.O0)
            {
                IntermediateOutputPath = units,
                CompiledUnits = true,
            };
        const string First = Types + """
            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    Shape a = new Square();
                    Shape b = new Circle();
                    Shape c = new Line();
                    return Uses.Sum(a) + Uses.Sum(b) + Uses.Sum(c);
                }
            }
            """;
        const string Second = Types + """
            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    Shape c = new Line();
                    Shape b = new Circle();
                    Shape a = new Square();
                    return Uses.Sum(a) + Uses.Sum(b) + Uses.Sum(c);
                }
            }
            """;
        const string LaterFirst = Types + """
            public static class Make
            {
                public static Shape All(int input)
                {
                    Shape a = new Square();
                    Shape b = new Circle();
                    Shape c = new Line();
                    return input == 0 ? a : input == 1 ? b : c;
                }
            }
            public static class EntryPoint
            {
                public static int Run(int input) => Uses.Sum(null!) + Uses.Sum(Make.All(input));
            }
            """;
        var first = declarationFirst ? LaterFirst : First;
        var second = declarationFirst
            ? System.Text.RegularExpressions.Regex.Replace(LaterFirst,
                @"Shape a = new Square\(\);(\s+)Shape b = new Circle\(\);(\s+)Shape c = new Line\(\);",
                "Shape c = new Line();$1Shape b = new Circle();$2Shape a = new Square();")
            : Second;
        Assert.NotEqual(first, second);
        _ = NetWasmCompiler.Compile(Options(first));
        var before = File.ReadAllBytes(Path.Combine(kept, "module.wasm"));
        var functions = UnitFunctionIndices.Read(File.ReadAllBytes(Path.Combine(kept, "functions.bin")));
        var after = NetWasmCompiler.Compile(Options(second)).ApplicationModule;
        var report = File.ReadAllText(Path.Combine(kept, "last-inputs.txt"));
        Assert.Contains("early cutoff: yes", report, StringComparison.Ordinal);
        var oldBodies = WasmCodePatcher.Bodies(before)!;
        var newBodies = WasmCodePatcher.Bodies(after)!;
        var names = functions.Functions.ToDictionary(pair => pair.Value - functions.ImportedFunctions, pair => pair.Key);
        var differing = new List<string>();
        for (var position = 0; position < Math.Min(oldBodies.Count, newBodies.Count); position++)
        {
            if (!oldBodies[position].AsSpan().SequenceEqual(newBodies[position]))
                differing.Add(names.GetValueOrDefault(position, $"#{position}"));
        }
        var patch = new Dictionary<int, byte[]>();
        for (var position = 0; position < newBodies.Count; position++)
            if (!oldBodies[position].AsSpan().SequenceEqual(newBodies[position])) patch[position] = newBodies[position];
        var patched = WasmCodePatcher.ReplaceBodies(before, patch)!;

        Assert.Equal(oldBodies.Count, newBodies.Count);
        Assert.Equal([declarationFirst ? "Make" : "EntryPoint"], differing.Select(name => name[(name.IndexOf(']') + 1)..name.IndexOf(':')]));
        Assert.True(patched.AsSpan().SequenceEqual(after), "the reordering changed more than its method's body");
    }
}
