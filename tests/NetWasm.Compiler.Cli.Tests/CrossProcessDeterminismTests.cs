using System.Diagnostics;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Cli.Tests;

// What the compiler writes depends only on its inputs: one program compiled by two nwc processes
// is the same bytes. Each process hashes strings with a seed of its own, so a pass that numbers or
// orders what it emits by a hash-ordered collection keyed by names differs between the two, which a
// second compile in the same process cannot show.
public sealed class CrossProcessDeterminismTests
{
    private const string World = "netwasm:platform@1.0.0/platform";

    // Shapes the managed optimizations rewrite: objects replaced by their fields, a foreach over an
    // array through its interface, structs split into locals, generic instances, delegates and
    // string-keyed lookups.
    private const string Source = """
        using System;
        using System.Collections.Generic;

        namespace Determinism;

        public sealed class Range(int start, int count)
        {
            public int Start = start;
            public int Count = count;
            public int Step = 1;
            public long Total;
        }

        public struct Pair(int left, int right)
        {
            public int Left = left;
            public int Right = right;
        }

        public static class EntryPoint
        {
            private static readonly int[] s_values = [3, 1, 4, 1, 5, 9, 2, 6];
            private static readonly Dictionary<string, int> s_names = new() { ["one"] = 1, ["two"] = 2, ["three"] = 3 };

            public static int Run(int input)
            {
                var range = new Range(input, 4);
                long sum = range.Start + range.Count * range.Step;
                IEnumerable<int> values = s_values;
                foreach (var value in values) sum += value;
                var pair = new Pair(input, (int)sum);
                sum += Swap(pair).Left;
                Func<int, int> twice = value => value * 2;
                sum += Apply(twice, input) + Largest(new List<int>(s_values));
                sum += s_names.TryGetValue(input % 2 == 0 ? "two" : "three", out var named) ? named : 0;
                return (int)sum;
            }

            private static Pair Swap(Pair pair) => new(pair.Right, pair.Left);

            private static T Apply<T>(Func<T, T> function, T value) => function(value);

            private static T Largest<T>(List<T> items) where T : IComparable<T>
            {
                var largest = items[0];
                foreach (var item in items) if (item.CompareTo(largest) > 0) largest = item;
                return largest;
            }
        }
        """;

    // Shapes the rest of the pipeline lowers: default interface methods and generic virtual
    // methods, async state machines, exception regions, static constructors, string switches,
    // records, multidimensional arrays and enum formatting.
    private const string BroadSource = """
        using System;
        using System.Collections.Generic;
        using System.Threading.Tasks;

        namespace Determinism;

        public interface IShape { double Area(); string Name => "shape"; }
        public record Circle(double Radius) : IShape { public double Area() => Math.PI * Radius * Radius; public string Name => "circle"; }
        public record Square(double Side) : IShape { public double Area() => Side * Side; }
        public enum Mode { Fast, Careful, Lazy }
        public abstract class Visitor { public abstract T Visit<T>(T value) where T : notnull; }
        public sealed class Echo : Visitor { public override T Visit<T>(T value) => value; }

        public static class Registry
        {
            public static readonly Dictionary<string, Func<int, int>> Operations = new()
            {
                ["double"] = value => value * 2, ["square"] = value => value * value, ["negate"] = value => -value,
            };
        }

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                IShape[] shapes = [new Circle(input), new Square(input + 1), new Circle(2)];
                var area = 0;
                var names = new List<string>();
                foreach (var shape in shapes)
                {
                    if (shape.Area() > 3) area += (int)shape.Area();
                    if (!names.Contains(shape.Name)) names.Add(shape.Name);
                }
                names.Sort(string.CompareOrdinal);
                var joined = string.Join(",", names);
                var grid = new int[3, 3];
                grid[input % 3, 1] = input;
                var total = area + joined.Length + grid[input % 3, 1] + Classify(joined) + Registry.Operations["square"](input);
                total += new Echo().Visit(input) + new Echo().Visit("text").Length + ((Mode)(input % 3)).ToString().Length;
                total += Guarded(input) + Compute(input).GetAwaiter().GetResult();
                return total;
            }

            private static int Classify(string text) => text switch
            {
                "circle,shape" => 1, "shape" => 2, "circle" => 3, "triangle" => 4, "hexagon" => 5, _ => 0,
            };

            private static int Guarded(int input)
            {
                try
                {
                    try { return checked(int.MaxValue + input); }
                    finally { input++; }
                }
                catch (OverflowException) when (input > 0) { return input; }
            }

            private static async Task<int> Compute(int input)
            {
                await Task.CompletedTask;
                var tasks = new Task<int>[3];
                for (var index = 0; index < tasks.Length; index++) tasks[index] = Scale(index, input);
                var values = await Task.WhenAll(tasks);
                var sum = 0;
                foreach (var value in values) sum += value;
                return sum;
            }

            private static async Task<int> Scale(int index, int input)
            {
                await Task.CompletedTask;
                return index * input;
            }
        }
        """;

    [Fact]
    public void TwoProcessesCompileOneProgramToTheSameBytes()
    {
        using var assets = TestAssets.Create();
        var application = assets.CompileOptimizedSource("Determinism", Source);

        var first = Compile(assets, application, "first.wasm", "O3");
        var second = Compile(assets, application, "second.wasm", "O3");

        Assert.True(first.AsSpan().SequenceEqual(second), "two nwc processes wrote different modules for one program");
    }

    // The bytes depend on neither the compiler's thread count nor where the input lies (issue
    // 187's class), at every preset: a pass that numbers what it emits in the order parallel work
    // finishes, or that writes the input's path, differs between these three compiles.
    [Theory]
    [InlineData("O0")]
    [InlineData("O2")]
    [InlineData("O3")]
    [InlineData("Oz")]
    public void ThreadsAndInputPathsLeaveTheModuleAsItIs(string optimization)
    {
        using var assets = TestAssets.Create();
        var application = assets.CompileOptimizedSource("Determinism", BroadSource);
        var elsewhere = Path.Combine(assets.Directory, "elsewhere", "nested");
        Directory.CreateDirectory(elsewhere);
        var moved = Path.Combine(elsewhere, Path.GetFileName(application));
        File.Copy(application, moved);

        var first = Compile(assets, application, "first.wasm", optimization);
        var serial = Compile(assets, moved, "serial.wasm", optimization, processorCount: 1);
        var third = Compile(assets, moved, "third.wasm", optimization);

        Assert.True(first.AsSpan().SequenceEqual(serial), $"one thread wrote a different module at {optimization}");
        Assert.True(first.AsSpan().SequenceEqual(third), $"another input path wrote a different module at {optimization}");
    }

    private static byte[] Compile(
        TestAssets assets,
        string application,
        string name,
        string optimization,
        int? processorCount = null)
    {
        var output = Path.Combine(assets.Directory, name);
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        if (processorCount is { } count)
        {
            start.Environment["DOTNET_PROCESSOR_COUNT"] = count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        foreach (var argument in (string[])
                 [
                     Path.Combine(AppContext.BaseDirectory, "nwc.dll"), "-nologo", application, $"-r:{assets.CoreLib}",
                     "-target:function", "-entry:Determinism.EntryPoint::Run", $"-optimize:{optimization}", $"-out:{output}",
                     $"-wit:{Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0")}", $"-world:{World}",
                 ])
        {
            start.ArgumentList.Add(argument);
        }
        using var process = Process.Start(start) ?? throw new InvalidOperationException("nwc did not start");
        var error = process.StandardError.ReadToEndAsync();
        var text = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, text + error.Result);
        return File.ReadAllBytes(output);
    }
}
