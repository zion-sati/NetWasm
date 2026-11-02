using System.Linq;
using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Compiler.Tests.Correctness;

// A program that reads type names gets the name facts it reads at every
// optimization level, however far inlining folds the readers into it, including
// a name read in a catch block. Each case returns a hash of the names it read.
public sealed class TypeNameCorpusTests(CorrectnessTestRunner runner)
{
    [Theory]
    [InlineData(FinalWasmOptimization.Oz)]
    [InlineData(FinalWasmOptimization.O3)]
    public void TypeNamesMatchDesktopAtEveryOptimizationLevel(FinalWasmOptimization optimization) =>
        runner.Run(new(
            "TypeNameCorpus",
            "NetWasm.Correctness.TypeNames",
            Source,
            [.. Enumerable.Range(0, 8)])
        {
            Optimization = optimization,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
        });

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
                    _ => -1,
                };

                public static int Trace() => 0;

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
