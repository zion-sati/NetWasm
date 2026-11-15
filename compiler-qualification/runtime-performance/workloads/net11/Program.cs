// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

// One timed loop per .NET 11 code generation item. Each scenario prints
// NET11|name|ticks|frequency|checksum; the checksum must not change between
// compilers, so a faster wrong answer fails the comparison. Scenarios are
// NoInlining so each stays its own function: otherwise wasm-opt merges them
// and one scenario's code changes register allocation in the others.
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length != 2 || !int.TryParse(args[1], out var count) || count <= 0) return 2;
        var data = Data.Create(count);
        Func<Data, (long Ticks, long Checksum)>? scenario = args[0] switch
        {
            "conversions" => Scenarios.Conversions,
            "constant-strings" => Scenarios.ConstantStrings,
            "sparse-set" => Scenarios.SparseSet,
            "offset-bounds" => Scenarios.OffsetBounds,
            "from-end" => Scenarios.FromEnd,
            "empty-span-guard" => Scenarios.EmptySpanGuard,
            "implied-branches" => Scenarios.ImpliedBranches,
            "generic-virtual" => Scenarios.GenericVirtual,
            "default-interface" => Scenarios.DefaultInterface,
            "activator-exact" => Scenarios.ActivatorExact,
            _ => null,
        };
        if (scenario is null) return 3;
        scenario(data);
        var (ticks, checksum) = scenario(data);
        Console.WriteLine(string.Join("|", "NET11", args[0], ticks.ToString(),
            Stopwatch.Frequency.ToString(), checksum.ToString()));
        return 0;
    }
}

internal sealed class Data
{
    public required double[] Doubles { get; init; }
    public required int[] Integers { get; init; }
    public required int[][] Arrays { get; init; }
    public required int Repetitions { get; init; }

    public static Data Create(int count)
    {
        var random = 2463534242U;
        uint Next()
        {
            random ^= random << 13;
            random ^= random >> 17;
            random ^= random << 5;
            return random;
        }
        var doubles = new double[4096];
        for (var index = 0; index < doubles.Length; index++)
            doubles[index] = ((int)Next() % 200_000) / 7.0;
        var integers = new int[4096];
        for (var index = 0; index < integers.Length; index++)
            integers[index] = (int)(Next() % 16);
        var arrays = new int[1024][];
        for (var index = 0; index < arrays.Length; index++)
        {
            arrays[index] = new int[(int)(Next() % 6)];
            for (var item = 0; item < arrays[index].Length; item++)
                arrays[index][item] = (int)(Next() % 1000);
        }
        return new() { Doubles = doubles, Integers = integers, Arrays = arrays, Repetitions = count };
    }
}

internal interface IShape
{
    int Sides { get; }
}

internal interface IMeasure<T>
{
    int Measure(T value) => value is null ? 0 : 1;
}

internal sealed class Square : IShape, IMeasure<int>
{
    public int Sides => 4;
}

internal class Visitor
{
    public virtual int Visit<T>(T value) => value is int number ? number & 7 : 1;
}

internal sealed class CountingVisitor : Visitor
{
    public override int Visit<T>(T value) => value is int number ? number & 3 : 2;
}

internal static class Scenarios
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static (long, long) Conversions(Data data)
    {
        var values = data.Doubles;
        long checksum = 0;
        var start = Stopwatch.GetTimestamp();
        for (var repetition = 0; repetition < data.Repetitions; repetition++)
            for (var index = 0; index < values.Length; index++)
            {
                var value = values[index];
                checksum += unchecked((byte)value + (sbyte)value + (short)value + (ushort)value + (char)value);
            }
        return (Stopwatch.GetTimestamp() - start, checksum);
    }

    private static bool IsAdmin(string role) => role == "Admin";

    private static bool IsPng(ReadOnlySpan<byte> header) => header.SequenceEqual("PNG"u8);

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static (long, long) ConstantStrings(Data data)
    {
        long checksum = 0;
        var start = Stopwatch.GetTimestamp();
        for (var repetition = 0; repetition < data.Repetitions * 4096; repetition++)
        {
            checksum += IsAdmin("Guest") ? 3 : 1;
            checksum += IsAdmin("Admin") ? 5 : 2;
            checksum += IsPng("GIF"u8) ? 7 : 1;
            checksum ^= repetition;
        }
        return (Stopwatch.GetTimestamp() - start, checksum);
    }

    private static bool IsOdd(int value) => value is 1 or 3 or 5 or 7 or 9 or 11 or 13 or 15;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static (long, long) SparseSet(Data data)
    {
        var values = data.Integers;
        long checksum = 0;
        var start = Stopwatch.GetTimestamp();
        for (var repetition = 0; repetition < data.Repetitions; repetition++)
            for (var index = 0; index < values.Length; index++)
                checksum += IsOdd(values[index]) ? index : 1;
        return (Stopwatch.GetTimestamp() - start, checksum);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static (long, long) OffsetBounds(Data data)
    {
        var values = data.Integers;
        long checksum = 0;
        var start = Stopwatch.GetTimestamp();
        for (var repetition = 0; repetition < data.Repetitions; repetition++)
            for (var index = 0; index + 1 < values.Length; index++)
                checksum += values[index] * 3 + values[index + 1];
        return (Stopwatch.GetTimestamp() - start, checksum);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static (long, long) FromEnd(Data data)
    {
        var arrays = data.Arrays;
        long checksum = 0;
        var start = Stopwatch.GetTimestamp();
        for (var repetition = 0; repetition < data.Repetitions * 4; repetition++)
            for (var index = 0; index < arrays.Length; index++)
            {
                var values = arrays[index];
                if (values.Length > 0)
                    checksum += values[^1];
            }
        return (Stopwatch.GetTimestamp() - start, checksum);
    }

    private static bool StartsWith(ReadOnlySpan<int> span, int value) => !span.IsEmpty && span[0] == value;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static (long, long) EmptySpanGuard(Data data)
    {
        var arrays = data.Arrays;
        long checksum = 0;
        var start = Stopwatch.GetTimestamp();
        for (var repetition = 0; repetition < data.Repetitions * 4; repetition++)
            for (var index = 0; index < arrays.Length; index++)
                checksum += StartsWith(arrays[index], index & 1023) ? index : 1;
        return (Stopwatch.GetTimestamp() - start, checksum);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Add(long checksum, int value) => (int)(checksum + value) & 0xffff;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static (long, long) ImpliedBranches(Data data)
    {
        var values = data.Integers;
        long checksum = 0;
        var start = Stopwatch.GetTimestamp();
        for (var repetition = 0; repetition < data.Repetitions; repetition++)
            for (var index = 0; index < values.Length; index++)
            {
                var value = values[index];
                if (value > 0)
                {
                    if (value > 1) checksum += value;
                }
                var parity = (value & 1) == 0 ? 1 : 2;
                if (parity == 1) checksum += index;
                else checksum -= 1;
            }
        return (Stopwatch.GetTimestamp() - start, checksum + Add(checksum, values[0]));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static (long, long) GenericVirtual(Data data)
    {
        var values = data.Integers;
        Visitor visitor = new CountingVisitor();
        long checksum = 0;
        var start = Stopwatch.GetTimestamp();
        for (var repetition = 0; repetition < data.Repetitions; repetition++)
            for (var index = 0; index < values.Length; index++)
                checksum += visitor.Visit(values[index]);
        return (Stopwatch.GetTimestamp() - start, checksum);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static (long, long) DefaultInterface(Data data)
    {
        var values = data.Integers;
        IMeasure<int> measure = new Square();
        long checksum = 0;
        var start = Stopwatch.GetTimestamp();
        for (var repetition = 0; repetition < data.Repetitions; repetition++)
            for (var index = 0; index < values.Length; index++)
                checksum += measure.Measure(values[index]) + values[index];
        return (Stopwatch.GetTimestamp() - start, checksum);
    }

    private static int Sides<T>() where T : IShape, new() => Activator.CreateInstance<T>().Sides;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static (long, long) ActivatorExact(Data data)
    {
        long checksum = 0;
        var start = Stopwatch.GetTimestamp();
        for (var repetition = 0; repetition < data.Repetitions * 1024; repetition++)
            checksum += Sides<Square>() + (repetition & 3);
        return (Stopwatch.GetTimestamp() - start, checksum);
    }
}
