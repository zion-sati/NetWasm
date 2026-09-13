using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace System
{
    // Portions derived from dotnet/runtime System.Private.CoreLib (MIT).
    public readonly ref struct ReadOnlySpan<T>
    {
        private readonly ref T _reference;
        private readonly int _length;

        public ReadOnlySpan(T[]? array)
        {
            if (array == null)
            {
                _reference = ref Unsafe.NullRef<T>();
                _length = 0;
                return;
            }
            _reference = ref MemoryMarshal.GetArrayDataReference(array);
            _length = array.Length;
        }

        public ReadOnlySpan(T[] array, int start, int length)
        {
            if (array == null)
            {
                throw new ArgumentNullException();
            }
            if (start < 0)
            {
                throw new ArgumentOutOfRangeException();
            }
            if (length < 0)
            {
                throw new ArgumentOutOfRangeException();
            }
            if (start > array.Length - length)
            {
                throw new ArgumentOutOfRangeException();
            }
            _reference = ref Unsafe.Add(
                ref MemoryMarshal.GetArrayDataReference(array), start);
            _length = length;
        }

        internal ReadOnlySpan(ref T reference, int length)
        {
            _reference = ref reference;
            _length = length;
        }

        public ReadOnlySpan(ref readonly T reference)
        {
            _reference = ref Unsafe.AsRef(in reference);
            _length = 1;
        }

        public unsafe ReadOnlySpan(void* pointer, int length)
        {
            if (length < 0)
            {
                throw new ArgumentOutOfRangeException();
            }
            _reference = ref Unsafe.AsRef<T>(pointer);
            _length = length;
        }

        public int Length
        {
            get => _length;
        }

        public bool IsEmpty
        {
            get => _length == 0;
        }

        public static ReadOnlySpan<T> Empty
        {
            get => default;
        }

        public static implicit operator ReadOnlySpan<T>(ArraySegment<T> segment) =>
            new(segment.Array!, segment.Offset, segment.Count);

        public ref readonly T this[int index]
        {
            get
            {
                if ((uint)index >= (uint)_length)
                {
                    throw new IndexOutOfRangeException();
                }
                return ref Unsafe.Add(ref _reference, index);
            }
        }

        public ref readonly T this[Index index] => ref this[index.GetOffset(_length)];

        public ReadOnlySpan<T> this[Range range]
        {
            get
            {
                var offsets = range.GetOffsetAndLength(_length);
                return Slice(offsets.Item1, offsets.Item2);
            }
        }

        public static bool operator ==(ReadOnlySpan<T> left, ReadOnlySpan<T> right) =>
            throw new PlatformNotSupportedException();

        public static bool operator !=(ReadOnlySpan<T> left, ReadOnlySpan<T> right) =>
            throw new PlatformNotSupportedException();

        public override bool Equals(object? value) =>
            throw new NotSupportedException();

        public override int GetHashCode() => throw new NotSupportedException();

        public override string ToString()
        {
            if (typeof(T) == typeof(char))
            {
                var characters = new ReadOnlySpan<char>(
                    ref Unsafe.As<T, char>(ref _reference), _length);
                return string.Create(characters.ToArray());
            }

            return $"System.ReadOnlySpan<{typeof(T).Name}>[{_length}]";
        }

        public Enumerator GetEnumerator() => new(this);

        public ref struct Enumerator : System.Collections.Generic.IEnumerator<T>,
            System.Collections.IEnumerator, IDisposable
        {
            private readonly ReadOnlySpan<T> _span;
            private int _index;

            internal Enumerator(ReadOnlySpan<T> span)
            {
                _span = span;
                _index = -1;
            }

            public bool MoveNext()
            {
                var next = _index + 1;
                if (next < _span.Length)
                {
                    _index = next;
                    return true;
                }

                return false;
            }

            public ref readonly T Current
            {
                get => ref _span[_index];
            }
            T System.Collections.Generic.IEnumerator<T>.Current => Current;
            object System.Collections.IEnumerator.Current => Current!;
            void System.Collections.IEnumerator.Reset() => _index = -1;
            void IDisposable.Dispose() { }
        }

        public ref readonly T GetPinnableReference()
        {
            ref T result = ref Unsafe.NullRef<T>();
            if (_length != 0)
            {
                result = ref _reference;
            }
            return ref result;
        }

        public ReadOnlySpan<T> Slice(int start)
        {
            if (start < 0 || start > _length)
            {
                throw new ArgumentOutOfRangeException();
            }
            return new ReadOnlySpan<T>(
                ref Unsafe.Add(ref _reference, start), _length - start);
        }

        public ReadOnlySpan<T> Slice(int start, int length)
        {
            if (start < 0)
            {
                throw new ArgumentOutOfRangeException();
            }
            if (length < 0)
            {
                throw new ArgumentOutOfRangeException();
            }
            if (start > _length - length)
            {
                throw new ArgumentOutOfRangeException();
            }
            return new ReadOnlySpan<T>(
                ref Unsafe.Add(ref _reference, start), length);
        }

        public void CopyTo(Span<T> destination)
        {
            var source = new Span<T>(ref _reference, _length);
            source.CopyTo(destination);
        }

        public bool TryCopyTo(Span<T> destination)
        {
            if (_length > destination.Length)
            {
                return false;
            }
            CopyTo(destination);
            return true;
        }

        public T[] ToArray()
        {
            var result = new T[_length];
            CopyTo(new Span<T>(result));
            return result;
        }

        public static ReadOnlySpan<T> CastUp<TDerived>(ReadOnlySpan<TDerived> items)
            where TDerived : class?, T
        {
            ref T reference = ref Unsafe.As<TDerived, T>(
                ref MemoryMarshal.GetReference(items));
            return new ReadOnlySpan<T>(ref reference, items.Length);
        }

        public static implicit operator ReadOnlySpan<T>(T[]? array) => new(array);

    }
}
