using System.Linq;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests.Correctness;

// A constrained call on a value type that does not implement the method, so
// that a default interface method runs, passes it a box of the value
// (ECMA-335 III.2.1): the method reads the value through the interface, and
// what it does to the box leaves the caller's value as it was. The generic
// math writers IBinaryInteger<T>.WriteBigEndian and WriteLittleEndian are
// such methods.
public sealed class ValueTypeDefaultInterfaceMethodCorpusTests(CorrectnessTestRunner runner)
{
    [Theory]
    [InlineData(FinalWasmOptimization.Oz)]
    [InlineData(FinalWasmOptimization.O3)]
    public void DefaultMethodsRunOnABoxOfTheValue(FinalWasmOptimization optimization) =>
        runner.Run(new(
            "ValueTypeDefaultInterfaceMethodCorpus",
            "NetWasm.Correctness.ValueTypeDefaultMethods",
            Source,
            [.. Enumerable.Range(0, 13)])
        {
            Optimization = optimization,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
            ReportAllMismatches = true,
        });

    // The constrained call becomes the box of the value and a virtual call, with
    // the arguments carried across in new locals; no constrained prefix is left
    // to pass the value's address.
    [Fact]
    public void ConstrainedDefaultMethodCallsBoxTheValue()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("ValueTypeDefaultMethods", Source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "NetWasm.Correctness.ValueTypeDefaultMethods.EntryPoint", "Run", [],
            Optimization: FinalWasmOptimization.None));

        foreach (var (name, arguments) in new[] { ("Twice", 0), ("Sum", 3) })
        {
            var body = result.Program.ConstructedMethods.Values.Single(method =>
                method.Method.Definition.Name == name &&
                method.Method.MethodArguments is [{ FullName: "NetWasm.Correctness.ValueTypeDefaultMethods.Greeter" }]).Body;
            var operations = body.Instructions.Select(static instruction => instruction.Operation).ToArray();
            var box = Array.IndexOf(operations, CilOperation.Box);

            Assert.DoesNotContain(CilOperation.Constrained, operations);
            Assert.Equal(CilOperation.LoadObject, operations[box - 1]);
            Assert.All(operations[(box - 1 - arguments)..(box - 1)], static operation => Assert.Equal(CilOperation.StoreLocal, operation));
            Assert.All(operations[(box + 1)..(box + 1 + arguments)], static operation => Assert.Equal(CilOperation.LoadLocal, operation));
            Assert.Equal(CilOperation.CallVirtual, operations[box + 1 + arguments]);
            Assert.Equal(body.Locals.Length, body.LocalSignatureTypes.Length);
        }
    }

    internal const string Source = """
            using System;
            using System.Numerics;

            namespace NetWasm.Correctness.ValueTypeDefaultMethods;

            public interface IGreeter
            {
                int Id { get; }
                int Twice() => Id * 2;
                int Sum(int left, long right, string text) => Id + (int)right + left + text.Length;
                void Fill(out int value) => value = Id + 7;
                T Echo<T>(T value) => value;
                int Quadruple() => Twice() * 2;
            }
            public struct Greeter : IGreeter { public int Value; public int Id => Value; }

            public interface ICounter { int Count { get; } void Increment(); void IncrementTwice() { Increment(); Increment(); } }
            public struct Counter : ICounter { public int Count { get; private set; } public void Increment() => Count++; }

            public interface IWrap<T> { T Value { get; } T Get() => Value; }
            public struct Wrapper<T> : IWrap<T> { public T Item; public T Value => Item; }

            public interface IBase { int Which() => 1; }
            public interface IDerived : IBase { int IBase.Which() => 2; }
            public struct UsesDerived : IDerived { }

            public struct Plain { public int X; public int Y; }

            public static class EntryPoint
            {
                public static int Run(int input) => input switch
                {
                    0 => Twice(new Greeter { Value = 21 }),
                    1 => Sum(new Greeter { Value = 1 }),
                    2 => Fill(new Greeter { Value = 3 }),
                    3 => Echo(new Greeter { Value = 4 }),
                    4 => Quadruple(new Greeter { Value = 5 }),
                    5 => Mutate(new Counter()),
                    6 => Get(new Wrapper<int> { Item = 9 }) + Get(new Wrapper<string> { Item = "abc" }).Length * 100,
                    7 => Which(new UsesDerived()),
                    8 => Write<int>(258) + Write<sbyte>(-3) * 10000,
                    9 => Write<long>(long.MinValue) + Write<ushort>(65535) * 10000,
                    10 => Write<Int128>(Int128.MaxValue),
                    11 => ((IGreeter)new Greeter { Value = 5 }).Twice(),
                    12 => (Same(new Plain { X = 1, Y = 2 }, new Plain { X = 1, Y = 2 }) ? 1 : 0) +
                        (Hashes(new Plain { X = 3, Y = 4 }) ? 10 : 0),
                    _ => -1,
                };

                public static int Trace() => 0;

                private static int Twice<T>(T value) where T : IGreeter => value.Twice();

                private static int Sum<T>(T value) where T : IGreeter => value.Sum(10, 100, "four");

                private static int Fill<T>(T value) where T : IGreeter
                {
                    value.Fill(out var filled);
                    return filled;
                }

                private static int Echo<T>(T value) where T : IGreeter => value.Echo(40) + value.Echo("xy").Length;

                private static int Quadruple<T>(T value) where T : IGreeter => value.Quadruple();

                // The default method runs on a box: the caller's copy stays.
                private static int Mutate<T>(T counter) where T : ICounter
                {
                    counter.IncrementTwice();
                    counter.Increment();
                    return counter.Count;
                }

                private static T Get<T, TWrap>(TWrap wrapper) where TWrap : IWrap<T> => wrapper.Get();

                private static int Get(Wrapper<int> wrapper) => Get<int, Wrapper<int>>(wrapper);

                private static string Get(Wrapper<string> wrapper) => Get<string, Wrapper<string>>(wrapper);

                private static int Which<T>(T value) where T : IBase => value.Which();

                private static int Write<T>(T value) where T : IBinaryInteger<T>
                {
                    var big = new byte[value.GetByteCount()];
                    var little = new byte[value.GetByteCount()];
                    var written = value.WriteBigEndian(big) + value.WriteLittleEndian(little);
                    return written * 100 + big[^1] + little[0] * 3;
                }

                private static bool Same<T>(T left, T right) => left!.Equals(right);

                private static bool Hashes<T>(T value) => value!.GetHashCode() == value.GetHashCode();
            }
            """;
}
