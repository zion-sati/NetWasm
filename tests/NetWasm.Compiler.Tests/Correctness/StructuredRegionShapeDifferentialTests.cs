// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Compiler.Tests.Correctness;

// Control flow inside and across protected regions, against desktop .NET at the dev-loop, speed and size levels: every
// reachable block of a method must be emitted and every handler must end where its region does. The shapes are the
// ones that move blocks between a region and its surroundings: joins that never return (throw helpers inlined from
// several sites, inside a handler, across a handler boundary, shared by a protected body and its handler), switch
// tables and switch expressions inside regions, loops whose exits leave a region (break, continue, return, goto), one
// region with several leave targets, regions nested in handlers, filters, and a single try/finally wrapping loops, a
// switch expression, a sort with a lambda, a set and a long else branch, the shape of Avalonia's
// CommandBar.UpdateDynamicOverflow, whose blocks were left unemitted at Oz (public issue 271).
public sealed class StructuredRegionShapeDifferentialTests(CorrectnessTestRunner runner)
{
    public static TheoryData<FinalWasmOptimization, string> Cases()
    {
        var cases = new TheoryData<FinalWasmOptimization, string>();
        foreach (var optimization in (FinalWasmOptimization[])[FinalWasmOptimization.O0, FinalWasmOptimization.O1,
                     FinalWasmOptimization.O3, FinalWasmOptimization.Oz])
        {
            cases.Add(optimization, "Release-Wasm32-Direct-Linked");
            cases.Add(optimization, "Release-Wasm64-Direct-Linked");
        }
        // Roslyn's Debug IL keeps the branches Release folds.
        cases.Add(FinalWasmOptimization.Oz, "Debug-Wasm32-Direct-Linked");
        return cases;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void FinallyRegionsMatchDesktop(FinalWasmOptimization optimization, string cell) =>
        Run("FinallyRegionShapes", FinallySource, "finally", optimization, cell);

    [Theory]
    [MemberData(nameof(Cases))]
    public void CatchRegionsMatchDesktop(FinalWasmOptimization optimization, string cell) =>
        Run("CatchRegionShapes", CatchSource, "catch", optimization, cell);

    [Theory]
    [MemberData(nameof(Cases))]
    public void FilterRegionsMatchDesktop(FinalWasmOptimization optimization, string cell) =>
        Run("FilterRegionShapes", FilterSource, "filter", optimization, cell);

    [Theory]
    [MemberData(nameof(Cases))]
    public void NestedRegionsMatchDesktop(FinalWasmOptimization optimization, string cell) =>
        Run("NestedRegionShapes", NestedSource, "nested", optimization, cell);

    [Theory]
    [MemberData(nameof(Cases))]
    public void TheCommandBarOverflowShapeMatchesDesktop(FinalWasmOptimization optimization, string cell) =>
        Run("OverflowRegionShape", OverflowSource, "overflow", optimization, cell);

    private void Run(string name, string source, string kind, FinalWasmOptimization optimization, string cell) =>
        runner.Run(new CorpusFixture(name, "NetWasm.Correctness." + name, Prelude(name) + source,
            [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11])
        {
            CaseId = "control-flow.region-shapes." + kind,
            Matrix = new(CorpusMatrixProfile.Extended, cell, CorpusExecutionBackend.Linked),
            OracleMode = OracleMode.SameSource,
            SameSourceReason = "Compare every reachable block of methods with protected regions against desktop .NET.",
            Optimization = optimization,
        });

    // Helpers every shape shares: a trace, a non-returning failure and a range check meant to be inlined at several
    // sites, so the optimizer sees one join that never returns, reached from several branches.
    private static string Prelude(string name) => $$"""
        using System;
        using System.Collections.Generic;
        using System.Diagnostics.CodeAnalysis;
        using System.Runtime.CompilerServices;

        namespace NetWasm.Correctness.{{name}};

        public enum Mode { Compact, Wide, Hidden, Overflow }

        public sealed class ShapeException(int code) : Exception("shape " + code) { public int Code { get; } = code; }

        public static class Shape
        {
            public static int Value;
            public static void Mark(int value) => Value = unchecked(Value * 31 + value);

            [DoesNotReturn, MethodImpl(MethodImplOptions.NoInlining)]
            public static void Fail(int code) => throw new ShapeException(code);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static int Checked(int value, int limit)
            {
                if ((uint)value >= (uint)limit) Fail(value);
                return value;
            }
        }

        """;

    private const string FinallySource = """
        public static class EntryPoint
        {
            public static int Trace() => Shape.Value;

            // Span copies inlined into the finally: their range checks share one throw that never returns.
            private static int RestoreInFinally(byte[] data, int input)
            {
                Span<byte> saved = stackalloc byte[16];
                data.AsSpan(4, 8).CopyTo(saved);
                try
                {
                    data.AsSpan(4, 8).Clear();
                    if (input % 5 == 4) Shape.Fail(40);
                    return data[4] + Shape.Checked(input, 64);
                }
                finally
                {
                    saved.Slice(0, 4).CopyTo(data.AsSpan(4));
                    saved.Slice(4, 4).CopyTo(data.AsSpan(8));
                    Shape.Mark(data[4] + data[8]);
                }
            }

            // Loop exits leaving a try/finally: break, continue, return and goto, over IList<T>, List<T> and HashSet<T>.
            private static int LoopExits(IList<int> list, List<int> concrete, HashSet<int> set, int input)
            {
                var total = 0;
                try
                {
                    foreach (var value in list)
                    {
                        if (value == input) break;
                        if (value % 3 == 0) continue;
                        total += Shape.Checked(value, 100);
                    }
                    foreach (var value in concrete)
                    {
                        if (value > input + 6) goto done;
                        total += value;
                    }
                    foreach (var value in set)
                    {
                        if (value == input + 2) return total * 3;
                        total ^= value;
                    }
                }
                finally
                {
                    Shape.Mark(total);
                }
                total += 1000;
            done:
                return total;
            }

            // One region with several leave targets.
            private static int SeveralLeaves(int input)
            {
                var result = 0;
                try
                {
                    switch (input % 6)
                    {
                        case 0: goto first;
                        case 1: goto second;
                        case 2: return 7;
                        case 3: result = Shape.Checked(input, 8); break;
                        case 4: Shape.Fail(41); break;
                        default: goto third;
                    }
                }
                finally
                {
                    Shape.Mark(input);
                }
                return result + 1;
            first:
                return result + 10;
            second:
                return result + 20;
            third:
                return result + 30;
            }

            // A sort with a lambda and a switch expression on an enum inside the finally; a dense switch in the try.
            private static int WorkInFinally(int input)
            {
                var pairs = new List<(int Index, int Order)>();
                try
                {
                    for (var index = 0; index < 6; index++)
                    {
                        pairs.Add((index, (index * 7 + input) % 5));
                        switch (index + input)
                        {
                            case 0: Shape.Mark(1); break;
                            case 1: Shape.Mark(2); break;
                            case 2: Shape.Mark(3); break;
                            case 3: Shape.Mark(4); break;
                            case 4: Shape.Mark(5); break;
                            case 5: Shape.Mark(6); break;
                            case 6: if (input == 6) Shape.Fail(42); break;
                        }
                    }
                }
                finally
                {
                    pairs.Sort((left, right) => left.Order != right.Order
                        ? left.Order.CompareTo(right.Order) : right.Index.CompareTo(left.Index));
                    var mode = (Mode)(input % 4);
                    Shape.Mark(mode switch
                    {
                        Mode.Compact => pairs[0].Index,
                        Mode.Wide => pairs[^1].Index * 2,
                        Mode.Hidden => -1,
                        _ => pairs.Count,
                    });
                }
                return pairs[0].Index * 10 + pairs[1].Index;
            }

            public static int Run(int input)
            {
                Shape.Value = 17;
                var data = new byte[32];
                for (var index = 0; index < data.Length; index++) data[index] = (byte)(index * 3 + input);
                var total = 0;
                int Guard(Func<int> body)
                {
                    try { return body(); }
                    catch (ShapeException exception) { Shape.Mark(exception.Code); return -exception.Code; }
                }
                total += Guard(() => RestoreInFinally(data, input));
                total += data[5] + data[9];
                var values = new List<int> { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };
                total += Guard(() => LoopExits(values, values, new HashSet<int>(values), input));
                total += Guard(() => SeveralLeaves(input));
                total += Guard(() => WorkInFinally(input));
                return unchecked(Shape.Value * 7 + total);
            }
        }
        """;

    private const string CatchSource = """
        public static class EntryPoint
        {
            public static int Trace() => Shape.Value;

            private static Exception Make(int kind) => (kind % 4) switch
            {
                0 => new InvalidOperationException("io"),
                1 => new ArgumentException("arg"),
                2 => new ShapeException(kind),
                _ => new FormatException("format"),
            };

            // Several typed catches; the try and the catches share an inlined check that never returns on failure.
            private static int TypedCatches(int input)
            {
                try
                {
                    if (input % 3 == 0) throw Make(input);
                    return Shape.Checked(input, 10) * 2;
                }
                catch (InvalidOperationException) { return Shape.Checked(input + 1, 12); }
                catch (ShapeException exception) when (exception.Code > 2) { return -exception.Code; }
                catch (ArgumentException) { Shape.Mark(5); goto after; }
                catch (Exception exception)
                {
                    Shape.Mark(exception.Message.Length);
                    switch (input)
                    {
                        case 3: return 30;
                        case 6: return 60;
                        case 9: Shape.Fail(90); break;
                        default: break;
                    }
                }
                return -1;
            after:
                return 99;
            }

            // A catch that loops, rethrows, and leaves through goto; a switch expression on an enum in the catch.
            private static int CatchWithLoopAndRethrow(int input, IList<int> list)
            {
                var total = 0;
                try
                {
                    foreach (var value in list)
                    {
                        if (value == input) throw new ShapeException(value);
                        total += value;
                    }
                }
                catch (ShapeException exception)
                {
                    foreach (var value in list)
                    {
                        if (value > exception.Code + 3) goto leave;
                        total -= value;
                    }
                    var mode = (Mode)(exception.Code % 4);
                    total += mode switch { Mode.Compact => 1, Mode.Wide => 2, Mode.Hidden => throw new ShapeException(exception.Code + 50), _ => 4 };
                }
            leave:
                return total;
            }

            // Span copies inlined into a catch: their range checks share one throw that never returns.
            private static int RestoreInCatch(byte[] data, int input)
            {
                Span<byte> saved = stackalloc byte[16];
                data.AsSpan(4, 8).CopyTo(saved);
                try
                {
                    data.AsSpan(4, 8).Clear();
                    if (input % 2 == 1) throw new ShapeException(input);
                    return data[4] + input;
                }
                catch (ShapeException exception)
                {
                    saved.Slice(0, 4).CopyTo(data.AsSpan(4));
                    saved.Slice(4, 4).CopyTo(data.AsSpan(exception.Code % 3 == 0 ? 8 : 30));
                    return -data[4];
                }
            }

            public static int Run(int input)
            {
                Shape.Value = 23;
                var bytes = new byte[32];
                for (var index = 0; index < bytes.Length; index++) bytes[index] = (byte)(index + input);
                try { Shape.Mark(RestoreInCatch(bytes, input)); }
                catch (ArgumentException) { Shape.Mark(77); }
                Shape.Mark(bytes[4] + bytes[8]);
                var total = 0;
                for (var step = 0; step < 4; step++)
                {
                    try { total += TypedCatches(input + step); }
                    catch (ShapeException exception) { Shape.Mark(exception.Code); }
                    try { total += CatchWithLoopAndRethrow(input + step, new List<int> { 2, 4, 6, 8, 10, 12, 14 }); }
                    catch (ShapeException exception) { Shape.Mark(-exception.Code); }
                }
                return unchecked(Shape.Value * 7 + total);
            }
        }
        """;

    private const string FilterSource = """
        public static class EntryPoint
        {
            public static int Trace() => Shape.Value;

            private static bool Accept(int value, Mode mode)
            {
                Shape.Mark(value);
                return mode switch { Mode.Compact => value % 2 == 0, Mode.Wide => true, _ => false };
            }

            // Filters with switch expressions; loops and leaves inside the filtered body; a catch inside it.
            private static int Filtered(int input, List<int> values)
            {
                var total = 0;
                try
                {
                    foreach (var value in values)
                    {
                        try
                        {
                            if (value == input) throw new ShapeException(value);
                            total += Shape.Checked(value, 50);
                        }
                        catch (ShapeException inner) when (inner.Code % 3 == 0)
                        {
                            total += 100;
                            continue;
                        }
                        if (value > input + 4) break;
                    }
                    if (input % 4 == 1) throw new ShapeException(input * 2);
                }
                catch (ShapeException exception) when (Accept(exception.Code, (Mode)(input % 4)))
                {
                    switch (exception.Code % 5)
                    {
                        case 0: return -10;
                        case 1: return -11;
                        case 2: goto tail;
                        default: total -= exception.Code; break;
                    }
                }
                catch (ShapeException exception) when (exception.Code > 3) { return -exception.Code; }
            tail:
                return total;
            }

            // Span reads inlined into a when clause; out of range for large codes, where desktop takes the filter's own
            // exception as false and moves on to the next clause.
            private static int SpanFilter(byte[] data, int input)
            {
                try
                {
                    throw new ShapeException(input * 5);
                }
                catch (ShapeException exception) when (data.AsSpan(exception.Code, 4)[0] % 2 == 0
                    && data.AsSpan(exception.Code % 8, 2).IndexOf((byte)3) < 1)
                {
                    return exception.Code;
                }
                catch (ShapeException exception)
                {
                    return -exception.Code;
                }
            }

            public static int Run(int input)
            {
                Shape.Value = 29;
                var bytes = new byte[32];
                for (var index = 0; index < bytes.Length; index++) bytes[index] = (byte)(index * 3);
                Shape.Mark(SpanFilter(bytes, input));
                var total = 0;
                var values = new List<int> { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
                for (var step = 0; step < 3; step++)
                {
                    try { total += Filtered(input + step, values); }
                    catch (ShapeException exception) { Shape.Mark(exception.Code + 1000); }
                }
                return unchecked(Shape.Value * 7 + total);
            }
        }
        """;

    private const string NestedSource = """
        public static class EntryPoint
        {
            public static int Trace() => Shape.Value;

            // A try inside a finally, a finally inside a catch, a catch inside a filtered body, with leaves that cross
            // several regions at once.
            private static int Nested(int input)
            {
                var total = 0;
                try
                {
                    try
                    {
                        if (input % 3 == 0) throw new ShapeException(input);
                        total += Shape.Checked(input, 20);
                    }
                    finally
                    {
                        try
                        {
                            if (input % 4 == 1) throw new InvalidOperationException();
                            Shape.Mark(1);
                        }
                        catch (InvalidOperationException) { Shape.Mark(2); }
                        finally { Shape.Mark(3); }
                    }
                }
                catch (ShapeException exception) when (exception.Code != 9)
                {
                    try
                    {
                        if (exception.Code == 6) goto outer;
                        total -= exception.Code;
                    }
                    finally
                    {
                        Shape.Mark(exception.Code);
                    }
                }
                catch (ShapeException)
                {
                    try { Shape.Fail(9); }
                    catch (ShapeException again) when (again.Code == 9) { return -9; }
                }
                return total;
            outer:
                return 600;
            }

            public static int Run(int input)
            {
                Shape.Value = 31;
                var total = 0;
                for (var step = 0; step < 4; step++)
                {
                    try { total += Nested(input + step); }
                    catch (Exception exception) { Shape.Mark(exception.Message.Length); }
                }
                return unchecked(Shape.Value * 7 + total);
            }
        }
        """;

    // CommandBar.UpdateDynamicOverflow's shape: one try/finally around foreach loops over IList<T>, a switch expression
    // on an enum property, a sort of index/order pairs with a lambda, a set of indices, and nested if/else with a long
    // else branch that computes the overflow.
    private const string OverflowSource = """
        public interface IElement
        {
            bool IsVisible { get; }
            double Width { get; }
            int Order { get; }
            bool InOverflow { get; set; }
        }

        public sealed class Element(bool visible, double width, int order) : IElement
        {
            public bool IsVisible { get; } = visible;
            public double Width { get; } = width;
            public int Order { get; } = order;
            public bool InOverflow { get; set; }
        }

        public sealed class Bar(IList<IElement> primary, IList<IElement> secondary, Mode mode)
        {
            private bool _updating;
            public Mode Mode { get; } = mode;
            public int Moved { get; private set; }

            public void UpdateOverflow(double available)
            {
                if (_updating) return;
                _updating = true;
                try
                {
                    double used = 0;
                    var visible = 0;
                    foreach (var element in primary)
                    {
                        if (!element.IsVisible) continue;
                        used += element.Width;
                        visible++;
                    }
                    var limit = Mode switch
                    {
                        Mode.Compact => available * 0.5,
                        Mode.Wide => available * 2,
                        Mode.Hidden => 0,
                        _ => available,
                    };
                    if (used <= limit)
                    {
                        foreach (var element in secondary)
                        {
                            if (element.InOverflow && used + element.Width <= limit)
                            {
                                element.InOverflow = false;
                                used += element.Width;
                                Moved--;
                            }
                        }
                    }
                    else if (visible == 0)
                    {
                        Moved = -100;
                    }
                    else
                    {
                        var candidates = new List<(int Index, int Order)>();
                        for (var index = 0; index < primary.Count; index++)
                        {
                            var element = primary[index];
                            if (element.IsVisible && !element.InOverflow) candidates.Add((index, element.Order));
                        }
                        candidates.Sort((left, right) => left.Order == right.Order
                            ? right.Index.CompareTo(left.Index) : right.Order.CompareTo(left.Order));
                        var moved = new HashSet<int>();
                        foreach (var (index, _) in candidates)
                        {
                            if (used <= limit) break;
                            var element = primary[index];
                            if (element.Width <= 0) continue;
                            element.InOverflow = true;
                            used -= element.Width;
                            if (!moved.Add(index)) throw new ShapeException(index);
                            Moved++;
                        }
                        if (used > limit)
                        {
                            foreach (var element in secondary)
                            {
                                if (!element.InOverflow)
                                {
                                    element.InOverflow = true;
                                    Moved += 10;
                                }
                            }
                        }
                        else if (moved.Count > 2)
                        {
                            Moved += moved.Count * 100;
                        }
                    }
                }
                finally
                {
                    _updating = false;
                }
            }
        }

        public static class EntryPoint
        {
            public static int Trace() => Shape.Value;

            public static int Run(int input)
            {
                Shape.Value = 37;
                var primary = new List<IElement>();
                var secondary = new List<IElement>();
                for (var index = 0; index < 8; index++)
                {
                    primary.Add(new Element(index % 3 != input % 3, 10 + (index * input) % 7, (index * 5 + input) % 4));
                    secondary.Add(new Element(true, 5 + index, index) { InOverflow = index % 2 == 0 });
                }
                var bar = new Bar(primary, secondary, (Mode)(input % 4));
                var total = 0;
                foreach (var available in (double[])[20, 45, 80, 150])
                {
                    bar.UpdateOverflow(available);
                    total = unchecked(total * 31 + bar.Moved);
                    foreach (var element in primary) total += element.InOverflow ? 1 : 0;
                }
                return unchecked(Shape.Value * 7 + total);
            }
        }
        """;
}
