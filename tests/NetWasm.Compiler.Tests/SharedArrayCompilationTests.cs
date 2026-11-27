// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Tests.Correctness;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// Arrays Array.CreateInstance makes of a reference element the program names no array of are shared arrays, which keep
// the element in their header (SharedArrayShapes). Each case uses one as a program would use the array of its element,
// and answers as desktop .NET 11 does: casts to its own type, base arrays and the array interfaces, and to types it is
// not; enumeration where the program otherwise allocates one other implementation; stores, spans, copies, clones and
// LINQ; GetType's object and facts; elements of each kind; elements no array can have; and a rank-two array.
public sealed class SharedArrayCompilationTests(CorrectnessTestRunner runner)
{
    private const string EntryType = "SharedArrays.EntryPoint";

    // The native runtime's casts and copies of shared arrays, and its collector, against desktop.
    [Theory]
    [InlineData("Release-Wasm32-Direct-Linked", FinalWasmOptimization.None)]
    [InlineData("Release-Wasm32-Direct-Linked", FinalWasmOptimization.O3)]
    [InlineData("Release-Wasm64-Direct-Linked", FinalWasmOptimization.Oz)]
    public void TheLinkedRuntimeSeesDesktopsResults(string cell, FinalWasmOptimization optimization) => runner.Run(
        new CorpusFixture("SharedArrays", "SharedArrays", Source, [0])
        {
            CaseId = "array.runtime-creation.shared",
            RequiredRuntimeCapabilities = OracleRuntimeCapabilities.GarbageCollection,
            Matrix = new(CorpusMatrixProfile.Extended, cell, CorpusExecutionBackend.Linked),
            ExposesLegacyTrace = false,
            OracleMode = OracleMode.SameSource,
            SameSourceReason = "Compare shared arrays against the arrays of their elements on the desktop runtime.",
            Optimization = optimization,
        });

    [Theory]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O1)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O3)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.Oz)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.O1)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.O3)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.Oz)]
    public void SharedArraysAnswerAsTheArraysOfTheirElementsDo(WasmTarget target, FinalWasmOptimization optimization)
    {
        var expected = InProcessDesktopResults.Of(Source, "SharedArrays.Desktop", EntryType);
        Assert.Equal(CaseNames.Length, expected.Length);

        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource($"SharedArrays{target}{optimization}", Source);
        var compilation = NetWasmCompiler.Compile(CompilerTestSupport.CreateReactorOptions(
            assets, assembly, EntryType, "Print", target) with { Optimization = optimization });
        var actual = CompilerTestSupport.ExecuteWithStandardWasiNodeCapturingOutput(
                compilation.ApplicationModule, assets.Directory, 0, target, compilation.StaticDataEnd)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(int.Parse)
            .ToArray();
        Assert.Equal(CaseNames.Length, actual.Length);

        var differences = Enumerable.Range(0, CaseNames.Length)
            .Where(index => actual[index] != expected[index])
            .Select(index => $"{CaseNames[index]}: {actual[index]}, desktop {expected[index]}")
            .ToList();
        Assert.True(differences.Count == 0,
            $"{differences.Count} of {CaseNames.Length} cases differ from desktop:\n" + string.Join("\n", differences));
    }

    private static readonly string[] CaseNames =
    [
        "casts of a Derived[]",
        "cast to an array it is not",
        "enumeration beside the one other implementation",
        "covariant store through a base array",
        "store of the wrong type through object[]",
        "IList<Base> over a Derived[]",
        "spans",
        "copies to and from typed arrays",
        "narrowing copy",
        "clone",
        "LINQ and List<T>",
        "GetType",
        "elements of each kind",
        "elements no array has",
        "rank two",
    ];

    private const string Source = """
        #nullable disable
        using System;
        using System.Collections;
        using System.Collections.Generic;
        using System.Linq;

        namespace SharedArrays;

        public class Base : IComparable<Base>
        {
            public int Value;
            public int CompareTo(Base other) => Value.CompareTo(other.Value);
        }
        public sealed class Derived : Base { }
        public sealed class Other { }
        public interface IShape { int Sides { get; } }
        public sealed class Square : IShape { public int Sides => 4; }
        public sealed class Lone { }
        public sealed class Grid { }

        public static class EntryPoint
        {
            // Each element is a type argument the program names, as dependency injection reads it.
            private static Type Of<T>() => typeof(IEnumerable<T>).GenericTypeArguments[0];
            private static Array Make<T>(int length) => Array.CreateInstance(Of<T>(), length);

            private static int Code(Func<int> test)
            {
                try { return test(); }
                catch (InvalidCastException) { return -400; }
                catch (ArrayTypeMismatchException) { return -500; }
                catch (PlatformNotSupportedException) { return -600; }
                catch (NotSupportedException) { return -700; }
                catch (Exception) { return -900; }
            }

            private static int Casts()
            {
                object array = Make<Derived>(2);
                return (array is Derived[] ? 1 : 0) + (array is Base[] ? 2 : 0) + (array is object[] ? 4 : 0) +
                    (array is IEnumerable<Base> ? 8 : 0) + (array is IList<Derived> ? 16 : 0) +
                    (array is IReadOnlyList<Base> ? 32 : 0) + (array is string[] ? 64 : 0) +
                    (array is IList<string> ? 128 : 0) + (array is IEnumerable<int> ? 256 : 0) +
                    (array is Array ? 512 : 0) + (array is IList ? 1024 : 0) +
                    (array is ICollection<IComparable<Base>> ? 2048 : 0) + (array is Derived[,] ? 4096 : 0);
            }

            private static int FailedCast() => ((string[])(object)Make<Derived>(1)).Length;

            // The one other IEnumerable<Base> the program allocates is a List<Base>: the array's enumeration is not the
            // list's.
            private static int Enumeration()
            {
                IEnumerable<Base> list = new List<Base> { new Base { Value = 5 } };
                var array = (Base[])Make<Base>(3);
                array[0] = new Base { Value = 1 };
                array[1] = new Derived { Value = 2 };
                array[2] = new Base { Value = 3 };
                var sum = 0;
                foreach (var sequence in new[] { list, (IEnumerable<Base>)array })
                    foreach (var item in sequence) sum = sum * 10 + item.Value;
                return sum;
            }

            private static int CovariantStore()
            {
                Base[] array = (Base[])Make<Derived>(2);
                array[0] = new Derived { Value = 7 };
                return array[0].Value + (array.GetValue(0) == array[0] ? 100 : 0);
            }

            private static int WrongStore()
            {
                var array = (object[])Make<Derived>(1);
                array[0] = new Other();
                return 1;
            }

            private static int InterfaceMembers()
            {
                var list = (IList<Base>)Make<Derived>(3);
                var item = new Derived { Value = 9 };
                list[1] = item;
                var wrong = Code(() => { list[0] = new Base(); return 0; });
                return list.IndexOf(item) * 1000 + (list.Contains(item) ? 100 : 0) + list.Count * 10 + (wrong == -500 ? 1 : 0);
            }

            // Span<T> takes an array whose type is exactly T[].
            private static int Spans()
            {
                var span = new Span<Base>((Base[])Make<Base>(3));
                var mismatched = Code(() => new Span<Base>((Base[])Make<Derived>(1)).Length);
                return span.Length * 1000 - mismatched;
            }

            private static int Copies()
            {
                var shared = (Base[])Make<Base>(3);
                shared[0] = new Base { Value = 1 };
                shared[1] = new Derived { Value = 2 };
                var typed = new Base[3];
                Array.Copy(shared, typed, 3);
                var back = (Base[])Make<Base>(3);
                Array.Copy(typed, back, 3);
                var objects = new object[3];
                Array.Copy(shared, objects, 3);
                var derived = (Base[])Make<Derived>(2);
                Array.Copy(new[] { new Derived { Value = 4 } }, derived, 1);
                return typed[1].Value * 1000 + back[0].Value * 100 + (objects[1] is Derived ? 10 : 0) +
                    (back[2] is null ? 1 : 0) + derived[0].Value * 10000;
            }

            private static int NarrowingCopy()
            {
                var shared = (Base[])Make<Base>(2);
                shared[0] = new Base();
                Array.Copy(shared, (Derived[])Make<Derived>(2), 2);
                return 1;
            }

            private static int Clones()
            {
                var array = (Base[])Make<Base>(2);
                var clone = (Base[])array.Clone();
                return (clone.GetType() == array.GetType() ? 1 : 0) + clone.Length * 10 + (clone is Base[] ? 100 : 0) +
                    (ReferenceEquals(clone, array) ? 1000 : 0);
            }

            private static int Linq()
            {
                var array = (Base[])Make<Base>(2);
                array[0] = new Base { Value = 3 };
                array[1] = new Base { Value = 4 };
                var list = array.ToList();
                var copied = ((IEnumerable<Base>)array).ToArray();
                var added = new List<Base>();
                added.AddRange(array);
                return list.Count * 10000 + copied[1].Value * 1000 + added.Count * 100 + array.Select(item => item.Value).Sum();
            }

            // The program's own Base[] is typeof(Base[]); an element it names no array of answers one Type object.
            private static int Types()
            {
                var first = Make<Base>(1);
                var lone = Make<Lone>(1);
                var other = Make<Lone>(3);
                var type = lone.GetType();
                return (first.GetType() == typeof(Base[]) ? 1 : 0) + (first.GetType() == Make<Base>(2).GetType() ? 2 : 0) +
                    (type == other.GetType() ? 4 : 0) + (type.GetElementType() == Of<Lone>() ? 8 : 0) +
                    (type.IsArray ? 16 : 0) + (type.IsSZArray ? 32 : 0) + (type.GetArrayRank() == 1 ? 64 : 0) +
                    (typeof(object[]).IsAssignableFrom(type) ? 128 : 0) +
                    (typeof(IEnumerable<Lone>).IsAssignableFrom(type) ? 256 : 0) +
                    (typeof(string[]).IsAssignableFrom(type) ? 512 : 0) + (type.BaseType == typeof(Array) ? 1024 : 0) +
                    (Of<Lone>().MakeArrayType() == type ? 2048 : 0) + (type.IsInstanceOfType(other) ? 4096 : 0) +
                    (typeof(Base[]).IsAssignableFrom(Make<Derived>(1).GetType()) ? 8192 : 0) +
                    (type.IsAssignableFrom(typeof(Lone[])) ? 16384 : 0);
            }

            private static int Kinds()
            {
                var shapes = (IShape[])Make<IShape>(1);
                shapes[0] = new Square();
                var jagged = (Base[][])Make<Base[]>(1);
                jagged[0] = new Base[2];
                var delegates = (Func<int>[])Make<Func<int>>(1);
                delegates[0] = () => 7;
                var lists = (List<int>[])Make<List<int>>(1);
                lists[0] = new List<int> { 1, 2 };
                return shapes[0].Sides + jagged[0].Length * 10 + delegates[0]() * 100 + lists[0].Count * 1000 +
                    (jagged.GetType() == typeof(Base[][]) ? 10000 : 0) + (jagged is object[][] ? 20000 : 0);
            }

            private static int NoArray() =>
                Code(() => Array.CreateInstance(typeof(List<>), 1).Length) * 100 +
                Code(() => Array.CreateInstance(typeof(void), 1).Length);

            private static int Ranked()
            {
                var grid = Array.CreateInstance(Of<Grid>(), 2, 3);
                var item = new Grid();
                grid.SetValue(item, 1, 2);
                return grid.Rank * 10000 + grid.Length * 100 + (grid is Grid[,] ? 1 : 0) + (grid is object[,] ? 2 : 0) +
                    (grid.GetType().GetElementType() == Of<Grid>() ? 4 : 0) + (grid.GetValue(1, 2) == item ? 8 : 0) +
                    (grid is Grid[] ? 16 : 0) + (grid.GetType().GetArrayRank() == 2 ? 32 : 0);
            }

            public static int[] Results() =>
            [
                Code(Casts),
                Code(FailedCast),
                Code(Enumeration),
                Code(CovariantStore),
                Code(WrongStore),
                Code(InterfaceMembers),
                Code(Spans),
                Code(Copies),
                Code(NarrowingCopy),
                Code(Clones),
                Code(Linq),
                Code(Types),
                Code(Kinds),
                Code(NoArray),
                Code(Ranked),
            ];

            // Each case's result on its own line.
            public static int Print(int input)
            {
                foreach (var result in Results()) Console.WriteLine(result);
                return input;
            }

            // Every case's result, folded into one value.
            public static int Run(int input)
            {
                var digest = unchecked((int)2166136261);
                foreach (var result in Results()) digest = unchecked((digest ^ result) * 16777619);
                return digest ^ input;
            }
        }
        """;
}
