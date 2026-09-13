// Adapted from dotnet/runtime System.Private.CoreLib's circular-buffer Queue<T>
// at commit 811225a482702af7ecc35d817966bc70b88a3a23.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Collections.Generic
{
    public class Queue<T> : IEnumerable<T>, IReadOnlyCollection<T>, System.Collections.ICollection,
        System.Collections.IEnumerable
    {
        private T[] _array;
        private int _head;
        private int _tail;
        private int _size;
        private int _version;

        public Queue()
        {
            _array = [];
        }

        public Queue(int capacity)
        {
            if (capacity < 0)
            {
                throw new System.ArgumentOutOfRangeException();
            }
            _array = new T[capacity];
        }

        public Queue(IEnumerable<T> collection) : this()
        {
            if (collection == null)
            {
                throw new System.ArgumentNullException();
            }
            foreach (var item in collection)
            {
                Enqueue(item);
            }
        }

        public int Count { get => _size; }
        public int Capacity { get => _array.Length; }

        public void Enqueue(T item)
        {
            if (_size == _array.Length)
            {
                Grow(_size + 1);
            }
            _array[_tail] = item;
            MoveNext(ref _tail);
            _size++;
            _version++;
        }

        public T Dequeue()
        {
            if (_size == 0)
            {
                throw new System.InvalidOperationException();
            }
            var removed = _array[_head];
            _array[_head] = default!;
            MoveNext(ref _head);
            _size--;
            _version++;
            return removed;
        }

        public bool TryDequeue(out T result)
        {
            if (_size == 0)
            {
                result = default!;
                return false;
            }
            result = Dequeue();
            return true;
        }

        public T Peek()
        {
            if (_size == 0)
            {
                throw new System.InvalidOperationException();
            }
            return _array[_head];
        }

        public bool TryPeek(out T result)
        {
            if (_size == 0)
            {
                result = default!;
                return false;
            }
            result = _array[_head];
            return true;
        }

        public bool Contains(T item)
        {
            for (var index = 0; index < _size; index++)
            {
                if (EqualityComparer<T>.Default.Equals(
                    _array[PhysicalIndex(index)], item))
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
                result[index] = _array[PhysicalIndex(index)];
            }
            return result;
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            if (array == null)
            {
                throw new System.ArgumentNullException();
            }
            if (arrayIndex < 0 || arrayIndex > array.Length - _size)
            {
                throw new System.ArgumentOutOfRangeException();
            }
            for (var index = 0; index < _size; index++)
            {
                array[arrayIndex + index] = _array[PhysicalIndex(index)];
            }
        }

        public void Clear()
        {
            for (var index = 0; index < _size; index++)
            {
                _array[PhysicalIndex(index)] = default!;
            }
            _size = 0;
            _head = 0;
            _tail = 0;
            _version++;
        }

        public Enumerator GetEnumerator() => new(this);
        IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
            GetEnumerator();

        bool System.Collections.ICollection.IsSynchronized => false;
        object System.Collections.ICollection.SyncRoot => this;

        void System.Collections.ICollection.CopyTo(System.Array array, int index)
        {
            if (array is T[] typed)
            {
                CopyTo(typed, index);
                return;
            }
            throw new System.ArgumentException();
        }

        private int PhysicalIndex(int logicalIndex)
        {
            var index = _head + logicalIndex;
            return index >= _array.Length ? index - _array.Length : index;
        }

        private void Grow(int required)
        {
            var capacity = _array.Length == 0 ? 4 : _array.Length * 2;
            if (capacity < required)
            {
                capacity = required;
            }
            var replacement = new T[capacity];
            for (var index = 0; index < _size; index++)
            {
                replacement[index] = _array[PhysicalIndex(index)];
            }
            _array = replacement;
            _head = 0;
            _tail = _size;
        }

        private void MoveNext(ref int index)
        {
            index++;
            if (index == _array.Length)
            {
                index = 0;
            }
        }

        public void TrimExcess()
        {
            if (_size < (int)(_array.Length * 0.9))
            {
                SetCapacity(_size);
            }
        }

        public void TrimExcess(int capacity)
        {
            if (capacity < _size || capacity < 0)
            {
                throw new System.ArgumentOutOfRangeException();
            }
            if (capacity < _array.Length)
            {
                SetCapacity(capacity);
            }
        }

        public int EnsureCapacity(int capacity)
        {
            if (capacity < 0)
            {
                throw new System.ArgumentOutOfRangeException();
            }
            if (capacity > _array.Length)
            {
                Grow(capacity);
            }
            return _array.Length;
        }

        private void SetCapacity(int capacity)
        {
            var replacement = new T[capacity];
            for (var index = 0; index < _size; index++)
            {
                replacement[index] = _array[PhysicalIndex(index)];
            }
            _array = replacement;
            _head = 0;
            _tail = _size == capacity ? 0 : _size;
        }

        public struct Enumerator : IEnumerator<T>, System.Collections.IEnumerator, System.IDisposable
        {
            private readonly Queue<T> _queue;
            private readonly int _version;
            private int _index;
            private T _current;

            internal Enumerator(Queue<T> queue)
            {
                _queue = queue;
                _version = queue._version;
                _index = 0;
                _current = default!;
            }

            public T Current { get => _current; }
            object System.Collections.IEnumerator.Current => _current!;

            public bool MoveNext()
            {
                if (_version != _queue._version)
                {
                    throw new System.InvalidOperationException();
                }
                if (_index == _queue._size)
                {
                    _current = default!;
                    return false;
                }
                _current = _queue._array[_queue.PhysicalIndex(_index++)];
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
