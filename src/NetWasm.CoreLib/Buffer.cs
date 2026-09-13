// Portions derived from dotnet/runtime System.Private.CoreLib.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System
{
    public static partial class Buffer
    {
        public static void BlockCopy(Array src, int srcOffset, Array dst, int dstOffset, int count)
        {
            if (src is null || dst is null) throw new ArgumentNullException();
            if (srcOffset < 0 || dstOffset < 0 || count < 0) throw new ArgumentOutOfRangeException();
            if (srcOffset > ByteLength(src) - count || dstOffset > ByteLength(dst) - count)
            {
                throw new ArgumentException();
            }

            if (ReferenceEquals(src, dst) && dstOffset > srcOffset && dstOffset < srcOffset + count)
            {
                for (var offset = count - 1; offset >= 0; offset--)
                {
                    SetByte(dst, dstOffset + offset, GetByte(src, srcOffset + offset));
                }
                return;
            }

            for (var offset = 0; offset < count; offset++)
            {
                SetByte(dst, dstOffset + offset, GetByte(src, srcOffset + offset));
            }
        }

        public static int ByteLength(Array array)
        {
            if (array is null) throw new ArgumentNullException();
            if (array is byte[] bytes) return bytes.Length;
            if (array is sbyte[] sbytes) return sbytes.Length;
            if (array is bool[] booleans) return booleans.Length;
            if (array is char[] chars) return checked(chars.Length * 2);
            if (array is short[] shorts) return checked(shorts.Length * 2);
            if (array is ushort[] ushorts) return checked(ushorts.Length * 2);
            if (array is int[] ints) return checked(ints.Length * 4);
            if (array is uint[] uints) return checked(uints.Length * 4);
            if (array is float[] floats) return checked(floats.Length * 4);
            if (array is long[] longs) return checked(longs.Length * 8);
            if (array is ulong[] ulongs) return checked(ulongs.Length * 8);
            if (array is double[] doubles) return checked(doubles.Length * 8);
            throw new ArgumentException();
        }

        public static byte GetByte(Array array, int index)
        {
            if ((uint)index >= (uint)ByteLength(array)) throw new ArgumentOutOfRangeException();
            return ReadByte(array, index);
        }

        public static void SetByte(Array array, int index, byte value)
        {
            if ((uint)index >= (uint)ByteLength(array)) throw new ArgumentOutOfRangeException();
            WriteByte(array, index, value);
        }

        public static unsafe void MemoryCopy(
            void* source,
            void* destination,
            long destinationSizeInBytes,
            long sourceBytesToCopy)
        {
            if (destinationSizeInBytes < 0 || sourceBytesToCopy < 0 ||
                sourceBytesToCopy > destinationSizeInBytes)
            {
                throw new ArgumentOutOfRangeException();
            }

            var count = ToLength(sourceBytesToCopy);
            new Span<byte>(source, count).CopyTo(new Span<byte>(destination, count));
        }

        public static unsafe void MemoryCopy(
            void* source,
            void* destination,
            ulong destinationSizeInBytes,
            ulong sourceBytesToCopy)
        {
            if (sourceBytesToCopy > destinationSizeInBytes)
            {
                throw new ArgumentOutOfRangeException();
            }

            var count = ToLength(sourceBytesToCopy);
            new Span<byte>(source, count).CopyTo(new Span<byte>(destination, count));
        }

        private static byte ReadByte(Array array, int index)
        {
            if (array is byte[] bytes) return bytes[index];
            if (array is sbyte[] sbytes) return unchecked((byte)sbytes[index]);
            if (array is bool[] booleans) return booleans[index] ? (byte)1 : (byte)0;
            if (array is char[] chars) return (byte)(chars[index / 2] >> ((index & 1) * 8));
            if (array is short[] shorts) return (byte)(shorts[index / 2] >> ((index & 1) * 8));
            if (array is ushort[] ushorts) return (byte)(ushorts[index / 2] >> ((index & 1) * 8));
            if (array is int[] ints) return (byte)(ints[index / 4] >> ((index & 3) * 8));
            if (array is uint[] uints) return (byte)(uints[index / 4] >> ((index & 3) * 8));
            if (array is long[] longs) return (byte)(longs[index / 8] >> ((index & 7) * 8));
            if (array is ulong[] ulongs) return (byte)(ulongs[index / 8] >> ((index & 7) * 8));
            throw new PlatformNotSupportedException();
        }

        private static void WriteByte(Array array, int index, byte value)
        {
            if (array is byte[] bytes) { bytes[index] = value; return; }
            if (array is sbyte[] sbytes) { sbytes[index] = unchecked((sbyte)value); return; }
            if (array is bool[] booleans) { booleans[index] = value != 0; return; }
            if (array is char[] chars) { chars[index / 2] = (char)ReplaceByte(chars[index / 2], index, value, 2); return; }
            if (array is short[] shorts) { shorts[index / 2] = (short)ReplaceByte(unchecked((ulong)shorts[index / 2]), index, value, 2); return; }
            if (array is ushort[] ushorts) { ushorts[index / 2] = (ushort)ReplaceByte(ushorts[index / 2], index, value, 2); return; }
            if (array is int[] ints) { ints[index / 4] = (int)ReplaceByte(unchecked((ulong)ints[index / 4]), index, value, 4); return; }
            if (array is uint[] uints) { uints[index / 4] = (uint)ReplaceByte(uints[index / 4], index, value, 4); return; }
            if (array is long[] longs) { longs[index / 8] = (long)ReplaceByte(unchecked((ulong)longs[index / 8]), index, value, 8); return; }
            if (array is ulong[] ulongs) { ulongs[index / 8] = ReplaceByte(ulongs[index / 8], index, value, 8); return; }
            throw new PlatformNotSupportedException();
        }

        private static ulong ReplaceByte(ulong current, int byteIndex, byte value, int size)
        {
            var shift = (byteIndex % size) * 8;
            var mask = 0xffUL << shift;
            return (current & ~mask) | ((ulong)value << shift);
        }

        private static int ToLength(long value)
        {
            var result = (int)value;
            return value == result ? result : throw new ArgumentOutOfRangeException();
        }

        private static int ToLength(ulong value)
        {
            var result = (int)value;
            return (ulong)result == value ? result : throw new ArgumentOutOfRangeException();
        }
    }
}
