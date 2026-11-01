// NetWasm's own ArrayPool<T>, not a port: upstream's ArrayPool.cs, SharedArrayPool.cs and
// ConfigurableArrayPool.cs keep per-thread and per-core caches this stands in for.
using System.Buffers;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace System.Buffers
{
    public abstract class ArrayPool<T>
    {
        private sealed class SharedPool : ArrayPool<T>
        {
            private const int DefaultMaxArrayLength = 1024 * 1024;
            private const int DefaultMaxArraysPerBucket = 50;
            private readonly Bucket[] _buckets;

            public SharedPool() : this(DefaultMaxArrayLength, DefaultMaxArraysPerBucket)
            {
            }

            public SharedPool(int maxArrayLength, int maxArraysPerBucket)
            {
                const int minimumArrayLength = 0x10;
                const int maximumArrayLength = 0x40000000;
                maxArrayLength = Math.Max(minimumArrayLength,
                    Math.Min(maximumArrayLength, maxArrayLength));
                _buckets = new Bucket[SelectBucketIndex(maxArrayLength) + 1];
                for (var index = 0; index < _buckets.Length; index++)
                {
                    _buckets[index] = new Bucket(
                        GetMaxSizeForBucket(index), maxArraysPerBucket);
                }
            }

            public override T[] Rent(int minimumLength)
            {
                if (minimumLength < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(minimumLength));
                }
                if (minimumLength == 0)
                {
                    return Array.Empty<T>();
                }

                var bucketIndex = SelectBucketIndex(minimumLength);
                if (bucketIndex >= _buckets.Length)
                {
                    return new T[minimumLength];
                }
                var limit = Math.Min(bucketIndex + 2, _buckets.Length);
                for (var index = bucketIndex; index < limit; index++)
                {
                    var buffer = _buckets[index].Rent();
                    if (buffer is not null)
                    {
                        return buffer;
                    }
                }
                return new T[_buckets[bucketIndex].BufferLength];
            }

            public override void Return(T[] array, bool clearArray = false)
            {
                if (array is null)
                {
                    throw new ArgumentNullException(nameof(array));
                }
                if (array.Length == 0)
                {
                    return;
                }

                var bucketIndex = SelectBucketIndex(array.Length);
                if (bucketIndex >= _buckets.Length)
                {
                    return;
                }
                if (clearArray)
                {
                    Array.Clear(array, 0, array.Length);
                }
                _buckets[bucketIndex].Return(array);
            }

            private static int SelectBucketIndex(int bufferSize) =>
                BitOperations.Log2((uint)bufferSize - 1 | 15) - 3;

            private static int GetMaxSizeForBucket(int bucketIndex) =>
                16 << bucketIndex;

            private sealed class Bucket
            {
                private readonly T[]?[] _buffers;
                private int _index;

                public Bucket(int bufferLength, int numberOfBuffers)
                {
                    BufferLength = bufferLength;
                    _buffers = new T[numberOfBuffers][];
                }

                public int BufferLength { get; }

                public T[]? Rent()
                {
                    if (_index >= _buffers.Length)
                    {
                        return null;
                    }
                    var buffer = _buffers[_index];
                    _buffers[_index++] = null;
                    return buffer ?? new T[BufferLength];
                }

                public void Return(T[] array)
                {
                    if (array.Length != BufferLength)
                    {
                        throw new ArgumentException(null, nameof(array));
                    }
                    if (_index != 0)
                    {
                        _buffers[--_index] = array;
                    }
                }
            }
        }

        private static readonly ArrayPool<T> s_shared = new SharedPool();
        public static ArrayPool<T> Shared
        {
            get => s_shared;
        }
        public static ArrayPool<T> Create() => new SharedPool();
        public static ArrayPool<T> Create(int maxArrayLength, int maxArraysPerBucket)
        {
            if (maxArrayLength <= 0 || maxArraysPerBucket <= 0)
                throw new ArgumentOutOfRangeException();
            return new SharedPool(maxArrayLength, maxArraysPerBucket);
        }

        public abstract T[] Rent(int minimumLength);
        public abstract void Return(T[] array, bool clearArray = false);
    }
}
