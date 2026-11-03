using System.Linq;
using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Compiler.Tests.Correctness;

// isinst and castclass of every value in a matrix against classes, abstract
// and never allocated subclasses, generic instantiations, interfaces, arrays
// under covariance and int/uint compatibility, boxed primitives and enums,
// variant delegates and exception hierarchies, at Oz and O3. A class test may
// check the interval of ids the layout compiler numbers for the class and its
// subtypes, so the matrix puts unallocated and generic subtypes inside those
// intervals and unrelated types around them.
public sealed class TypeTestCorpusTests(CorrectnessTestRunner runner)
{
    [Theory]
    [InlineData(FinalWasmOptimization.Oz)]
    [InlineData(FinalWasmOptimization.O3)]
    public void TypeTestsMatchDesktop(FinalWasmOptimization optimization) =>
        runner.Run(new(
            "TypeTestCorpus",
            "NetWasm.Correctness.TypeTests",
            Source,
            [.. Enumerable.Range(0, 3)])
        {
            Optimization = optimization,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
            ReportAllMismatches = true,
        });

    private const string Source = """
            using System;
            using System.Collections.Generic;
            using System.IO;

            namespace NetWasm.Correctness.TypeTests;

            public interface IPet { }
            public abstract class Animal { }
            public class Dog : Animal, IPet { }
            public sealed class Puppy : Dog { }
            public sealed class Cat : Animal, IPet { }
            public abstract class NeverMade : Animal { }
            public sealed class AlsoNeverMade : NeverMade { }
            public class Box<T> : Animal { }
            public sealed class Crate<T> : Box<T> { }
            public sealed class Rock { }
            public enum Color { Red, Green }

            public static class EntryPoint
            {
                private static object?[] Values() =>
                [
                    new Dog(), new Puppy(), new Cat(), new Box<int>(), new Crate<string>(), new Rock(),
                    new string[1], new int[1], new uint[1], new object[1], new Dog[1], new Puppy[1], new int[1, 1],
                    5, 5u, Color.Green, (Func<string>)(() => ""), (Action)(() => { }),
                    new ArgumentNullException(), new FileNotFoundException(), "text", new List<int>(), null,
                ];

                public static int Run(int input) => input switch
                {
                    0 => IsInstance(),
                    1 => CastClass(),
                    2 => Generic<Animal>() * 31 + Generic<Box<int>>() * 7 + Generic<Array>(),
                    _ => throw new ArgumentOutOfRangeException(nameof(input)),
                };

                private static int IsInstance()
                {
                    var hash = 17;
                    foreach (var value in Values())
                    {
                        bool[] tests =
                        [
                            value is Animal, value is Dog, value is Puppy, value is Cat, value is NeverMade,
                            value is AlsoNeverMade, value is Box<int>, value is Box<string>, value is Crate<string>,
                            value is IPet, value is Array, value is object[], value is Animal[], value is IPet[],
                            value is int[], value is uint[], value is int[,], value is ValueType, value is Enum,
                            value is int, value is Color, value is Delegate, value is MulticastDelegate,
                            value is Func<object>, value is Func<string>, value is Action, value is Exception,
                            value is SystemException, value is ArgumentException, value is IOException,
                            value is string, value is IComparable, value is IEnumerable<int>, value is object,
                        ];
                        foreach (var test in tests)
                        {
                            hash = unchecked(hash * 31 + (test ? 1 : 2));
                        }
                    }
                    return hash;
                }

                private static int CastClass()
                {
                    var hash = 17;
                    foreach (var value in Values())
                    {
                        hash = unchecked(hash * 31 + Cast(() => (Animal)value!));
                        hash = unchecked(hash * 31 + Cast(() => (Dog)value!));
                        hash = unchecked(hash * 31 + Cast(() => (NeverMade)value!));
                        hash = unchecked(hash * 31 + Cast(() => (Box<int>)value!));
                        hash = unchecked(hash * 31 + Cast(() => (Array)value!));
                        hash = unchecked(hash * 31 + Cast(() => (object[])value!));
                        hash = unchecked(hash * 31 + Cast(() => (Enum)value!));
                        hash = unchecked(hash * 31 + Cast(() => (Exception)value!));
                    }
                    return hash;
                }

                private static int Cast(Func<object?> cast)
                {
                    try
                    {
                        return cast() is null ? 1 : 2;
                    }
                    catch (InvalidCastException)
                    {
                        return 3;
                    }
                }

                // The same tests where the target is a generic argument.
                private static int Generic<T>()
                {
                    var count = 0;
                    foreach (var value in Values())
                    {
                        count = count * 2 + (value is T ? 1 : 0);
                        count &= 0xFFFFFF;
                    }
                    return count;
                }

                public static int Trace() => 0;
            }
            """;
}
