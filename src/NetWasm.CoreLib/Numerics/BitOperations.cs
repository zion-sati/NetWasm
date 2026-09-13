// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Portable scalar implementation adapted from System.Private.CoreLib at
// runtime commit 811225a482702af7ecc35d817966bc70b88a3a23.  NetWasm keeps
// the managed algorithms and intentionally does not select hardware SIMD.

using System.Runtime.CompilerServices;

namespace System.Numerics
{
    /// <summary>Provides scalar bit-manipulation operations.</summary>
    public static class BitOperations
    {
        public static bool IsPow2(int value) => value > 0 && (value & (value - 1)) == 0;

        public static bool IsPow2(uint value) => value != 0 && (value & (value - 1)) == 0;

        public static bool IsPow2(long value) => value > 0 && (value & (value - 1)) == 0;

        public static bool IsPow2(ulong value) => value != 0 && (value & (value - 1)) == 0;

        public static bool IsPow2(nint value) => value > 0 && (value & (value - 1)) == 0;

        public static bool IsPow2(nuint value) => value != 0 && (value & (value - 1)) == 0;

        public static uint RoundUpToPowerOf2(uint value)
        {
            if (value == 0) return 0;
            value--;
            value |= value >> 1;
            value |= value >> 2;
            value |= value >> 4;
            value |= value >> 8;
            value |= value >> 16;
            return value + 1;
        }

        public static ulong RoundUpToPowerOf2(ulong value)
        {
            if (value == 0) return 0;
            value--;
            value |= value >> 1;
            value |= value >> 2;
            value |= value >> 4;
            value |= value >> 8;
            value |= value >> 16;
            value |= value >> 32;
            return value + 1;
        }

        public static nuint RoundUpToPowerOf2(nuint value) => (nuint)RoundUpToPowerOf2((ulong)value);

        public static int LeadingZeroCount(uint value)
        {
            if (value == 0) return 32;
            var count = 0;
            for (var bit = 31; (value & (1u << bit)) == 0; bit--) count++;
            return count;
        }

        public static int LeadingZeroCount(ulong value)
        {
            if (value == 0) return 64;
            var count = 0;
            for (var bit = 63; (value & (1ul << bit)) == 0; bit--) count++;
            return count;
        }

        public static int LeadingZeroCount(nuint value) => LeadingZeroCount((ulong)value);

        public static int Log2(uint value)
        {
            if (value == 0) return 0;
            var result = 0;
            while ((value >>= 1) != 0) result++;
            return result;
        }

        public static int Log2(ulong value)
        {
            if (value == 0) return 0;
            var result = 0;
            while ((value >>= 1) != 0) result++;
            return result;
        }

        public static int Log2(nuint value) => Log2((ulong)value);

        public static int PopCount(uint value)
        {
            var count = 0;
            while (value != 0)
            {
                value &= value - 1;
                count++;
            }
            return count;
        }

        public static int PopCount(ulong value)
        {
            var count = 0;
            while (value != 0)
            {
                value &= value - 1;
                count++;
            }
            return count;
        }

        public static int PopCount(nuint value) => PopCount((ulong)value);

        public static int TrailingZeroCount(int value) => TrailingZeroCount((uint)value);

        public static int TrailingZeroCount(uint value)
        {
            if (value == 0) return 32;
            var count = 0;
            while ((value & 1) == 0)
            {
                value >>= 1;
                count++;
            }
            return count;
        }

        public static int TrailingZeroCount(long value) => TrailingZeroCount((ulong)value);

        public static int TrailingZeroCount(ulong value)
        {
            if (value == 0) return 64;
            var count = 0;
            while ((value & 1) == 0)
            {
                value >>= 1;
                count++;
            }
            return count;
        }

        public static int TrailingZeroCount(nint value) => TrailingZeroCount((nuint)value);

        public static int TrailingZeroCount(nuint value) => TrailingZeroCount((ulong)value);

        public static uint RotateLeft(uint value, int offset) => (value << offset) | (value >> (32 - offset));

        public static ulong RotateLeft(ulong value, int offset) => (value << offset) | (value >> (64 - offset));

        public static nuint RotateLeft(nuint value, int offset) => (nuint)RotateLeft((ulong)value, offset);

        public static uint RotateRight(uint value, int offset) => (value >> offset) | (value << (32 - offset));

        public static ulong RotateRight(ulong value, int offset) => (value >> offset) | (value << (64 - offset));

        public static nuint RotateRight(nuint value, int offset) => (nuint)RotateRight((ulong)value, offset);

        public static uint Crc32C(uint crc, byte data) => Crc32CByte(crc, data);

        public static uint Crc32C(uint crc, ushort data)
        {
            crc = Crc32CByte(crc, (byte)data);
            return Crc32CByte(crc, (byte)(data >> 8));
        }

        public static uint Crc32C(uint crc, uint data)
        {
            for (var index = 0; index < 4; index++)
            {
                crc = Crc32CByte(crc, (byte)data);
                data >>= 8;
            }
            return crc;
        }

        public static uint Crc32C(uint crc, ulong data)
        {
            for (var index = 0; index < 8; index++)
            {
                crc = Crc32CByte(crc, (byte)data);
                data >>= 8;
            }
            return crc;
        }

        private static uint Crc32CByte(uint crc, byte data)
        {
            crc ^= data;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0u : 0x82F63B78u);
            }
            return crc;
        }
    }
}
