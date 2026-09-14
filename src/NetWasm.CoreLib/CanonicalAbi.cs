namespace System.Runtime.InteropServices.WebAssembly
{
    public readonly struct CanonicalBuffer : IDisposable
    {
        public CanonicalBuffer(nuint address, nuint length)
        {
            Address = address;
            Length = length;
        }

        public nuint Address { get; }
        public nuint Length { get; }

        public void Dispose()
        {
            if (Length != 0)
            {
                CanonicalAbi.Free(Address);
            }
        }
    }

    public static unsafe class CanonicalAbi
    {
        public static uint CreateResourceHandle(object value) => 0;

        public static object? GetResourceHandle(uint handle) => null;

        public static void ReleaseResourceHandle(uint handle) { }

        public static nuint Reallocate(
            nuint oldAddress,
            nuint oldSize,
            nuint alignment,
            nuint newSize) => 0;

        public static void Free(nuint address) { }

        public static nuint Allocate(nuint size, nuint alignment)
        {
            var address = Reallocate(0, 0, alignment, size);
            if (size != 0 && address == 0)
            {
                throw new OutOfMemoryException();
            }
            return address;
        }

        public static CanonicalBuffer AllocateElements(
            nuint length,
            nuint elementSize,
            nuint alignment)
        {
            if (elementSize != 0 && length > nuint.MaxValue / elementSize)
            {
                throw new OutOfMemoryException();
            }
            return new CanonicalBuffer(
                Allocate(length * elementSize, alignment),
                length);
        }

        public static bool LiftBoolean(int value) => value switch
        {
            0 => false,
            1 => true,
            _ => throw new ArgumentException(),
        };

        public static uint LiftCharacter(int value)
        {
            var scalar = (uint)value;
            if (scalar > 0x10ffff || scalar >= 0xd800 && scalar <= 0xdfff)
            {
                throw new ArgumentException();
            }
            return scalar;
        }

        public static int LiftDiscriminant(int value, int caseCount)
        {
            if ((uint)value >= (uint)caseCount)
            {
                throw new ArgumentException();
            }
            return value;
        }

        public static uint LiftFlagsWord(uint value, int usedBitCount)
        {
            if (usedBitCount is < 0 or > 32)
            {
                throw new ArgumentOutOfRangeException();
            }
            if (usedBitCount != 32 && value >> usedBitCount != 0)
            {
                throw new ArgumentException();
            }
            return value;
        }

        public static int SingleToInt32Bits(float value) => *(int*)&value;

        public static float Int32BitsToSingle(int value) => *(float*)&value;

        public static long DoubleToInt64Bits(double value) => *(long*)&value;

        public static double Int64BitsToDouble(long value) => *(double*)&value;

        public static CanonicalBuffer LowerString(string value)
        {
            if (value == null)
            {
                throw new ArgumentNullException();
            }
            var byteCount = Utf8ByteCount(value);
            var address = Allocate((nuint)byteCount, 1);
            var output = (byte*)address;
            var outputIndex = 0;
            for (var index = 0; index < value.Length; index++)
            {
                var scalar = (uint)value[index];
                if (scalar >= 0xd800 && scalar <= 0xdbff)
                {
                    scalar = 0x10000 + ((scalar - 0xd800) << 10) +
                        ((uint)value[++index] - 0xdc00);
                }
                if (scalar <= 0x7f)
                {
                    output[outputIndex++] = (byte)scalar;
                }
                else if (scalar <= 0x7ff)
                {
                    output[outputIndex++] = (byte)(0xc0 | scalar >> 6);
                    output[outputIndex++] = (byte)(0x80 | scalar & 0x3f);
                }
                else if (scalar <= 0xffff)
                {
                    output[outputIndex++] = (byte)(0xe0 | scalar >> 12);
                    output[outputIndex++] = (byte)(0x80 | scalar >> 6 & 0x3f);
                    output[outputIndex++] = (byte)(0x80 | scalar & 0x3f);
                }
                else
                {
                    output[outputIndex++] = (byte)(0xf0 | scalar >> 18);
                    output[outputIndex++] = (byte)(0x80 | scalar >> 12 & 0x3f);
                    output[outputIndex++] = (byte)(0x80 | scalar >> 6 & 0x3f);
                    output[outputIndex++] = (byte)(0x80 | scalar & 0x3f);
                }
            }
            return new CanonicalBuffer(address, (nuint)byteCount);
        }

        public static string LiftString(nuint address, nuint length)
        {
            if (length == 0)
            {
                return string.Empty;
            }
            if (address == 0 || length > int.MaxValue)
            {
                throw new ArgumentException();
            }
            var input = (byte*)address;
            var byteLength = (int)length;
            var characterCount = CountUtf16Characters(input, byteLength);
            var characters = new char[characterCount];
            var inputIndex = 0;
            var outputIndex = 0;
            while (inputIndex < byteLength)
            {
                var scalar = DecodeScalar(input, byteLength, ref inputIndex);
                if (scalar <= 0xffff)
                {
                    characters[outputIndex++] = (char)scalar;
                }
                else
                {
                    scalar -= 0x10000;
                    characters[outputIndex++] = (char)(0xd800 + (scalar >> 10));
                    characters[outputIndex++] = (char)(0xdc00 + (scalar & 0x3ff));
                }
            }
            return string.Create(characters);
        }

        public static byte ReadByte(nuint address, nuint offset) =>
            *((byte*)address + offset);

        public static void WriteByte(nuint address, nuint offset, byte value) =>
            *((byte*)address + offset) = value;

        public static ushort ReadUInt16(nuint address, nuint offset) =>
            *(ushort*)((byte*)address + offset);

        public static void WriteUInt16(nuint address, nuint offset, ushort value) =>
            *(ushort*)((byte*)address + offset) = value;

        public static int ReadInt32(nuint address, nuint offset) =>
            *(int*)((byte*)address + offset);

        public static void WriteInt32(nuint address, nuint offset, int value) =>
            *(int*)((byte*)address + offset) = value;

        public static long ReadInt64(nuint address, nuint offset) =>
            *(long*)((byte*)address + offset);

        public static void WriteInt64(nuint address, nuint offset, long value) =>
            *(long*)((byte*)address + offset) = value;

        public static float ReadSingle(nuint address, nuint offset) =>
            *(float*)((byte*)address + offset);

        public static void WriteSingle(nuint address, nuint offset, float value) =>
            *(float*)((byte*)address + offset) = value;

        public static double ReadDouble(nuint address, nuint offset) =>
            *(double*)((byte*)address + offset);

        public static void WriteDouble(nuint address, nuint offset, double value) =>
            *(double*)((byte*)address + offset) = value;

        public static nuint ReadAddress(nuint address, nuint offset) =>
            *(nuint*)((byte*)address + offset);

        public static void WriteAddress(nuint address, nuint offset, nuint value) =>
            *(nuint*)((byte*)address + offset) = value;

        private static int Utf8ByteCount(string value)
        {
            var count = 0;
            for (var index = 0; index < value.Length; index++)
            {
                var scalar = (uint)value[index];
                if (scalar >= 0xd800 && scalar <= 0xdbff)
                {
                    if (index + 1 >= value.Length || value[index + 1] < 0xdc00 ||
                        value[index + 1] > 0xdfff)
                    {
                        throw new ArgumentException();
                    }
                    index++;
                    count = CheckedAdd(count, 4);
                }
                else if (scalar >= 0xdc00 && scalar <= 0xdfff)
                {
                    throw new ArgumentException();
                }
                else
                {
                    count = CheckedAdd(count, scalar <= 0x7f ? 1 : scalar <= 0x7ff ? 2 : 3);
                }
            }
            return count;
        }

        private static int CheckedAdd(int value, int increment)
        {
            if (value > int.MaxValue - increment)
            {
                throw new OutOfMemoryException();
            }
            return value + increment;
        }

        private static int CountUtf16Characters(byte* input, int length)
        {
            var inputIndex = 0;
            var count = 0;
            while (inputIndex < length)
            {
                var scalar = DecodeScalar(input, length, ref inputIndex);
                count = CheckedAdd(count, scalar <= 0xffff ? 1 : 2);
            }
            return count;
        }

        private static uint DecodeScalar(byte* input, int length, ref int index)
        {
            var first = input[index++];
            if (first <= 0x7f)
            {
                return first;
            }
            if (first >= 0xc2 && first <= 0xdf)
            {
                RequireRemaining(length, index, 1);
                return (uint)((first & 0x1f) << 6 | Continuation(input[index++]));
            }
            if (first >= 0xe0 && first <= 0xef)
            {
                RequireRemaining(length, index, 2);
                var second = input[index++];
                if (!IsContinuation(second) || first == 0xe0 && second < 0xa0 ||
                    first == 0xed && second > 0x9f)
                {
                    throw new ArgumentException();
                }
                return (uint)((first & 0x0f) << 12 | (second & 0x3f) << 6 |
                    Continuation(input[index++]));
            }
            if (first >= 0xf0 && first <= 0xf4)
            {
                RequireRemaining(length, index, 3);
                var second = input[index++];
                if (!IsContinuation(second) || first == 0xf0 && second < 0x90 ||
                    first == 0xf4 && second > 0x8f)
                {
                    throw new ArgumentException();
                }
                return (uint)((first & 0x07) << 18 | (second & 0x3f) << 12 |
                    Continuation(input[index++]) << 6 | Continuation(input[index++]));
            }
            throw new ArgumentException();
        }

        private static void RequireRemaining(int length, int index, int count)
        {
            if (index > length - count)
            {
                throw new ArgumentException();
            }
        }

        private static int Continuation(byte value)
        {
            if (!IsContinuation(value))
            {
                throw new ArgumentException();
            }
            return value & 0x3f;
        }

        private static bool IsContinuation(byte value) => (value & 0xc0) == 0x80;
    }
}
