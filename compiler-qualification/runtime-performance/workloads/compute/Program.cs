// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

// Computation with no library work and no allocation. Each scenario prints
// COMPUTE|name|ticks|frequency|checksum, then GC|collections|allocated|total for
// the timed region. The program fails if that region allocates or collects, and
// each checksum is exact (the bits of a double for n-body), so a runtime that
// takes a shortcut, or reorders or fuses floating-point operations, no longer
// matches desktop .NET.
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length != 2 || !int.TryParse(args[1], out var count) || count <= 0) return 2;
        IKernel? kernel = args[0] switch
        {
            "nbody" => new NBody(count),
            "sort" => new Sorting(count),
            "crc32" => new Crc32(count),
            _ => null,
        };
        if (kernel is null) return 3;

        // Run the timed work untimed first, so a tiered JIT reaches its optimized code.
        for (var warmup = 0; warmup < 3; warmup++)
        {
            kernel.Reset();
            kernel.Run();
        }
        kernel.Reset();
        _ = Stopwatch.GetTimestamp();
        _ = GC.CollectionCount(0);
        _ = GC.GetTotalAllocatedBytes(true);

        var collectionsBefore = GC.CollectionCount(0);
        var allocatedBefore = GC.GetTotalAllocatedBytes(true);
        var start = Stopwatch.GetTimestamp();
        kernel.Run();
        var elapsed = Stopwatch.GetTimestamp() - start;
        var allocated = GC.GetTotalAllocatedBytes(true) - allocatedBefore;
        var collections = GC.CollectionCount(0) - collectionsBefore;

        Console.WriteLine(string.Join("|", "COMPUTE", args[0],
            elapsed.ToString(), Stopwatch.Frequency.ToString(), kernel.Checksum().ToString()));
        Console.WriteLine(string.Join("|", "GC", collections.ToString(),
            allocated.ToString(), GC.GetTotalMemory(false).ToString()));
        if (allocatedBefore <= 0) return 6;
        if (allocated != 0 || collections != 0) return 4;
        return 0;
    }
}

internal interface IKernel
{
    // Restores the kernel's input; not timed.
    void Reset();

    // The timed work.
    void Run();

    long Checksum();
}

internal struct Body
{
    public double X, Y, Z, Vx, Vy, Vz, Mass;
}

// The Computer Language Benchmarks Game n-body simulation: the Sun and the four
// gas giants, advanced with a fixed time step. Floating-point arithmetic only.
internal sealed class NBody(int steps) : IKernel
{
    private const double SolarMass = 4 * Math.PI * Math.PI;
    private const double DaysPerYear = 365.24;
    private const double TimeStep = 0.01;

    private readonly Body[] _initial = CreateSystem();
    private readonly Body[] _bodies = new Body[5];

    public void Reset() => Array.Copy(_initial, _bodies, _initial.Length);

    public void Run() => Advance(_bodies, steps);

    public long Checksum() => BitConverter.DoubleToInt64Bits(Energy(_bodies));

    private static Body[] CreateSystem()
    {
        Body[] bodies =
        [
            new() { Mass = SolarMass },
            Planet(4.84143144246472090e+00, -1.16032004402742839e+00, -1.03622044471123109e-01,
                1.66007664274403694e-03, 7.69901118419740425e-03, -6.90460016972063023e-05,
                9.54791938424326609e-04),
            Planet(8.34336671824457987e+00, 4.12479856412430479e+00, -4.03523417114321381e-01,
                -2.76742510726862411e-03, 4.99852801234917238e-03, 2.30417297573763929e-05,
                2.85885980666130812e-04),
            Planet(1.28943695621391310e+01, -1.51111514016986312e+01, -2.23307578892655734e-01,
                2.96460137564761618e-03, 2.37847173959480950e-03, -2.96589568540237556e-05,
                4.36624404335156298e-05),
            Planet(1.53796971148509165e+01, -2.59193146099879641e+01, 1.79258772950371181e-01,
                2.68067772490389322e-03, 1.62824170038242295e-03, -9.51592254519715870e-05,
                5.15138902046611451e-05),
        ];

        // Give the Sun the momentum that makes the system's total zero.
        double px = 0, py = 0, pz = 0;
        foreach (var body in bodies)
        {
            px += body.Vx * body.Mass;
            py += body.Vy * body.Mass;
            pz += body.Vz * body.Mass;
        }
        bodies[0].Vx = -px / SolarMass;
        bodies[0].Vy = -py / SolarMass;
        bodies[0].Vz = -pz / SolarMass;
        return bodies;
    }

    private static Body Planet(double x, double y, double z, double vx, double vy, double vz, double mass) =>
        new()
        {
            X = x, Y = y, Z = z,
            Vx = vx * DaysPerYear, Vy = vy * DaysPerYear, Vz = vz * DaysPerYear,
            Mass = mass * SolarMass,
        };

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Advance(Body[] bodies, int steps)
    {
        for (var step = 0; step < steps; step++)
        {
            for (var i = 0; i < bodies.Length; i++)
            {
                ref var a = ref bodies[i];
                for (var j = i + 1; j < bodies.Length; j++)
                {
                    ref var b = ref bodies[j];
                    var dx = a.X - b.X;
                    var dy = a.Y - b.Y;
                    var dz = a.Z - b.Z;
                    var distanceSquared = dx * dx + dy * dy + dz * dz;
                    var magnitude = TimeStep / (distanceSquared * Math.Sqrt(distanceSquared));
                    var aScale = a.Mass * magnitude;
                    var bScale = b.Mass * magnitude;
                    a.Vx -= dx * bScale;
                    a.Vy -= dy * bScale;
                    a.Vz -= dz * bScale;
                    b.Vx += dx * aScale;
                    b.Vy += dy * aScale;
                    b.Vz += dz * aScale;
                }
            }
            for (var i = 0; i < bodies.Length; i++)
            {
                ref var body = ref bodies[i];
                body.X += TimeStep * body.Vx;
                body.Y += TimeStep * body.Vy;
                body.Z += TimeStep * body.Vz;
            }
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static double Energy(Body[] bodies)
    {
        var energy = 0.0;
        for (var i = 0; i < bodies.Length; i++)
        {
            ref var a = ref bodies[i];
            energy += 0.5 * a.Mass * (a.Vx * a.Vx + a.Vy * a.Vy + a.Vz * a.Vz);
            for (var j = i + 1; j < bodies.Length; j++)
            {
                ref var b = ref bodies[j];
                var dx = a.X - b.X;
                var dy = a.Y - b.Y;
                var dz = a.Z - b.Z;
                energy -= a.Mass * b.Mass / Math.Sqrt(dx * dx + dy * dy + dz * dz);
            }
        }
        return energy;
    }
}

// An in-place quicksort of 100,000 integers, refilled from the same seed data
// every round: branchy integer code over dependent array accesses.
internal sealed class Sorting(int rounds) : IKernel
{
    private const int Length = 100_000;

    private readonly int[] _source = Seed();
    private readonly int[] _values = new int[Length];
    private long _checksum;

    public void Reset() => _checksum = 0;

    public long Checksum() => _checksum;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Run()
    {
        var source = _source;
        var values = _values;
        var checksum = 0L;
        for (var round = 0; round < rounds; round++)
        {
            // Each round sorts different values, so no round repeats another's work.
            for (var i = 0; i < values.Length; i++) values[i] = source[i] ^ round;
            Sort(values, 0, values.Length - 1);
            for (var i = 0; i < values.Length; i++) checksum = checksum * 31 + values[i];
        }
        _checksum = checksum;
    }

    private static int[] Seed()
    {
        var values = new int[Length];
        var state = 0x9E3779B9u;
        for (var i = 0; i < values.Length; i++)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            values[i] = (int)state;
        }
        return values;
    }

    private static void Sort(int[] values, int low, int high)
    {
        // Recurse into the smaller side and loop over the larger, so depth stays logarithmic.
        while (high - low >= 16)
        {
            var middle = low + (high - low) / 2;
            if (values[middle] < values[low]) Swap(values, middle, low);
            if (values[high] < values[low]) Swap(values, high, low);
            if (values[high] < values[middle]) Swap(values, high, middle);
            var pivot = values[middle];
            var i = low;
            var j = high;
            while (i <= j)
            {
                while (values[i] < pivot) i++;
                while (values[j] > pivot) j--;
                if (i <= j)
                {
                    Swap(values, i, j);
                    i++;
                    j--;
                }
            }
            if (j - low < high - i)
            {
                Sort(values, low, j);
                low = i;
            }
            else
            {
                Sort(values, i, high);
                high = j;
            }
        }
        for (var i = low + 1; i <= high; i++)
        {
            var value = values[i];
            var j = i - 1;
            while (j >= low && values[j] > value)
            {
                values[j + 1] = values[j];
                j--;
            }
            values[j + 1] = value;
        }
    }

    private static void Swap(int[] values, int a, int b) => (values[a], values[b]) = (values[b], values[a]);
}

// Table-driven CRC-32 (IEEE 802.3) over a 1 MiB buffer, each pass seeded by the
// last: shifts, exclusive ors and table loads in one loop-carried chain.
internal sealed class Crc32(int passes) : IKernel
{
    private readonly uint[] _table = CreateTable();
    private readonly byte[] _data = Seed();
    private uint _crc;

    public void Reset() => _crc = 0;

    public long Checksum() => _crc;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Run()
    {
        var table = _table;
        var data = _data;
        var crc = 0u;
        for (var pass = 0; pass < passes; pass++)
        {
            var value = ~crc;
            for (var i = 0; i < data.Length; i++)
                value = table[(value ^ data[i]) & 0xFF] ^ (value >> 8);
            crc = ~value;
        }
        _crc = crc;
    }

    private static uint[] CreateTable()
    {
        var table = new uint[256];
        for (var i = 0u; i < table.Length; i++)
        {
            var value = i;
            for (var bit = 0; bit < 8; bit++)
                value = (value & 1) != 0 ? 0xEDB88320u ^ (value >> 1) : value >> 1;
            table[i] = value;
        }
        return table;
    }

    private static byte[] Seed()
    {
        var data = new byte[1 << 20];
        var state = 0x2545F491u;
        for (var i = 0; i < data.Length; i++)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            data[i] = (byte)state;
        }
        return data;
    }
}
