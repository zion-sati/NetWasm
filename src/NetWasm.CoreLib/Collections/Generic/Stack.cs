// Portions derived from dotnet/runtime System.Collections Stack<T>.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Collections.Generic
{
    public class Stack<T> : IEnumerable<T>, IReadOnlyCollection<T>
    {
        private T[] _array;
        private int _size;
        private int _version;

        public Stack()
        {
            _array = [];
        }

        public Stack(int capacity)
        {
            if (capacity < 0)
            {
                throw new System.ArgumentOutOfRangeException();
            }
            _array = new T[capacity];
        }

        public int Count => _size;

        public void Push(T item)
        {
            if (_size == _array.Length)
            {
                var capacity = _array.Length == 0 ? 4 : _array.Length * 2;
                var replacement = new T[capacity];
                System.Array.Copy(_array, 0, replacement, 0, _size);
                _array = replacement;
            }
            _array[_size++] = item;
            _version++;
        }

        public T Pop()
        {
            if (_size == 0)
            {
                throw new System.InvalidOperationException();
            }
            var result = _array[--_size];
            _array[_size] = default!;
            _version++;
            return result;
        }

        public bool TryPop(out T result)
        {
            if (_size == 0)
            {
                result = default!;
                return false;
            }
            result = Pop();
            return true;
        }

        public T Peek()
        {
            if (_size == 0)
            {
                throw new System.InvalidOperationException();
            }
            return _array[_size - 1];
        }

        public bool TryPeek(out T result)
        {
            if (_size == 0)
            {
                result = default!;
                return false;
            }
            result = _array[_size - 1];
            return true;
        }

        public bool Contains(T item)
        {
            for (var index = _size - 1; index >= 0; index--)
            {
                if (EqualityComparer<T>.Default.Equals(_array[index], item))
                {
                    return true;
                }
            }
            return false;
        }

        public T[] ToArray()
        {
            var result = new T[_size];
            for (var index = 0; index < _size; index++)
            {
                result[index] = _array[_size - index - 1];
            }
            return result;
        }

        public void Clear()
        {
            while (_size != 0)
            {
                _array[--_size] = default!;
            }
            _version++;
        }

        public Enumerator GetEnumerator() => new(this);
        IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
            GetEnumerator();

        public struct Enumerator : IEnumerator<T>
        {
            private readonly Stack<T> _stack;
            private readonly int _version;
            private int _index;
            private T _current;

            internal Enumerator(Stack<T> stack)
            {
                _stack = stack;
                _version = stack._version;
                _index = stack._size;
                _current = default!;
            }

            public T Current => _current;
            object System.Collections.IEnumerator.Current => _current!;

            public bool MoveNext()
            {
                if (_version != _stack._version)
                {
                    throw new System.InvalidOperationException();
                }
                if (_index == 0)
                {
                    _current = default!;
                    return false;
                }
                _current = _stack._array[--_index];
                return true;
            }

            public void Dispose()
            {
            }

            void System.Collections.IEnumerator.Reset() =>
                throw new System.NotSupportedException();
        }
    }
}
