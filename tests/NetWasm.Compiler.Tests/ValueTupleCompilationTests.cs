// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class ValueTupleCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerPreservesTupleTailAndStructuralContracts(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        const string source = """
            using System;
            using System.Collections;
            using System.Runtime.CompilerServices;

            namespace TupleContractFixture;

            public sealed class DerivedTuple : Tuple<int>
            {
                public DerivedTuple(int value) : base(value) { }
            }

            public sealed class RecursiveComparer : IComparer, IEqualityComparer
            {
                public int Compare(object? left, object? right)
                {
                    if (object.ReferenceEquals(left, right)) return 0;
                    if (left is null) return -1;
                    if (right is null) return 1;
                    if (left is IStructuralComparable structural)
                    {
                        return structural.CompareTo(right, this);
                    }
                    return ((IComparable)left).CompareTo(right);
                }

                public new bool Equals(object? left, object? right)
                {
                    if (object.ReferenceEquals(left, right)) return true;
                    if (left is null || right is null) return false;
                    return left is IStructuralEquatable structural
                        ? structural.Equals(right, this)
                        : left.Equals(right);
                }

                public int GetHashCode(object value) =>
                    value is IStructuralEquatable structural
                        ? structural.GetHashCode(this)
                        : value.GetHashCode();
            }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    Tuple<int> derived = new DerivedTuple(input);
                    if (derived.Item1 != input) return 1;

                    var single = Tuple.Create(input);
                    single.Deconstruct(out int singleItem);
                    if (singleItem != input || single.ToValueTuple().Item1 != input)
                    {
                        return 9;
                    }

                    var seven = Tuple.Create(1, 2, 3, 4, 5, 6, input);
                    seven.Deconstruct(
                        out int first,
                        out int second,
                        out int third,
                        out int fourth,
                        out int fifth,
                        out int sixth,
                        out int seventh);
                    var sevenValue = seven.ToValueTuple();
                    var sevenRoundTrip = sevenValue.ToTuple();
                    if (first != 1 || second != 2 || third != 3 || fourth != 4 ||
                        fifth != 5 || sixth != 6 || seventh != input ||
                        sevenValue.Item7 != input || sevenRoundTrip.Item7 != input)
                    {
                        return 10;
                    }

                    var reference = new Tuple<int, int, int, int, int, int, int, Tuple<int, int>>(
                        1, 2, 3, 4, 5, 6, 7, new Tuple<int, int>(8, 9));
                    var referenceCopy = new Tuple<int, int, int, int, int, int, int, Tuple<int, int>>(
                        1, 2, 3, 4, 5, 6, 7, new Tuple<int, int>(8, 9));
                    ITuple referenceContract = reference;
                    if (referenceContract.Length != 9 ||
                        (int)referenceContract[7]! != 8 ||
                        (int)referenceContract[8]! != 9 ||
                        reference.ToString() != "(1, 2, 3, 4, 5, 6, 7, 8, 9)")
                    {
                        return 2;
                    }

                    var value = (1, 2, 3, 4, 5, 6, 7, 8, 9);
                    var valueCopy = (1, 2, 3, 4, 5, 6, 7, 8, 9);
                    ITuple valueContract = value;
                    if (valueContract.Length != 9 ||
                        (int)valueContract[8]! != 9 ||
                        value.ToString() != "(1, 2, 3, 4, 5, 6, 7, 8, 9)")
                    {
                        return 3;
                    }

                    var comparer = new RecursiveComparer();
                    if (!((IStructuralEquatable)reference).Equals(referenceCopy, comparer) ||
                        ((IStructuralComparable)reference).CompareTo(referenceCopy, comparer) != 0 ||
                        ((IStructuralEquatable)reference).GetHashCode(comparer) !=
                            ((IStructuralEquatable)referenceCopy).GetHashCode(comparer) ||
                        !((IStructuralEquatable)value).Equals(valueCopy, comparer) ||
                        ((IStructuralComparable)value).CompareTo(valueCopy, comparer) != 0)
                    {
                        return 4;
                    }

                    var rejectedReferenceTail = false;
                    try
                    {
                        var invalid = new Tuple<int, int, int, int, int, int, int, string>(
                            1, 2, 3, 4, 5, 6, 7, "not a tuple");
                        if (invalid.Rest == "not a tuple") return 7;
                    }
                    catch (ArgumentException)
                    {
                        rejectedReferenceTail = true;
                    }

                    var rejectedValueTail = false;
                    try
                    {
                        var invalid = new ValueTuple<int, int, int, int, int, int, int, int>(
                            1, 2, 3, 4, 5, 6, 7, 8);
                        if (invalid.Rest == 8) return 8;
                    }
                    catch (ArgumentException)
                    {
                        rejectedValueTail = true;
                    }

                    if (!rejectedReferenceTail) return 5;
                    if (!rejectedValueTail) return 6;
                    return input;
                }
            }
            """;

        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            $"TupleContractFixture_{optimize}_{target}",
            source,
            "TupleContractFixture.EntryPoint",
            optimize,
            target,
            42,
            [])));
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerLowersTupleConstructionFieldsAndDeconstruction(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        const string source = """
            namespace ValueTupleFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var named = (Start: 3, Length: input);
                    System.Runtime.CompilerServices.ITuple contract = named;
                    if (named.Length != input || contract.Length != 2 ||
                        (int)contract[1]! != input)
                    {
                        return -1;
                    }
                    var pair = (Left: input, Right: 1);
                    var (left, right) = pair;
                    var equal = (Left: input, Right: 1);
                    var different = (Left: input, Right: 2);
                    if (pair != equal || pair == different ||
                        pair.GetHashCode() != equal.GetHashCode())
                    {
                        return 0;
                    }
                    object boxed = Identity(pair);
                    var unboxed = ((int Left, int Right))boxed;
                    var longTuple = (1, 2, 3, 4, 5, 6, 7, input);
                    return unboxed.Left + right + longTuple.Item8 - input;
                }

                private static T Identity<T>(T value) => value;
            }
            """;

        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "ValueTupleFixture",
            source,
            "ValueTupleFixture.EntryPoint",
            optimize,
            target,
            41,
            [])));
    }
}
