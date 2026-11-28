// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Tests.Correctness;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// Array.CreateInstance, CreateInstanceFromArrayType and MakeArrayType answer as desktop .NET 11 answers for an element
// type the program never names: one it reads from a type it does name (a generic argument, an element, a base or
// declaring type, an interface, a nullable's underlying type) or from an object. Each case also uses the array as
// dependency injection does: stores, a covariant store, enumeration as the requested sequence, IList<T>, and a store
// of the wrong type. The corpus runner runs the same program against desktop in every lane, GC stress included.
public sealed class ArrayCreationReachCompilationTests(CorrectnessTestRunner runner)
{
    private const string EntryType = "ArrayCreationReach.EntryPoint";

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void EveryElementAProgramCanReadCreatesAsOnDesktop(WasmTarget target)
    {
        var expected = InProcessDesktopResults.Of(Source, "ArrayCreationReach.Desktop", EntryType);
        Assert.Equal(CaseNames.Length, expected.Length);

        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource($"ArrayCreationReach{target}", Source);
        var compilation = NetWasmCompiler.Compile(CompilerTestSupport.CreateReactorOptions(
            assets, assembly, EntryType, "Print", target));
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

    // Every case's result, folded into Run's return value.
    [Theory]
    [InlineData("Release-Wasm32-Direct-Linked")]
    [InlineData("Release-Wasm64-Direct-Linked")]
    public void TheCorpusRunnerSeesDesktopsResults(string cell) => runner.Run(
        new CorpusFixture("ArrayCreationReach", "ArrayCreationReach", Source, [0])
        {
            CaseId = "array.runtime-creation.reach",
            RequiredRuntimeCapabilities = OracleRuntimeCapabilities.GarbageCollection,
            Matrix = new(CorpusMatrixProfile.Extended, cell, CorpusExecutionBackend.Linked),
            ExposesLegacyTrace = false,
            OracleMode = OracleMode.SameSource,
            SameSourceReason = "Compare runtime-typed array creation against the desktop runtime.",
            Optimization = FinalWasmOptimization.None,
        });

    // A program that makes array types but never creates arrays from a Type: MakeArrayType answers for an element it
    // reads from a type it names, as desktop does.
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void MakeArrayTypeAnswersWithoutArrayCreation(WasmTarget target)
    {
        var expected = InProcessDesktopResults.Of(MadeSource, "ArrayTypeReach.Desktop", EntryType);
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource($"ArrayTypeReach{target}", MadeSource);
        var compilation = NetWasmCompiler.Compile(CompilerTestSupport.CreateReactorOptions(
            assets, assembly, EntryType, "Print", target));
        var actual = CompilerTestSupport.ExecuteWithStandardWasiNodeCapturingOutput(
                compilation.ApplicationModule, assets.Directory, 0, target, compilation.StaticDataEnd)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(int.Parse)
            .ToArray();
        Assert.Equal(expected, actual);
    }

    private const string MadeSource = """
        #nullable disable
        using System;
        using System.Collections.Generic;

        namespace ArrayCreationReach;

        public sealed class Vector { }
        public sealed class Grid { }
        public sealed class Bounded { }

        public static class EntryPoint
        {
            private static Type Argument<TSequence>() => typeof(TSequence).GenericTypeArguments[0];

            // Whether it is an array, a vector, its rank, and whether its element is the one asked for.
            private static int Made<TSequence>(int rank)
            {
                var element = Argument<TSequence>();
                var made = rank == 0 ? element.MakeArrayType() : element.MakeArrayType(rank);
                return (made.IsArray ? 1000 : 0) + (made.IsSZArray ? 100 : 0) + made.GetArrayRank() * 10 +
                    (made.GetElementType() == element ? 1 : 0);
            }

            private static int Code(Func<int> test)
            {
                try { return test(); }
                catch (PlatformNotSupportedException) { return -600; }
                catch (NotSupportedException) { return -700; }
                catch (Exception) { return -900; }
            }

            public static int[] Results() =>
            [
                Code(() => Made<IEnumerable<Vector>>(0)),
                Code(() => Made<IEnumerable<Grid>>(2)),
                Code(() => Made<IEnumerable<Bounded>>(1)),
            ];

            public static int Print(int input)
            {
                foreach (var result in Results()) Console.WriteLine(result);
                return input;
            }
        }
        """;

    private static readonly string[] CaseNames =
    [
        "sequence of a generic interface, two stores",
        "sequence of a non-generic interface",
        "empty sequence of a type nothing implements",
        "sequence of a struct",
        "sequence of int",
        "sequence of int[] (jagged)",
        "store of the wrong type",
        "covariant store through object[]",
        "covariant store of the wrong type",
        "IList<T> over the array",
        "element of an array type",
        "base type",
        "declaring type",
        "nullable's underlying type",
        "interface of a type",
        "interfaces a base type declares",
        "an object's type",
        "rectangular, rank 2",
        "rectangular, rank 3",
        "lower bound on rank 1",
        "MakeArrayType of a generic argument",
        "MakeArrayType(2) of a generic argument",
        "FromArrayType, vector",
        "FromArrayType, rank 2",
        "FromArrayType, lower bounds",
        "FromArrayType, not an array",
        "FromArrayType, rank 2 with one length",
        "FromArrayType, null",
        "FromArrayType, negative length",
        "FromArrayType, lengths of the wrong rank",
        "FromArrayType, a negative length in lengths",
        "FromArrayType, non-zero lower bound on a vector",
        "FromArrayType, lengths and bounds of different counts",
    ];

    private const string Source = """
        #nullable disable
        using System;
        using System.Collections;
        using System.Collections.Generic;

        namespace ArrayCreationReach;

        public interface IHandler<T> { int Id { get; } }
        public interface IListener { }
        public sealed class Happened { }
        public sealed class Unseen { }
        public sealed class HandlerA : IHandler<Happened>, IListener { public int Id => 1; }
        public sealed class HandlerB : IHandler<Happened> { public int Id => 2; }
        public struct Point { public int X; }
        public sealed class Stranger { }
        public sealed class Listed { }
        public sealed class FromElement { }
        public class BaseOnly { }
        public sealed class DerivedOne : BaseOnly { }
        public sealed class Outer { public sealed class Inner { } }
        public struct Wrapped { public int Value; }
        public interface IContract<T> { }
        public sealed class Payload { }
        public class Contracted : IContract<Payload> { }
        public sealed class Inheriting : Contracted { }
        public sealed class Held { }
        public sealed class Grid { }
        public sealed class Cube { }
        public sealed class Bounded { }
        public sealed class Made { }
        public sealed class MadeGrid { }
        public sealed class Vector { }
        public sealed class Plane { }
        public sealed class Ranged { }

        public static class EntryPoint
        {
            // The rank, length, and whether the array's element type is the one asked for.
            private static int Shape(Array array, Type element) =>
                array.Rank * 100_000 + array.Length * 100 + (array.GetType().GetElementType() == element ? 1 : 0);

            private static int Parameter(ArgumentException error) => error.ParamName switch
            {
                null => 0,
                "arrayType" => 1,
                "length" => 2,
                "lengths" => 3,
                "lengths[0]" => 4,
                "lowerBounds" => 5,
                _ => 9,
            };

            private static int Code(Func<int> test)
            {
                try { return test(); }
                catch (ArgumentNullException error) { return -100 - Parameter(error); }
                catch (ArgumentOutOfRangeException error) { return -200 - Parameter(error); }
                catch (ArgumentException error) { return -300 - Parameter(error); }
                catch (InvalidCastException) { return -400; }
                catch (ArrayTypeMismatchException) { return -500; }
                catch (PlatformNotSupportedException) { return -600; }
                catch (NotSupportedException) { return -700; }
                catch (IndexOutOfRangeException) { return -800; }
                catch (Exception) { return -900; }
            }

            // As a container resolves IEnumerable<T>: the element type from the requested type's argument, the items
            // stored one by one, the array returned as the request and enumerated.
            private static int Sequence<TSequence>(params object[] items)
            {
                var element = typeof(TSequence).GenericTypeArguments[0];
                var array = Array.CreateInstance(element, items.Length);
                for (var index = 0; index < items.Length; index++) array.SetValue(items[index], index);
                var count = 0;
                foreach (var item in (IEnumerable)(TSequence)(object)array) count += item is null ? 0 : 1;
                return Shape(array, element) + count * 10_000_000;
            }

            private static int Covariant<TSequence>(object item)
            {
                var array = Array.CreateInstance(typeof(TSequence).GenericTypeArguments[0], 1);
                ((object[])array)[0] = item;
                return array.GetValue(0) == item ? 1 : 2;
            }

            private static int List<T>(T item) where T : class
            {
                var array = Array.CreateInstance(typeof(IEnumerable<T>).GenericTypeArguments[0], 2);
                var list = (IList<T>)array;
                list[1] = item;
                return (list[1] == item ? 1 : 2) + list.Count * 10 + (list.IndexOf(item) == 1 ? 100 : 0);
            }

            private static int Created(Type element) => Shape(Array.CreateInstance(element, 2), element);

            private static int ElementOf<T>() => Created(typeof(T).GetElementType());
            private static int BaseOf<T>() => Created(typeof(T).BaseType);
            private static int DeclaringOf<T>() => Created(typeof(T).DeclaringType);
            private static int UnderlyingOf<T>() => Created(Nullable.GetUnderlyingType(typeof(T)));
            private static int InterfaceOf<T>() => Created(typeof(T).GetInterfaces()[0]);
            private static int InterfacesOf<T>()
            {
                var interfaces = typeof(T).GetInterfaces();
                return interfaces.Length * 1_000_000 + Created(interfaces[0]);
            }
            private static int ObjectOf(object value) => Created(value.GetType());
            private static Type Argument<TSequence>() => typeof(TSequence).GenericTypeArguments[0];

            private static int Rectangular<TSequence>(int rank)
            {
                var element = Argument<TSequence>();
                var array = rank == 2 ? Array.CreateInstance(element, 2, 3) : Array.CreateInstance(element, 2, 3, 4);
                return Shape(array, element);
            }

            private static int LowerBound<TSequence>()
            {
                var element = Argument<TSequence>();
                var array = Array.CreateInstance(element, new[] { 2 }, new[] { 1 });
                return Shape(array, element) + array.GetLowerBound(0) * 10;
            }

            private static int Made<TSequence>(int rank)
            {
                var element = Argument<TSequence>();
                var made = rank == 1 ? element.MakeArrayType() : element.MakeArrayType(rank);
                var array = rank == 1
                    ? Array.CreateInstanceFromArrayType(made, 3)
                    : Array.CreateInstanceFromArrayType(made, 2, 2);
                return Shape(array, element) + (made.GetElementType() == element ? 10 : 0) +
                    (array.GetType() == made ? 20 : 0);
            }

            private static int FromArrayType<TArray>(params int[] lengths)
            {
                var array = lengths.Length == 1
                    ? Array.CreateInstanceFromArrayType(typeof(TArray), lengths[0])
                    : Array.CreateInstanceFromArrayType(typeof(TArray), lengths);
                return Shape(array, typeof(TArray).GetElementType()) + (array.GetType() == typeof(TArray) ? 10 : 0);
            }

            public static int[] Results() =>
            [
                Code(() => Sequence<IEnumerable<IHandler<Happened>>>(new HandlerA(), new HandlerB())),
                Code(() => Sequence<IEnumerable<IListener>>(new HandlerA())),
                Code(() => Sequence<IEnumerable<IHandler<Unseen>>>()),
                Code(() => Sequence<IEnumerable<Point>>(new Point { X = 1 })),
                Code(() => Sequence<IEnumerable<int>>(1, 2)),
                Code(() => Sequence<IEnumerable<int[]>>(new int[1])),
                Code(() => Sequence<IEnumerable<IHandler<Happened>>>(new Stranger())),
                Code(() => Covariant<IEnumerable<IListener>>(new HandlerA())),
                Code(() => Covariant<IEnumerable<IListener>>(new Stranger())),
                Code(() => List(new Listed())),
                Code(() => ElementOf<FromElement[]>()),
                Code(() => BaseOf<DerivedOne>()),
                Code(() => DeclaringOf<Outer.Inner>()),
                Code(() => UnderlyingOf<Wrapped?>()),
                Code(() => InterfaceOf<Contracted>()),
                Code(() => InterfacesOf<Inheriting>()),
                Code(() => ObjectOf(new Held())),
                Code(() => Rectangular<IEnumerable<Grid>>(2)),
                Code(() => Rectangular<IEnumerable<Cube>>(3)),
                Code(() => LowerBound<IEnumerable<Bounded>>()),
                Code(() => Made<IEnumerable<Made>>(1)),
                Code(() => Made<IEnumerable<MadeGrid>>(2)),
                Code(() => FromArrayType<Vector[]>(3)),
                Code(() => FromArrayType<Plane[,]>(2, 3)),
                Code(() => Array.CreateInstanceFromArrayType(typeof(Ranged[]), new[] { 2 }, new[] { 0 }).Length),
                Code(() => Array.CreateInstanceFromArrayType(typeof(int), 1).Length),
                Code(() => Array.CreateInstanceFromArrayType(typeof(int[,]), 1).Length),
                Code(() => Array.CreateInstanceFromArrayType(null, 1).Length),
                Code(() => Array.CreateInstanceFromArrayType(typeof(int[]), -1).Length),
                Code(() => Array.CreateInstanceFromArrayType(typeof(int[,]), 1, 2, 3).Length),
                Code(() => Array.CreateInstanceFromArrayType(typeof(int[,]), 1, -2).Length),
                Code(() => Array.CreateInstanceFromArrayType(typeof(int[]), new[] { 2 }, new[] { 1 }).Length),
                Code(() => Array.CreateInstanceFromArrayType(typeof(int[]), new[] { 2 }, new[] { 0, 0 }).Length),
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
