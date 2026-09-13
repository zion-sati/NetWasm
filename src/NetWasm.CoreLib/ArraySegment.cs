// Portions derived from dotnet/runtime System.Private.CoreLib.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections;
using System.Collections.Generic;

namespace System
{
    /// <summary>Delimits a section of a zero-based, one-dimensional array.</summary>
    public readonly struct ArraySegment<T> : ICollection<T>, IEnumerable<T>, IList<T>,
        IReadOnlyCollection<T>, IReadOnlyList<T>, IEnumerable
    {
        public static ArraySegment<T> Empty { get; } = new(new T[0]);

        private readonly T[]? _array;
        private readonly int _offset;
        private readonly int _count;

        public ArraySegment(T[] array)
        {
            if (array is null) throw new ArgumentNullException();
            _array = array;
            _offset = 0;
            _count = array.Length;
        }

        public ArraySegment(T[] array, int offset, int count)
        {
            if (array is null) throw new ArgumentNullException();
            if ((uint)offset > (uint)array.Length ||
                (uint)count > (uint)(array.Length - offset))
            {
                throw new ArgumentOutOfRangeException();
            }
            _array = array;
            _offset = offset;
            _count = count;
        }

        public T[]? Array
        {
            get => _array;
        }
        public int Offset
        {
            get => _offset;
        }
        public int Count
        {
            get => _count;
        }

        public T this[int index]
        {
            get
            {
                ValidateIndex(index);
                return _array![_offset + index];
            }
            set
            {
                ValidateIndex(index);
                _array![_offset + index] = value;
            }
        }

        public Enumerator GetEnumerator()
        {
            ThrowInvalidOperationIfDefault();
            return new Enumerator(this);
        }

        public void CopyTo(T[] destination) => CopyTo(destination, 0);

        public void CopyTo(T[] destination, int destinationIndex)
        {
            ThrowInvalidOperationIfDefault();
            System.Array.Copy(_array!, _offset, destination, destinationIndex, _count);
        }

        public void CopyTo(ArraySegment<T> destination)
        {
            ThrowInvalidOperationIfDefault();
            destination.ThrowInvalidOperationIfDefault();
            if (_count > destination._count) throw new ArgumentException();
            System.Array.Copy(_array!, _offset, destination._array!, destination._offset, _count);
        }

        public ArraySegment<T> Slice(int index)
        {
            ThrowInvalidOperationIfDefault();
            if ((uint)index > (uint)_count) throw new ArgumentOutOfRangeException();
            return new ArraySegment<T>(_array!, _offset + index, _count - index);
        }

        public ArraySegment<T> Slice(int index, int count)
        {
            ThrowInvalidOperationIfDefault();
            if ((uint)index > (uint)_count || (uint)count > (uint)(_count - index))
            {
                throw new ArgumentOutOfRangeException();
            }
            return new ArraySegment<T>(_array!, _offset + index, count);
        }

        public T[] ToArray()
        {
            ThrowInvalidOperationIfDefault();
            if (_count == 0) return Empty._array!;
            var result = new T[_count];
            System.Array.Copy(_array!, _offset, result, 0, _count);
            return result;
        }

        public override bool Equals(object? value) =>
            value is ArraySegment<T> other && Equals(other);

        public bool Equals(ArraySegment<T> other) =>
            _array == other._array && _offset == other._offset && _count == other._count;

        public override int GetHashCode() =>
            _array is null ? 0 : HashCode.Combine(_offset, _count, _array.GetHashCode());

        public static bool operator ==(ArraySegment<T> left, ArraySegment<T> right) => left.Equals(right);
        public static bool operator !=(ArraySegment<T> left, ArraySegment<T> right) => !left.Equals(right);
        public static implicit operator ArraySegment<T>(T[] array) =>
            array is null ? default : new ArraySegment<T>(array);

        T IList<T>.this[int index]
        {
            get => this[index];
            set => this[index] = value;
        }

        int IList<T>.IndexOf(T item)
        {
            ThrowInvalidOperationIfDefault();
            var index = System.Array.IndexOf(_array!, item, _offset, _count);
            return index < 0 ? -1 : index - _offset;
        }

        void IList<T>.Insert(int index, T item) => throw new NotSupportedException();
        void IList<T>.RemoveAt(int index) => throw new NotSupportedException();

        T IReadOnlyList<T>.this[int index] => this[index];

        bool ICollection<T>.IsReadOnly => true;
        void ICollection<T>.Add(T item) => throw new NotSupportedException();
        void ICollection<T>.Clear() => throw new NotSupportedException();

        bool ICollection<T>.Contains(T item)
        {
            ThrowInvalidOperationIfDefault();
            return System.Array.IndexOf(_array!, item, _offset, _count) >= 0;
        }

        bool ICollection<T>.Remove(T item) => throw new NotSupportedException();

        IEnumerator<T> IEnumerable<T>.GetEnumerator()
        {
            ThrowInvalidOperationIfDefault();
            return new Enumerator(this);
        }

        IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable<T>)this).GetEnumerator();

        private void ValidateIndex(int index)
        {
            ThrowInvalidOperationIfDefault();
            if ((uint)index >= (uint)_count) throw new ArgumentOutOfRangeException();
        }

        private void ThrowInvalidOperationIfDefault()
        {
            if (_array is null) throw new InvalidOperationException();
        }

        public struct Enumerator : IEnumerator<T>, IEnumerator, IDisposable
        {
            private readonly T[]? _array;
            private readonly int _start;
            private readonly int _end;
            private int _current;

            internal Enumerator(ArraySegment<T> segment)
            {
                _array = segment._array;
                _start = segment._offset;
                _end = segment._offset + segment._count;
                _current = segment._offset - 1;
            }

            public bool MoveNext()
            {
                if (_current < _end)
                {
                    _current++;
                    return _current < _end;
                }
                return false;
            }

            public T Current
            {
                get
                {
                    if (_current < _start || _current >= _end) throw new InvalidOperationException();
                    return _array![_current];
                }
            }

            object IEnumerator.Current => Current!;
            void IEnumerator.Reset() => _current = _start - 1;
            public void Dispose() { }
        }
    }
}
