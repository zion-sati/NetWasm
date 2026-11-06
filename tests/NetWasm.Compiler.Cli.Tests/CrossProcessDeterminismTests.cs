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

    [Fact]
    public void TwoProcessesCompileOneProgramToTheSameBytes()
    {
        using var assets = TestAssets.Create();
        var application = assets.CompileOptimizedSource("Determinism", Source);

        var first = Compile(assets, application, "first.wasm");
        var second = Compile(assets, application, "second.wasm");

        Assert.True(first.AsSpan().SequenceEqual(second), "two nwc processes wrote different modules for one program");
    }

    private static byte[] Compile(TestAssets assets, string application, string name)
    {
        var output = Path.Combine(assets.Directory, name);
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in (string[])
                 [
                     Path.Combine(AppContext.BaseDirectory, "nwc.dll"), "-nologo", application, $"-r:{assets.CoreLib}",
                     "-target:function", "-entry:Determinism.EntryPoint::Run", "-optimize:O3", $"-out:{output}",
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
