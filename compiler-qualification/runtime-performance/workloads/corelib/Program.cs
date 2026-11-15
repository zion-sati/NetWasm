// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

// One timed loop per CoreLib hot path. Each scenario prints
// CORELIB|name|ticks|frequency|checksum; the checksum must not change between
// compilers, so a faster wrong answer fails the comparison. Scenarios are
// NoInlining so each stays its own function.
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length != 2 || !int.TryParse(args[1], out var count) || count <= 0) return 2;
        Func<int, (long Ticks, long Checksum)>? scenario = args[0] switch
        {
            "short-copies" => Scenarios.ShortCopies,
            "integer-formatting" => Scenarios.IntegerFormatting,
            _ => null,
        };
        if (scenario is null) return 3;
        scenario(count);
        var (ticks, checksum) = scenario(count);
        Console.WriteLine(string.Join("|", "CORELIB", args[0], ticks.ToString(),
            Stopwatch.Frequency.ToString(), checksum.ToString()));
        return 0;
    }
}

internal static class Scenarios
{
    private static readonly string[] Words =
        ["a", "id", "row", "node", "label", "button", "visible", "selected"];

    // Short string concatenation and short span copies, the copies that string
    // building does most often: every length from 0 to 16 chars.
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static (long, long) ShortCopies(int repetitions)
    {
        var words = Words;
        var source = new char[32];
        for (var index = 0; index < source.Length; index++)
            source[index] = (char)('a' + index % 26);
        var destination = new char[48];
        long checksum = 0;
        var start = Stopwatch.GetTimestamp();
        for (var repetition = 0; repetition < repetitions; repetition++)
        {
            for (var index = 0; index < words.Length; index++)
            {
                var text = string.Concat(words[index], "-", words[(index + repetition) & 7]);
                checksum += text.Length + text[^1];
                var length = (index * 3 + repetition) % 17;
                var offset = index & 15;
                source.AsSpan(0, length).CopyTo(destination.AsSpan(offset));
                checksum += destination[offset + (length >> 1)];
            }
            checksum ^= repetition;
        }
        return (Stopwatch.GetTimestamp() - start, checksum);
    }

    // Default integer formatting at every digit count, to strings and into spans.
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static (long, long) IntegerFormatting(int repetitions)
    {
        Span<char> buffer = stackalloc char[24];
        long checksum = 0;
        var start = Stopwatch.GetTimestamp();
        for (var repetition = 0; repetition < repetitions; repetition++)
        {
            var value = (uint)repetition * 2_654_435_761u;
            for (var digits = 0; digits < 10; digits++)
            {
                var text = value.ToString();
                checksum += text.Length + text[^1];
                if (((int)value).TryFormat(buffer, out var written))
                    checksum += written + buffer[written - 1];
                checksum += ((long)value * -7).ToString().Length;
                value /= 10;
            }
            checksum ^= repetition;
        }
        return (Stopwatch.GetTimestamp() - start, checksum);
    }
}
