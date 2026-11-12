using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace NetWasm.Compiler.Layout;

// An append-only stream of bit fields, least significant bit first, whose fields can be patched
// once later fields fix their values.
internal sealed class RuntimeTypeTableBitWriter
{
    private readonly List<byte> _bytes = [];

    internal long Length { get; private set; }

    internal void Write(ulong value, int width)
    {
        CheckField(value, width);
        for (var bit = 0; bit < width; bit++, Length++)
        {
            if ((Length & 7) == 0)
            {
                _bytes.Add(0);
            }
            if ((value >> bit & 1) != 0)
            {
                _bytes[(int)(Length >> 3)] |= (byte)(1 << (int)(Length & 7));
            }
        }
    }

    internal void Patch(long offset, ulong value, int width)
    {
        CheckField(value, width);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(offset + width, Length);
        for (var bit = 0; bit < width; bit++)
        {
            var position = offset + bit;
            var mask = (byte)(1 << (int)(position & 7));
            if ((value >> bit & 1) != 0)
            {
                _bytes[(int)(position >> 3)] |= mask;
            }
            else
            {
                _bytes[(int)(position >> 3)] &= (byte)~mask;
            }
        }
    }

    // The stream's bytes and zero padding, so a reader may load the 8 bytes holding any field.
    internal ImmutableArray<byte> ToBytes(int padding)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(padding);
        var bytes = ImmutableArray.CreateBuilder<byte>(_bytes.Count + padding);
        bytes.AddRange(_bytes);
        bytes.AddRange(new byte[padding]);
        return bytes.MoveToImmutable();
    }

    // The number of bits that holds every value up to maximum.
    internal static int Width(long maximum)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximum);
        return maximum == 0 ? 0 : 64 - System.Numerics.BitOperations.LeadingZeroCount((ulong)maximum);
    }

    private static void CheckField(ulong value, int width)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(width, 32);
        if (width < 64 && value >> width != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), $"{value} does not fit in {width} bits.");
        }
    }
}
