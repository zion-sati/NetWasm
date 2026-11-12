using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests.Correctness;

// A program that reads type names gets the names .NET forms at every optimization level, however
// far inlining folds the readers into it, including a name read in a catch block. Names come from
// the type-symbols sidecar, which a NetWasmTypeNames program in the platform world reads at startup
// (issue 225). Each case hashes the names it read; Matches sets a bit for each case whose hash is the
// one desktop .NET computes (pinned in the source, from running it on .NET).
public sealed class TypeNameCorpusTests
{
    [Theory]
    [InlineData(FinalWasmOptimization.Oz, WasmTarget.Wasm32)]
    [InlineData(FinalWasmOptimization.O3, WasmTarget.Wasm32)]
    [InlineData(FinalWasmOptimization.O3, WasmTarget.Wasm64)]
    public void TypeNamesMatchDesktopAtEveryOptimizationLevel(FinalWasmOptimization optimization, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);

        Assert.Equal(0x1ff, executor.Execute(new CompilationScenario(
            "TypeNameCorpus", Source, "NetWasm.Correctness.TypeNames.EntryPoint", true, target, 0, [])
        {
            EntryMethodName = "Matches",
            Exports = [new("run", "NetWasm.Correctness.TypeNames.EntryPoint", "Matches")],
            WitPath = Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld = "netwasm:platform/platform@1.0.0",
            EmitTypeNames = true,
            Optimization = optimization,
        }));
    }

    // Without the sidecar (a host that does not mount it, or a program built without names) every
    // read still succeeds: a name, full name or display name reads type#<id>, a namespace is null.
    [Theory]
    [InlineData(true, FinalWasmOptimization.Oz, WasmTarget.Wasm32)]
    [InlineData(false, FinalWasmOptimization.O3, WasmTarget.Wasm32)]
    [InlineData(true, FinalWasmOptimization.O3, WasmTarget.Wasm64)]
    public void TypeNamesReadTypeIdsWithoutTheSidecar(bool emitTypeNames, FinalWasmOptimization optimization, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);

        Assert.Equal(0x1ff, executor.Execute(new CompilationScenario(
            "TypeNameFallbacks", Source, "NetWasm.Correctness.TypeNames.EntryPoint", true, target, 0, [])
        {
            EntryMethodName = "Fallbacks",
            Exports = [new("run", "NetWasm.Correctness.TypeNames.EntryPoint", "Fallbacks")],
            WitPath = Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld = "netwasm:platform/platform@1.0.0",
            EmitTypeNames = emitTypeNames,
            LoadStackTraceSymbols = false,
            Optimization = optimization,
        }));
    }

    internal const string Source = """
            using System;
            using System.Collections.Generic;

            namespace NetWasm.Correctness.TypeNames;

            public class Outer { public class Inner { } }
            public struct Point { public int X; }

            public static class EntryPoint
            {
                public static int Run(int input) => input switch
                {
                    0 => Hash(Names(new object[] { "s", new int[1], new Version(1, 2), new FormatException() })),
                    1 => Hash(typeof(Outer.Inner).Name + typeof(Outer.Inner).FullName + typeof(Outer.Inner).Namespace),
                    2 => Hash(typeof(List<int>).Name + typeof(Dictionary<string, int>).Name),
                    3 => Hash(new Point().GetType().Name + typeof(Point[]).Name + typeof(int[,]).Name),
                    4 => Hash(typeof(KeyValuePair<int, string>).Name + typeof(KeyValuePair<int, string>).Namespace),
                    5 => Hash(Thrown()),
                    6 => Hash(typeof(Outer).ToString()),
                    7 => Hash(Generic<Point>()),
                    8 => Hash(Arguments(typeof(Dictionary<char, Point>))),
                    _ => -1,
                };

                // Each case's hash on desktop .NET.
                private static readonly int[] Desktop =
                    [941914538, 25241197, -1873454232, 1474980625, -645004562, 171303406, -209231920, 2122584053, -393104417];

                // Each case whose every name reads type#<id> and whose namespaces are null sets its bit.
                public static int Fallbacks(int unused)
                {
                    var reads = new Func<string?[]>[]
                    {
                        () => Array.ConvertAll(new object[] { "s", new int[1], new Version(1, 2), new FormatException() }, value => value.GetType().Name),
                        () => [typeof(Outer.Inner).Name, typeof(Outer.Inner).FullName],
                        () => [typeof(List<int>).Name, typeof(Dictionary<string, int>).Name],
                        () => [new Point().GetType().Name, typeof(Point[]).Name, typeof(int[,]).Name],
                        () => [typeof(KeyValuePair<int, string>).Name],
                        () => [Thrown()],
                        () => [typeof(Outer).ToString()],
                        () => [typeof(Point).Name, typeof(Point).FullName],
                        () => Array.ConvertAll(typeof(Dictionary<char, Point>).GetGenericArguments(), argument => argument.Name),
                    };
                    var fallbacks = 0;
                    for (var index = 0; index < reads.Length; index++)
                    {
                        var all = true;
                        foreach (var name in reads[index]()) all &= IsTypeId(name);
                        if (all && typeof(Outer.Inner).Namespace is null && typeof(Point).Namespace is null) fallbacks |= 1 << index;
                    }
                    return fallbacks;
                }

                private static bool IsTypeId(string? name)
                {
                    if (name is null || !name.StartsWith("type#", StringComparison.Ordinal) || name.Length == 5) return false;
                    for (var index = 5; index < name.Length; index++)
                    {
                        if (!char.IsAsciiDigit(name[index])) return false;
                    }
                    return true;
                }

                public static int Matches(int unused)
                {
                    var matches = 0;
                    for (var input = 0; input < Desktop.Length; input++)
                    {
                        if (Run(input) == Desktop[input]) matches |= 1 << input;
                    }
                    return matches;
                }

                private static string Names(object[] values)
                {
                    var names = "";
                    foreach (var value in values)
                    {
                        names += value.GetType().Name + ";";
                    }
                    return names;
                }

                private static string Thrown()
                {
                    try
                    {
                        throw new InvalidOperationException("boom");
                    }
                    catch (Exception exception)
                    {
                        return exception.GetType().Name;
                    }
                }

                private static string Generic<T>() => typeof(T).Name + typeof(T).Namespace;

                private static string Arguments(Type type)
                {
                    var names = "";
                    foreach (var argument in type.GetGenericArguments())
                    {
                        names += argument.Name + ";";
                    }
                    return names;
                }

                private static int Hash(string text)
                {
                    var hash = 17;
                    foreach (var character in text)
                    {
                        hash = unchecked(hash * 31 + character);
                    }
                    return hash;
                }
            }
            """;
}
