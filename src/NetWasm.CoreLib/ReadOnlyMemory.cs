using System.Buffers;

namespace System
{
    // Portions derived from dotnet/runtime System.Private.CoreLib (MIT).
    public readonly struct ReadOnlyMemory<T> : IEquatable<ReadOnlyMemory<T>>
    {
        private readonly object? _object;
        private readonly int _start;
        private readonly int _length;

        public ReadOnlyMemory(T[]? array)
        {
            _object = array;
            _start = 0;
            _length = array?.Length ?? 0;
        }

        public ReadOnlyMemory(T[] array, int start, int length)
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
            _object = array;
            _start = start;
            _length = length;
        }

        internal ReadOnlyMemory(MemoryManager<T> manager, int start, int length)
        {
            if (manager == null) throw new ArgumentNullException();
            if (start < 0 || length < 0) throw new ArgumentOutOfRangeException();
            _object = manager;
            _start = start;
            _length = length;
        }

        internal ReadOnlyMemory(string text, int start, int length)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            if (typeof(T) != typeof(char)) throw new InvalidOperationException();
            if ((uint)start > (uint)text.Length ||
                (uint)length > (uint)(text.Length - start))
            {
                throw new ArgumentOutOfRangeException(nameof(start));
            }
            _object = text;
            _start = start;
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
        public static ReadOnlyMemory<T> Empty
        {
            get => default;
        }
        public ReadOnlySpan<T> Span
        {
            get
            {
                if (_object is string text)
                {
                    return new ReadOnlySpan<T>(
                        (T[])(object)text.ToCharArray(),
                        _start,
                        _length);
                }
                return _object is MemoryManager<T> manager
                    ? manager.GetSpan().Slice(_start, _length)
                    : _object is not T[] array
                        ? new ReadOnlySpan<T>((T[]?)null)
                        : new ReadOnlySpan<T>(array, _start, _length);
            }
        }

        public ReadOnlyMemory<T> Slice(int start) => Slice(start, _length - start);

        public ReadOnlyMemory<T> Slice(int start, int length)
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
            return _object switch
            {
                MemoryManager<T> manager =>
                    new ReadOnlyMemory<T>(manager, _start + start, length),
                string text => new ReadOnlyMemory<T>(text, _start + start, length),
                T[] array => new ReadOnlyMemory<T>(array, _start + start, length),
                _ => default,
            };
        }

        public void CopyTo(Memory<T> destination) => Span.CopyTo(destination.Span);
        public bool TryCopyTo(Memory<T> destination) => Span.TryCopyTo(destination.Span);
        public T[] ToArray() => Span.ToArray();
        public unsafe MemoryHandle Pin() => _object is MemoryManager<T> manager
            ? manager.Pin(_start)
            : throw new PlatformNotSupportedException();

        public bool Equals(ReadOnlyMemory<T> other) =>
            ReferenceEquals(_object, other._object) &&
            _start == other._start && _length == other._length;

        public override bool Equals(object? value) => value is ReadOnlyMemory<T> other && Equals(other);
        public override int GetHashCode() =>
            unchecked((_object?.GetHashCode() ?? 0) * 31 + _start * 17 + _length);
        public override string ToString() =>
            typeof(T) == typeof(char)
                ? Span.ToString()
                : $"System.ReadOnlyMemory<{typeof(T).Name}>[{_length}]";

        public static implicit operator ReadOnlyMemory<T>(T[]? array) => new(array);
        public static implicit operator ReadOnlyMemory<T>(ArraySegment<T> segment) =>
            new(segment.Array!, segment.Offset, segment.Count);
        internal Memory<T> DangerousAsMemory() =>
            _object switch
            {
                MemoryManager<T> manager => new Memory<T>(manager, _start, _length),
                T[] array => new Memory<T>(array, _start, _length),
                string => new Memory<T>(Span.ToArray()),
                _ => default,
            };

        internal bool TryGetArray(out ArraySegment<T> segment)
        {
            if (_length == 0)
            {
                segment = ArraySegment<T>.Empty;
                return true;
            }

            if (_object is T[] array)
            {
                segment = new ArraySegment<T>(array, _start, _length);
                return true;
            }

            if (_object is MemoryManager<T> manager &&
                manager.TryGetArray(out var managerSegment))
            {
                segment = managerSegment.Slice(_start, _length);
                return true;
            }

            segment = default;
            return false;
        }

        internal bool TryGetString(out string? text, out int start, out int length)
        {
            text = _object as string;
            start = text is null ? 0 : _start;
            length = text is null ? 0 : _length;
            return text is not null;
        }

        internal bool TryGetMemoryManager<TManager>(
            out TManager? manager,
            out int start,
            out int length)
            where TManager : MemoryManager<T>
        {
            manager = _object as TManager;
            if (manager is null)
            {
                start = 0;
                length = 0;
                return false;
            }

            start = _start;
            length = _length;
            return true;
        }

    }
}
