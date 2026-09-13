// Portions of this implementation are adapted from dotnet/runtime
// System.Private.CoreLib Number.BigInteger.cs. The upstream implementation is
// licensed under MIT. Copyright (c) .NET Foundation and Contributors.
//
// Unlike the upstream stack buffer, this profile grows dynamically so the
// runtime does not impose an arbitrary digit or block ceiling.
namespace System
{
    internal sealed class FloatingBigInteger
    {
        private uint[] _blocks;
        private int _length;

        internal FloatingBigInteger(uint value)
        {
            _blocks = new uint[1];
            if (value != 0)
            {
                _blocks[0] = value;
                _length = 1;
            }
        }

        internal FloatingBigInteger(ulong value)
        {
            _blocks = new uint[value > uint.MaxValue ? 2 : 1];
            if (value != 0)
            {
                _blocks[0] = (uint)value;
                _length = 1;
                if (value > uint.MaxValue)
                {
                    _blocks[1] = (uint)(value >> 32);
                    _length = 2;
                }
            }
        }

        private FloatingBigInteger(uint[] blocks, int length)
        {
            _blocks = blocks;
            _length = length;
        }

        internal bool IsZero => _length == 0;

        internal FloatingBigInteger Clone()
        {
            var blocks = new uint[_length == 0 ? 1 : _length];
            for (var index = 0; index < _length; index++) blocks[index] = _blocks[index];
            return new FloatingBigInteger(blocks, _length);
        }

        internal int BitLength
        {
            get
            {
                if (_length == 0) return 0;
                var high = _blocks[_length - 1];
                var bits = 0;
                while (high != 0)
                {
                    high >>= 1;
                    bits++;
                }
                return checked((_length - 1) * 32 + bits);
            }
        }

        internal void Multiply(uint value)
        {
            if (_length == 0 || value == 1) return;
            if (value == 0)
            {
                _length = 0;
                return;
            }
            EnsureCapacity(checked(_length + 1));
            var carry = 0UL;
            for (var index = 0; index < _length; index++)
            {
                var product = (ulong)_blocks[index] * value + carry;
                _blocks[index] = (uint)product;
                carry = product >> 32;
            }
            if (carry != 0) _blocks[_length++] = (uint)carry;
        }

        internal void Add(uint value)
        {
            if (value == 0) return;
            if (_length == 0)
            {
                _blocks[0] = value;
                _length = 1;
                return;
            }
            var carry = (ulong)value;
            var index = 0;
            while (carry != 0 && index < _length)
            {
                var sum = _blocks[index] + carry;
                _blocks[index] = (uint)sum;
                carry = sum >> 32;
                index++;
            }
            if (carry != 0)
            {
                EnsureCapacity(checked(_length + 1));
                _blocks[_length++] = (uint)carry;
            }
        }

        internal void MultiplyPower10(int power)
        {
            for (var index = 0; index < power; index++) Multiply(10);
        }

        internal void ShiftLeft(int bits)
        {
            if (_length == 0 || bits == 0) return;
            var wordShift = bits / 32;
            var bitShift = bits & 31;
            var required = checked(_length + wordShift + (bitShift == 0 ? 0 : 1));
            var shifted = new uint[required == 0 ? 1 : required];
            var carry = 0UL;
            for (var index = 0; index < _length; index++)
            {
                var value = ((ulong)_blocks[index] << bitShift) | carry;
                shifted[index + wordShift] = (uint)value;
                carry = value >> 32;
            }
            var length = _length + wordShift;
            if (carry != 0) shifted[length++] = (uint)carry;
            _blocks = shifted;
            _length = length;
        }

        internal void ShiftRightOne()
        {
            var carry = 0U;
            for (var index = _length - 1; index >= 0; index--)
            {
                var next = _blocks[index] & 1U;
                _blocks[index] = (_blocks[index] >> 1) | (carry << 31);
                carry = next;
            }
            Normalize();
        }

        internal static int Compare(FloatingBigInteger left, FloatingBigInteger right)
        {
            if (left._length != right._length) return left._length < right._length ? -1 : 1;
            for (var index = left._length - 1; index >= 0; index--)
            {
                if (left._blocks[index] == right._blocks[index]) continue;
                return left._blocks[index] < right._blocks[index] ? -1 : 1;
            }
            return 0;
        }

        internal void Subtract(FloatingBigInteger value)
        {
            var borrow = 0UL;
            for (var index = 0; index < _length; index++)
            {
                var subtrahend = (index < value._length ? value._blocks[index] : 0UL) + borrow;
                var current = _blocks[index];
                _blocks[index] = unchecked((uint)(current - subtrahend));
                borrow = current < subtrahend ? 1UL : 0UL;
            }
            Normalize();
        }

        internal static ulong DivideToUInt64(
            FloatingBigInteger numerator,
            FloatingBigInteger denominator,
            out FloatingBigInteger remainder)
        {
            if (denominator.IsZero) throw new DivideByZeroException();
            remainder = numerator.Clone();
            if (Compare(remainder, denominator) < 0) return 0;
            var shift = remainder.BitLength - denominator.BitLength;
            if (shift >= 64) throw new OverflowException();
            var shifted = denominator.Clone();
            shifted.ShiftLeft(shift);
            var quotient = 0UL;
            for (var bit = shift; bit >= 0; bit--)
            {
                if (Compare(remainder, shifted) >= 0)
                {
                    remainder.Subtract(shifted);
                    quotient |= 1UL << bit;
                }
                shifted.ShiftRightOne();
            }
            return quotient;
        }

        private void EnsureCapacity(int required)
        {
            if (_blocks.Length >= required) return;
            var capacity = _blocks.Length;
            while (capacity < required) capacity = checked(capacity * 2);
            var replacement = new uint[capacity];
            for (var index = 0; index < _length; index++) replacement[index] = _blocks[index];
            _blocks = replacement;
        }

        private void Normalize()
        {
            while (_length != 0 && _blocks[_length - 1] == 0) _length--;
        }
    }
}
