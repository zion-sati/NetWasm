// Portions derived from dotnet/runtime System.Private.CoreLib BitArray at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Collections
{
    public sealed class BitArray : ICollection, IEnumerable, ICloneable
    {
        private const int BitsPerByte = 8;
        internal byte[] _array;
        private int _bitLength;
        private int _version;

        public BitArray(int length) : this(length, false) { }

        public BitArray(int length, bool defaultValue)
        {
            if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
            _bitLength = length;
            _array = new byte[GetByteArrayLengthFromBitLength(length)];
            if (defaultValue) SetAll(true);
        }

        public BitArray(bool[] values)
        {
            ArgumentNullException.ThrowIfNull(values);
            _bitLength = values.Length;
            _array = new byte[GetByteArrayLengthFromBitLength(_bitLength)];
            for (var index = 0; index < values.Length; index++) if (values[index]) SetBit(index, true);
        }

        public BitArray(byte[] bytes)
        {
            ArgumentNullException.ThrowIfNull(bytes);
            if (bytes.Length > int.MaxValue / BitsPerByte) throw new ArgumentException();
            _bitLength = bytes.Length * BitsPerByte;
            _array = new byte[bytes.Length];
            Array.Copy(bytes, 0, _array, 0, bytes.Length);
        }

        public BitArray(int[] values)
        {
            ArgumentNullException.ThrowIfNull(values);
            if (values.Length > int.MaxValue / 32) throw new ArgumentException();
            _bitLength = values.Length * 32;
            _array = new byte[GetByteArrayLengthFromBitLength(_bitLength)];
            for (var index = 0; index < values.Length; index++)
            {
                var value = values[index];
                for (var bit = 0; bit < 32; bit++) if ((value & (1 << bit)) != 0) SetBit(index * 32 + bit, true);
            }
        }

        public BitArray(BitArray bits)
        {
            ArgumentNullException.ThrowIfNull(bits);
            _bitLength = bits._bitLength;
            _array = new byte[bits._array.Length];
            Array.Copy(bits._array, 0, _array, 0, _array.Length);
        }

        public BitArray(ReadOnlySpan<bool> values) : this(values.ToArray()) { }
        public BitArray(ReadOnlySpan<byte> values) : this(values.ToArray()) { }
        public BitArray(ReadOnlySpan<int> values) : this(values.ToArray()) { }

        public bool this[int index]
        {
            get => Get(index);
            set => Set(index, value);
        }

        public int Count
        {
            get => _bitLength;
        }
        public int Length
        {
            get => _bitLength;
            set
            {
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
                if (value == _bitLength) return;
                var replacement = new byte[GetByteArrayLengthFromBitLength(value)];
                var copy = replacement.Length < _array.Length ? replacement.Length : _array.Length;
                Array.Copy(_array, 0, replacement, 0, copy);
                _array = replacement;
                _bitLength = value;
                ClearUnusedBits();
                _version++;
            }
        }

        public bool IsReadOnly
        {
            get => false;
        }

        public bool IsSynchronized
        {
            get => false;
        }

        public object SyncRoot
        {
            get => this;
        }

        public bool Get(int index)
        {
            ValidateIndex(index);
            return (_array[index >> 3] & (1 << (index & 7))) != 0;
        }

        public void Set(int index, bool value)
        {
            ValidateIndex(index);
            SetBit(index, value);
            _version++;
        }

        public void SetAll(bool value)
        {
            for (var index = 0; index < _array.Length; index++) _array[index] = value ? (byte)0xFF : (byte)0;
            ClearUnusedBits();
            _version++;
        }

        public BitArray And(BitArray value)
        {
            ValidateSameLength(value);
            for (var index = 0; index < _array.Length; index++) _array[index] &= value._array[index];
            ClearUnusedBits();
            _version++;
            return this;
        }

        public BitArray Or(BitArray value)
        {
            ValidateSameLength(value);
            for (var index = 0; index < _array.Length; index++) _array[index] |= value._array[index];
            ClearUnusedBits();
            _version++;
            return this;
        }

        public BitArray Xor(BitArray value)
        {
            ValidateSameLength(value);
            for (var index = 0; index < _array.Length; index++) _array[index] ^= value._array[index];
            ClearUnusedBits();
            _version++;
            return this;
        }

        public BitArray Not()
        {
            for (var index = 0; index < _array.Length; index++) _array[index] = (byte)~_array[index];
            ClearUnusedBits();
            _version++;
            return this;
        }

        public BitArray LeftShift(int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            if (count == 0) return this;
            if (count >= _bitLength) { SetAll(false); return this; }
            for (var index = _bitLength - 1; index >= count; index--) SetBit(index, Get(index - count));
            for (var index = 0; index < count; index++) SetBit(index, false);
            _version++;
            return this;
        }

        public BitArray RightShift(int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            if (count == 0) return this;
            if (count >= _bitLength) { SetAll(false); return this; }
            for (var index = 0; index < _bitLength - count; index++) SetBit(index, Get(index + count));
            for (var index = _bitLength - count; index < _bitLength; index++) SetBit(index, false);
            _version++;
            return this;
        }

        public bool HasAllSet()
        {
            for (var index = 0; index < _bitLength; index++) if (!Get(index)) return false;
            return true;
        }

        public bool HasAnySet()
        {
            for (var index = 0; index < _bitLength; index++) if (Get(index)) return true;
            return false;
        }

        public int PopCount()
        {
            var count = 0;
            for (var index = 0; index < _bitLength; index++) if (Get(index)) count++;
            return count;
        }

        public object Clone() => new BitArray(this);

        public void CopyTo(Array array, int index)
        {
            ArgumentNullException.ThrowIfNull(array);
            var arrayLength = array is bool[] boolArray ? boolArray.Length :
                array is byte[] byteArray ? byteArray.Length :
                array is int[] intArray ? intArray.Length : array.Length;
            if (index < 0 || index > arrayLength) throw new ArgumentOutOfRangeException(nameof(index));
            if (array is bool[] bools)
            {
                if (bools.Length - index < _bitLength) throw new ArgumentException();
                for (var current = 0; current < _bitLength; current++) bools[index + current] = Get(current);
                return;
            }
            if (array is byte[] bytes)
            {
                var count = GetByteArrayLengthFromBitLength(_bitLength);
                if (bytes.Length - index < count) throw new ArgumentException();
                Array.Copy(_array, 0, bytes, index, count);
                return;
            }
            if (array is int[] ints)
            {
                var count = (int)(_bitLength / 32) + (_bitLength % 32 == 0 ? 0 : 1);
                if (ints.Length - index < count) throw new ArgumentException();
                for (var current = 0; current < count; current++)
                {
                    var value = 0;
                    for (var bit = 0; bit < 32 && current * 32 + bit < _bitLength; bit++) if (Get(current * 32 + bit)) value |= 1 << bit;
                    ints[index + current] = value;
                }
                return;
            }
            throw new ArgumentException();
        }

        public IEnumerator GetEnumerator() => new Enumerator(this);

        internal static int GetByteArrayLengthFromBitLength(int bitLength) =>
            (bitLength >> 3) + ((bitLength & 7) == 0 ? 0 : 1);
        private void ValidateIndex(int index) { if ((uint)index >= (uint)_bitLength) throw new ArgumentOutOfRangeException(nameof(index)); }
        private void ValidateSameLength(BitArray value) { ArgumentNullException.ThrowIfNull(value); if (_bitLength != value._bitLength) throw new ArgumentException(); }
        private void SetBit(int index, bool value)
        { if (value) _array[index >> 3] |= (byte)(1 << (index & 7)); else _array[index >> 3] &= (byte)~(1 << (index & 7)); }
        private void ClearUnusedBits()
        { if (_bitLength != 0 && (_bitLength & 7) != 0) _array[^1] &= (byte)((1 << (_bitLength & 7)) - 1); }

        private sealed class Enumerator : IEnumerator
        {
            private readonly BitArray _owner; private readonly int _version; private int _index = -1;
            public Enumerator(BitArray owner) { _owner = owner; _version = owner._version; }
            public object Current { get { if (_index < 0 || _index >= _owner._bitLength) throw new InvalidOperationException(); return _owner.Get(_index); } }
            public bool MoveNext() { if (_version != _owner._version) throw new InvalidOperationException(); _index++; return _index < _owner._bitLength; }
            public void Reset() { if (_version != _owner._version) throw new InvalidOperationException(); _index = -1; }
        }
    }
}
