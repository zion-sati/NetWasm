// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime System.Private.CoreLib BitArray.cs at commit
// 474ed963699fc29809015392738a15231aaae2de.

using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
#if FEATURE_LEGACY_FORMATTER
using System.Runtime.Serialization;
#endif

namespace System.Collections
{
    /// <summary>
    /// Manages a compact array of bit values, which are represented as <see cref="bool"/>.
    /// </summary>
    [Serializable]
    [TypeForwardedFrom("mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089")]
    public sealed class BitArray : ICollection, ICloneable
#if FEATURE_LEGACY_FORMATTER
        , ISerializable
#endif
    {
        private const int BitsPerInt32 = 32;
        private const int BitsPerByte = 8;

        internal byte[] _array;
        private int _bitLength;
        private int _version;

        public BitArray(int length)
            : this(length, false)
        {
        }

        public BitArray(int length, bool defaultValue)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(length);

            _array = AllocateByteArray(length);
            _bitLength = length;

            if (defaultValue)
            {
                Array.Fill(_array, (byte)0xFF);
                ClearHighExtraBits();
            }
        }

#if FEATURE_LEGACY_FORMATTER
        private BitArray(SerializationInfo info, StreamingContext context)
        {
            ArgumentNullException.ThrowIfNull(info);

            var array = (int[]?)info.GetValue("m_array", typeof(int[]));
            _bitLength = info.GetInt32("m_length");
            _version = info.GetInt32("_version");

            if (array is null || (uint)_bitLength > checked((uint)array.Length * BitsPerInt32))
            {
                throw new SerializationException(SR.Serialization_InvalidData);
            }

            _array = AllocateByteArray(_bitLength);
            if (BitConverter.IsLittleEndian)
            {
                MemoryMarshal.AsBytes(array).CopyTo(_array);
            }
            else
            {
                BinaryPrimitives.ReverseEndianness(
                    array,
                    MemoryMarshal.Cast<byte, int>((Span<byte>)_array));
            }

            ClearHighExtraBits();
        }

        void ISerializable.GetObjectData(SerializationInfo info, StreamingContext context)
        {
            ArgumentNullException.ThrowIfNull(info);

            var array = new int[GetInt32ArrayLengthFromBitLength(_bitLength)];
            CopyTo(array, 0);
            info.AddValue("m_array", array);
            info.AddValue("m_length", _bitLength);
            info.AddValue("_version", _version);
        }
#endif

        private void ClearHighExtraBits()
        {
            (uint index, uint extraBits) = Math.DivRem((uint)_bitLength, BitsPerInt32);
            if (extraBits != 0)
            {
                MemoryMarshal.Cast<byte, int>((Span<byte>)_array)[(int)index] &=
                    ReverseIfBE((1 << (int)extraBits) - 1);
            }
        }

        public BitArray(byte[] bytes)
        {
            ArgumentNullException.ThrowIfNull(bytes);
            _array = CreateArray(bytes, out _bitLength);
        }

        public BitArray(ReadOnlySpan<byte> bytes)
        {
            _array = CreateArray(bytes, out _bitLength);
        }

        private static byte[] CreateArray(ReadOnlySpan<byte> bytes, out int bitLength)
        {
            if (bytes.Length > int.MaxValue / BitsPerByte)
            {
                throw new ArgumentException(
                    SR.Format(SR.Argument_ArrayTooLarge, BitsPerByte),
                    nameof(bytes));
            }

            bitLength = bytes.Length * BitsPerByte;
            byte[] array = AllocateByteArray(bitLength);
            bytes.CopyTo(array);
            return array;
        }

        public BitArray(bool[] values)
        {
            ArgumentNullException.ThrowIfNull(values);
            _array = CreateArray(values, out _bitLength);
        }

        public BitArray(ReadOnlySpan<bool> values)
        {
            _array = CreateArray(values, out _bitLength);
        }

        private static byte[] CreateArray(ReadOnlySpan<bool> values, out int bitLength)
        {
            bitLength = values.Length;
            byte[] array = AllocateByteArray(bitLength);

            for (uint index = 0; index < (uint)values.Length; index++)
            {
                if (values[(int)index])
                {
                    (uint byteIndex, uint bitOffset) = Math.DivRem(index, BitsPerByte);
                    array[byteIndex] |= (byte)(1 << (int)bitOffset);
                }
            }

            return array;
        }

        public BitArray(int[] values)
        {
            ArgumentNullException.ThrowIfNull(values);
            _array = CreateArray(values, out _bitLength);
        }

        public BitArray(ReadOnlySpan<int> values)
        {
            _array = CreateArray(values, out _bitLength);
        }

        private static byte[] CreateArray(ReadOnlySpan<int> values, out int bitLength)
        {
            if (values.Length > int.MaxValue / BitsPerInt32)
            {
                throw new ArgumentException(
                    SR.Format(SR.Argument_ArrayTooLarge, BitsPerInt32),
                    nameof(values));
            }

            bitLength = values.Length * BitsPerInt32;
            byte[] array = AllocateByteArray(bitLength);

            if (BitConverter.IsLittleEndian)
            {
                MemoryMarshal.AsBytes(values).CopyTo(array);
            }
            else
            {
                BinaryPrimitives.ReverseEndianness(
                    values,
                    MemoryMarshal.Cast<byte, int>((Span<byte>)array));
            }

            return array;
        }

        public BitArray(BitArray bits)
        {
            ArgumentNullException.ThrowIfNull(bits);

            _bitLength = bits._bitLength;
            _array = AllocateByteArray(_bitLength);
            Array.Copy(bits._array, _array, _array.Length);
        }

        public bool this[int index]
        {
            get => Get(index);
            set => Set(index, value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Get(int index)
        {
            if ((uint)index >= (uint)_bitLength)
            {
                ThrowArgumentOutOfRangeException(index);
            }

            (uint byteIndex, uint bitOffset) = Math.DivRem((uint)index, BitsPerByte);
            return ((_array[byteIndex]) & (1 << (int)bitOffset)) != 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Set(int index, bool value)
        {
            if ((uint)index >= (uint)_bitLength)
            {
                ThrowArgumentOutOfRangeException(index);
            }

            (uint byteIndex, uint bitOffset) = Math.DivRem((uint)index, BitsPerByte);
            ref byte segment = ref _array[byteIndex];
            byte bitMask = (byte)(1 << (int)bitOffset);
            if (value)
            {
                segment |= bitMask;
            }
            else
            {
                segment &= (byte)~bitMask;
            }

            _version++;
        }

        public void SetAll(bool value)
        {
            if (value)
            {
                _array.AsSpan(0, GetByteArrayLengthFromBitLength(_bitLength)).Fill(0xFF);
                ClearHighExtraBits();
            }
            else
            {
                _array.AsSpan(0, GetByteArrayLengthFromBitLength(_bitLength)).Clear();
            }

            _version++;
        }

        public BitArray And(BitArray value) => Invoke(value, BinaryOperation.And);

        public BitArray Or(BitArray value) => Invoke(value, BinaryOperation.Or);

        public BitArray Xor(BitArray value) => Invoke(value, BinaryOperation.Xor);

        public BitArray Not()
        {
            Invoke(this, BinaryOperation.Not);
            ClearHighExtraBits();
            return this;
        }

        private BitArray Invoke(BitArray value, BinaryOperation operation)
        {
            ArgumentNullException.ThrowIfNull(value);

            byte[] thisArray = _array;
            byte[] valueArray = value._array;
            int count = GetByteArrayLengthFromBitLength(Length);

            if (Length != value.Length ||
                (uint)count > (uint)thisArray.Length ||
                (uint)count > (uint)valueArray.Length)
            {
                throw new ArgumentException(SR.Arg_ArrayLengthsDiffer);
            }

            int intCount = RoundUpToMultipleSizeOfInt32(count) / sizeof(int);
            Span<int> left = MemoryMarshal.Cast<byte, int>((Span<byte>)thisArray);
            ReadOnlySpan<int> right = MemoryMarshal.Cast<byte, int>((ReadOnlySpan<byte>)valueArray);
            for (int index = 0; index < intCount; index++)
            {
                left[index] = operation switch
                {
                    BinaryOperation.And => left[index] & right[index],
                    BinaryOperation.Or => left[index] | right[index],
                    BinaryOperation.Xor => left[index] ^ right[index],
                    _ => ~left[index],
                };
            }

            _version++;
            return this;
        }

        private enum BinaryOperation
        {
            And,
            Or,
            Xor,
            Not,
        }

        public BitArray RightShift(int count)
        {
            if (count <= 0)
            {
                ArgumentOutOfRangeException.ThrowIfNegative(count);

                _version++;
                return this;
            }

            Span<int> intSpan = MemoryMarshal.Cast<byte, int>((Span<byte>)_array);

            int toIndex = 0;
            int ints = GetInt32ArrayLengthFromBitLength(_bitLength);
            if (count < _bitLength)
            {
                (int fromIndex, int shiftCount) = Math.DivRem(count, 32);
                int extraBits = (int)((uint)_bitLength % 32);
                if (shiftCount == 0)
                {
                    uint mask = uint.MaxValue >> (BitsPerInt32 - extraBits);
                    intSpan[ints - 1] &= ReverseIfBE((int)mask);

                    intSpan.Slice((int)fromIndex, ints - fromIndex).CopyTo(intSpan);
                    toIndex = ints - fromIndex;
                }
                else
                {
                    int lastIndex = ints - 1;

                    while (fromIndex < lastIndex)
                    {
                        uint right = (uint)ReverseIfBE(intSpan[fromIndex]) >> shiftCount;
                        int left = ReverseIfBE(intSpan[++fromIndex]) << (BitsPerInt32 - shiftCount);
                        intSpan[toIndex++] = ReverseIfBE(left | (int)right);
                    }

                    uint mask = uint.MaxValue >> (BitsPerInt32 - extraBits);
                    mask &= (uint)ReverseIfBE(intSpan[fromIndex]);
                    intSpan[toIndex++] = ReverseIfBE((int)(mask >> shiftCount));
                }
            }

            intSpan.Slice(toIndex, ints - toIndex).Clear();
            _version++;
            return this;
        }

        public BitArray LeftShift(int count)
        {
            if (count <= 0)
            {
                ArgumentOutOfRangeException.ThrowIfNegative(count);

                _version++;
                return this;
            }

            Span<int> intSpan = MemoryMarshal.Cast<byte, int>((Span<byte>)_array);

            int lengthToClear;
            if (count < _bitLength)
            {
                int lastIndex = (int)((uint)(_bitLength - 1) / BitsPerInt32);
                (lengthToClear, int shiftCount) = Math.DivRem(count, BitsPerInt32);

                if (shiftCount == 0)
                {
                    intSpan.Slice(0, lastIndex + 1 - lengthToClear)
                        .CopyTo(intSpan.Slice(lengthToClear));
                }
                else
                {
                    int fromindex = lastIndex - lengthToClear;

                    while (fromindex > 0)
                    {
                        int left = ReverseIfBE(intSpan[fromindex]) << shiftCount;
                        uint right = (uint)ReverseIfBE(intSpan[--fromindex]) >>
                            (BitsPerInt32 - shiftCount);
                        intSpan[lastIndex] = ReverseIfBE(left | (int)right);
                        lastIndex--;
                    }
                    intSpan[lastIndex] =
                        ReverseIfBE(ReverseIfBE(intSpan[fromindex]) << shiftCount);
                }

                ClearHighExtraBits();
            }
            else
            {
                lengthToClear = GetInt32ArrayLengthFromBitLength(_bitLength);
            }

            intSpan.Slice(0, lengthToClear).Clear();
            _version++;
            return this;
        }

        public int Length
        {
            get => _bitLength;
            set
            {
                ArgumentOutOfRangeException.ThrowIfNegative(value);

                int newByteLength = GetAlignedByteArrayLength(value);
                if (newByteLength > _array.Length)
                {
                    Array.Resize(ref _array, newByteLength);
                }
                else
                {
                    int currentByteLength = GetByteArrayLengthFromBitLength(_bitLength);
                    if (newByteLength > currentByteLength)
                    {
                        _array.AsSpan(currentByteLength).Clear();
                    }
                    else
                    {
                        const int ShrinkThreshold = 1024;
                        if (newByteLength < _array.Length - ShrinkThreshold)
                        {
                            Array.Resize(ref _array, newByteLength);
                        }
                    }
                }

                _bitLength = value;
                ClearHighExtraBits();
                _version++;
            }
        }

        public void CopyTo(Array array, int index)
        {
            ArgumentNullException.ThrowIfNull(array);
            ArgumentOutOfRangeException.ThrowIfNegative(index);

            if (array.Rank != 1)
            {
                throw new ArgumentException(SR.Arg_RankMultiDimNotSupported, nameof(array));
            }

            if (array is int[] intArray)
            {
                int intLength = GetInt32ArrayLengthFromBitLength(_bitLength);
                if (array.Length - index < intLength)
                {
                    throw new ArgumentException(SR.Argument_InvalidOffLen);
                }

                if (intLength > 0)
                {
                    Span<int> source = MemoryMarshal.Cast<byte, int>((Span<byte>)_array)
                        .Slice(0, intLength);
                    if (BitConverter.IsLittleEndian)
                    {
                        source.CopyTo(intArray.AsSpan(index));
                    }
                    else
                    {
                        BinaryPrimitives.ReverseEndianness(source, intArray.AsSpan(index));
                    }

                    uint extraBits = (uint)_bitLength % BitsPerInt32;
                    if (extraBits != 0)
                    {
                        intArray[index + intLength - 1] =
                            ReverseIfBE(source[^1]) & ((1 << (int)extraBits) - 1);
                    }
                }
            }
            else if (array is byte[] byteArray)
            {
                int byteLength = GetByteArrayLengthFromBitLength(_bitLength);
                if (array.Length - index < byteLength)
                {
                    throw new ArgumentException(SR.Argument_InvalidOffLen);
                }

                if (byteLength > 0)
                {
                    ReadOnlySpan<byte> source = _array.AsSpan(0, byteLength);
                    source.CopyTo(byteArray.AsSpan(index));

                    uint extraBits = (uint)_bitLength % BitsPerByte;
                    if (extraBits != 0)
                    {
                        byteArray[index + byteLength - 1] =
                            (byte)(source[^1] & ((1 << (int)extraBits) - 1));
                    }
                }
            }
            else if (array is bool[] boolArray)
            {
                if (boolArray.Length - index < _bitLength)
                {
                    throw new ArgumentException(SR.Argument_InvalidOffLen);
                }

                for (uint bit = 0; bit < (uint)_bitLength; bit++)
                {
                    (uint byteIndex, uint bitOffset) = Math.DivRem(bit, BitsPerByte);
                    boolArray[(uint)index + bit] =
                        (_array[byteIndex] & (1 << (int)bitOffset)) != 0;
                }
            }
            else
            {
                throw new ArgumentException(SR.Arg_BitArrayTypeUnsupported, nameof(array));
            }
        }

        public bool HasAllSet()
        {
            uint extraBits = (uint)_bitLength % BitsPerByte;
            int byteCount = GetByteArrayLengthFromBitLength(_bitLength);
            if (extraBits != 0)
            {
                byteCount--;
            }

            if (_array.AsSpan(0, byteCount).ContainsAnyExcept((byte)0xFF))
            {
                return false;
            }

            if (extraBits == 0)
            {
                return true;
            }

            byte mask = (byte)((1 << (int)extraBits) - 1);
            return (_array[byteCount] & mask) == mask;
        }

        public bool HasAnySet()
        {
            uint extraBits = (uint)_bitLength % BitsPerByte;
            int byteCount = GetByteArrayLengthFromBitLength(_bitLength);
            if (extraBits != 0)
            {
                byteCount--;
            }

            if (_array.AsSpan(0, byteCount).ContainsAnyExcept((byte)0))
            {
                return true;
            }

            if (extraBits == 0)
            {
                return false;
            }

            byte mask = (byte)((1 << (int)extraBits) - 1);
            return (_array[byteCount] & mask) != 0;
        }

        public int PopCount()
        {
            int count = 0;
            foreach (int i in MemoryMarshal.Cast<byte, int>(_array))
            {
                count += int.PopCount(i);
            }
            return count;
        }

        public int Count => _bitLength;

        public object SyncRoot => this;

        public bool IsSynchronized => false;

        public bool IsReadOnly => false;

        public object Clone() => new BitArray(this);

        public IEnumerator GetEnumerator() => new BitArrayEnumeratorSimple(this);

        private static int GetInt32ArrayLengthFromBitLength(int bitLength)
        {
            Debug.Assert(bitLength >= 0);
            return (int)(((uint)bitLength + 31u) >> 5);
        }

        internal static int GetByteArrayLengthFromBitLength(int bitLength)
        {
            Debug.Assert(bitLength >= 0);
            return (int)(((uint)bitLength + 7u) >> 3);
        }

        private static int RoundUpToMultipleSizeOfInt32(int value) =>
            (value + (sizeof(int) - 1)) & ~(sizeof(int) - 1);

        private static int GetAlignedByteArrayLength(int bitLength) =>
            RoundUpToMultipleSizeOfInt32(GetByteArrayLengthFromBitLength(bitLength));

        private static byte[] AllocateByteArray(int bitLength)
        {
            int byteLength = GetAlignedByteArrayLength(bitLength);
            Debug.Assert(byteLength >= 0, "byteLength should be non-negative.");
            Debug.Assert(byteLength % sizeof(int) == 0, "byteLength should be a multiple of sizeof(int).");
            return bitLength != 0 ? new byte[byteLength] : [];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int ReverseIfBE(int value) =>
            BitConverter.IsLittleEndian ? value : BinaryPrimitives.ReverseEndianness(value);

        private static void ThrowArgumentOutOfRangeException(int index) =>
            throw new ArgumentOutOfRangeException(
                nameof(index),
                index,
                SR.ArgumentOutOfRange_IndexMustBeLess);

        private sealed class BitArrayEnumeratorSimple : IEnumerator, ICloneable
        {
            private static readonly object s_boxedTrue = true;
            private static readonly object s_boxedFalse = false;

            private readonly BitArray _bitArray;
            private readonly int _version;
            private int _index;
            private object _currentElement = s_boxedFalse;

            internal BitArrayEnumeratorSimple(BitArray bitArray)
            {
                _bitArray = bitArray;
                _index = -1;
                _version = bitArray._version;
            }

            private BitArrayEnumeratorSimple(BitArrayEnumeratorSimple enumerator)
            {
                _bitArray = enumerator._bitArray;
                _version = enumerator._version;
                _index = enumerator._index;
                _currentElement = enumerator._currentElement;
            }

            public object Clone() => new BitArrayEnumeratorSimple(this);

            public bool MoveNext()
            {
                if (_version != _bitArray._version)
                {
                    throw new InvalidOperationException(SR.InvalidOperation_EnumFailedVersion);
                }

                if (_index < (_bitArray._bitLength - 1))
                {
                    _index++;
                    _currentElement = _bitArray.Get(_index) ? s_boxedTrue : s_boxedFalse;
                    return true;
                }

                _index = _bitArray._bitLength;
                return false;
            }

            public object Current
            {
                get
                {
                    if ((uint)_index >= (uint)_bitArray._bitLength)
                    {
                        throw GetInvalidOperationException(_index);
                    }

                    return _currentElement;
                }
            }

            public void Reset()
            {
                if (_version != _bitArray._version)
                {
                    throw new InvalidOperationException(SR.InvalidOperation_EnumFailedVersion);
                }

                _index = -1;
            }

            private InvalidOperationException GetInvalidOperationException(int index)
            {
                if (index == -1)
                {
                    return new InvalidOperationException(SR.InvalidOperation_EnumNotStarted);
                }

                else
                {
                    Debug.Assert(index >= _bitArray._bitLength);
                    return new InvalidOperationException(SR.InvalidOperation_EnumEnded);
                }
            }
        }
    }
}
