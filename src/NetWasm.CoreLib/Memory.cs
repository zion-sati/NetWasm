using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace System
{
    // Portions derived from dotnet/runtime System.Private.CoreLib (MIT).
    public readonly struct Memory<T> : IEquatable<Memory<T>>
    {
        internal readonly T[]? _array;
        internal readonly MemoryManager<T>? _manager;
        internal readonly int _start;
        internal readonly int _length;

        public Memory(T[]? array)
        {
            _array = array;
            _manager = null;
            _start = 0;
            _length = array?.Length ?? 0;
        }

        public Memory(T[] array, int start, int length)
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
            _array = array;
            _manager = null;
            _start = start;
            _length = length;
        }

        internal Memory(MemoryManager<T> manager, int start, int length)
        {
            if (manager == null)
            {
                throw new ArgumentNullException();
            }
            if (start < 0 || length < 0)
            {
                throw new ArgumentOutOfRangeException();
            }
            _array = null;
            _manager = manager;
            _start = start;
            _length = length;
        }

        internal Memory(MemoryManager<T> manager, int length)
            : this(manager, 0, length) { }

        public int Length
        {
            get => _length;
        }
        public bool IsEmpty
        {
            get => _length == 0;
        }
        public static Memory<T> Empty
        {
            get => default;
        }
        public Span<T> Span
        {
            get
            {
                if (_manager != null)
                {
                    return _manager.GetSpan().Slice(_start, _length);
                }
                return _array == null
                    ? new Span<T>((T[]?)null)
                    : new Span<T>(_array, _start, _length);
            }
        }

        public Memory<T> Slice(int start) => Slice(start, _length - start);

        public Memory<T> Slice(int start, int length)
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
            return _manager != null
                ? new Memory<T>(_manager, _start + start, length)
                : _array == null
                    ? default
                    : new Memory<T>(_array, _start + start, length);
        }

        public void CopyTo(Memory<T> destination) => Span.CopyTo(destination.Span);

        public bool TryCopyTo(Memory<T> destination) => Span.TryCopyTo(destination.Span);

        public T[] ToArray() => Span.ToArray();

        public unsafe MemoryHandle Pin() =>
            _manager != null ? _manager.Pin(_start) : throw new PlatformNotSupportedException();

        public bool Equals(Memory<T> other) =>
            ReferenceEquals(_array, other._array) &&
            ReferenceEquals(_manager, other._manager) &&
            _start == other._start && _length == other._length;

        public override bool Equals(object? value) =>
            value is Memory<T> other && Equals(other);

        public override int GetHashCode() =>
            unchecked((_array?.GetHashCode() ?? _manager?.GetHashCode() ?? 0) * 31 + _start * 17 + _length);

        public override string ToString() =>
            typeof(T) == typeof(char)
                ? Span.ToString()
                : $"System.Memory<{typeof(T).Name}>[{_length}]";

        public static implicit operator Memory<T>(T[]? array) => new(array);
        public static implicit operator Memory<T>(ArraySegment<T> segment) =>
            new(segment.Array!, segment.Offset, segment.Count);

        public static implicit operator ReadOnlyMemory<T>(Memory<T> memory) =>
            memory._manager != null
                ? new ReadOnlyMemory<T>(memory._manager!, memory._start, memory._length)
                : new ReadOnlyMemory<T>(memory._array!, memory._start, memory._length);

    }
}

namespace System.Buffers
{
    // Portions derived from dotnet/runtime System.Private.CoreLib MemoryHandle.cs
    // at commit 811225a482702af7ecc35d817966bc70b88a3a23 (MIT).
    public unsafe struct MemoryHandle : IDisposable
    {
        private void* _pointer;
        private GCHandle _handle;
        private IPinnable? _pinnable;

        public unsafe MemoryHandle(
            void* pointer,
            System.Runtime.InteropServices.GCHandle handle = default(System.Runtime.InteropServices.GCHandle),
            IPinnable? pinnable = null)
        {
            _pointer = pointer;
            _handle = handle;
            _pinnable = pinnable;
        }

        public void* Pointer
        {
            get => _pointer;
        }

        public void Dispose()
        {
            if (_handle.IsAllocated)
            {
                _handle.Free();
            }

            if (_pinnable != null)
            {
                _pinnable.Unpin();
                _pinnable = null;
            }

            _pointer = null;
        }
    }

    public interface IPinnable
    {
        MemoryHandle Pin(int elementIndex);
        void Unpin();
    }

    public interface IMemoryOwner<T> : IDisposable
    {
        Memory<T> Memory { get; }
    }

    public abstract class MemoryManager<T> : IMemoryOwner<T>, IPinnable, IDisposable
    {
        public abstract Span<T> GetSpan();
        public abstract MemoryHandle Pin(int elementIndex = 0);
        public abstract void Unpin();

        public virtual Memory<T> Memory
        {
            get => CreateMemory(GetSpan().Length);
        }

        protected Memory<T> CreateMemory(int length) => new(this, length);

        protected Memory<T> CreateMemory(int start, int length) => new(this, start, length);

        protected internal virtual bool TryGetArray(out ArraySegment<T> segment)
        {
            segment = default;
            return false;
        }

        void IDisposable.Dispose() => Dispose(disposing: true);

        protected abstract void Dispose(bool disposing);
    }

    public abstract class ArrayPool<T>
    {
        private sealed class SharedPool : ArrayPool<T>
        {
            private T[]? _cached;
            private readonly int _maxArrayLength;

            public SharedPool(int maxArrayLength = int.MaxValue) => _maxArrayLength = maxArrayLength;

            public override T[] Rent(int minimumLength)
            {
                if (minimumLength < 0) throw new ArgumentOutOfRangeException();
                var cached = System.Threading.Interlocked.Exchange(ref _cached, null);
                return cached is not null && cached.Length >= minimumLength
                    ? cached
                    : new T[minimumLength];
            }

            public override void Return(T[] array, bool clearArray = false)
            {
                if (array == null) throw new ArgumentNullException();
                if (array.Length > _maxArrayLength) return;
                if (clearArray) Array.Clear(array, 0, array.Length);
                System.Threading.Interlocked.CompareExchange(ref _cached, array, null);
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
            return new SharedPool(maxArrayLength);
        }

        public abstract T[] Rent(int minimumLength);
        public abstract void Return(T[] array, bool clearArray = false);
    }

    public class SearchValues<T> where T : IEquatable<T>
    {
        private readonly T[] _values;
        private readonly StringComparison _stringComparison;

        internal SearchValues(T[] values, StringComparison stringComparison = StringComparison.Ordinal)
        {
            _values = values;
            _stringComparison = stringComparison;
        }

        public static SearchValues<T> Create(ReadOnlySpan<T> values) => new(values.ToArray());

        public bool Contains(T value)
        {
            for (var index = 0; index < _values.Length; index++)
            {
                if (typeof(T) == typeof(string))
                {
                    if (String.Equals(
                        (string?)(object?)_values[index],
                        (string?)(object?)value,
                        _stringComparison))
                    {
                        return true;
                    }
                }
                else if (EqualityComparer<T>.Default.Equals(_values[index], value))
                {
                    return true;
                }
            }

            return false;
        }

        internal int IndexOfAny(ReadOnlySpan<char> span)
        {
            if (typeof(T) != typeof(string))
            {
                return -1;
            }

            var values = (string[])(object)_values;
            for (var valueIndex = 0; valueIndex < values.Length; valueIndex++)
            {
                if (values[valueIndex].Length == 0) return 0;
            }
            for (var index = 0; index < span.Length; index++)
            {
                for (var valueIndex = 0; valueIndex < values.Length; valueIndex++)
                {
                    var value = values[valueIndex];
                    if (index + value.Length > span.Length)
                    {
                        continue;
                    }

                    var matched = true;
                    for (var offset = 0; offset < value.Length; offset++)
                    {
                        var source = span[index + offset];
                        var expected = value[offset];
                        if (_stringComparison == StringComparison.OrdinalIgnoreCase)
                        {
                            source = char.ToLowerInvariant(source);
                            expected = char.ToLowerInvariant(expected);
                        }
                        if (source != expected)
                        {
                            matched = false;
                            break;
                        }
                    }
                    if (matched)
                    {
                        return index;
                    }
                }
            }

            return -1;
        }
    }

    public static class SearchValues
    {
        public static SearchValues<byte> Create(params ReadOnlySpan<byte> values) =>
            SearchValues<byte>.Create(values);

        public static SearchValues<char> Create(params ReadOnlySpan<char> values) =>
            SearchValues<char>.Create(values);

        public static SearchValues<string> Create(ReadOnlySpan<string> values, StringComparison comparisonType)
        {
            if (comparisonType is not StringComparison.Ordinal and not StringComparison.OrdinalIgnoreCase)
            {
                throw new ArgumentException();
            }

            var copy = values.ToArray();
            for (var index = 0; index < copy.Length; index++)
            {
                if (copy[index] is null)
                {
                    throw new ArgumentNullException();
                }
            }
            return new SearchValues<string>(copy, comparisonType);
        }
    }
}

namespace System
{
    // Managed, allocation-free algorithms adapted from dotnet/runtime
    // System.Private.CoreLib MemoryExtensions (MIT). Runtime-specific SIMD,
    // globalization and vectorized helpers are intentionally omitted.
    public static partial class MemoryExtensions
    {
        public static Text.SpanLineEnumerator EnumerateLines(this ReadOnlySpan<char> span) =>
            new(span);

        public static Text.SpanLineEnumerator EnumerateLines(this Span<char> span) =>
            new((ReadOnlySpan<char>)span);

        private static bool Equal<T>(T left, T right, IEqualityComparer<T>? comparer) =>
            comparer?.Equals(left, right) ?? EqualityComparer<T>.Default.Equals(left, right);

        private static int Compare<T>(T left, T right, IComparer<T>? comparer) =>
            comparer?.Compare(left, right) ?? Comparer<T>.Default.Compare(left, right);

        public static Span<T> AsSpan<T>(this T[]? array) => new(array);
        public static Span<T> AsSpan<T>(this T[]? array, int start) =>
            array == null ? default : new(array, start, array.Length - start);
        public static Span<T> AsSpan<T>(this T[]? array, int start, int length) => new(array!, start, length);
        public static Span<T> AsSpan<T>(this T[]? array, Index start) =>
            array == null ? default : AsSpan(array, start.GetOffset(array.Length));
        public static Span<T> AsSpan<T>(this T[]? array, Range range) =>
            array == null ? default : new Span<T>(array, range.GetOffsetAndLength(array.Length).Item1, range.GetOffsetAndLength(array.Length).Item2);
        public static Span<T> AsSpan<T>(this ArraySegment<T> segment) =>
            new(segment.Array!, segment.Offset, segment.Count);
        public static Span<T> AsSpan<T>(this ArraySegment<T> segment, int start) =>
            new(segment.Array!, segment.Offset + start, segment.Count - start);
        public static Span<T> AsSpan<T>(this ArraySegment<T> segment, int start, int length) =>
            new(segment.Array!, segment.Offset + start, length);
        public static Span<T> AsSpan<T>(this ArraySegment<T> segment, Index start) =>
            AsSpan(segment, start.GetOffset(segment.Count));
        public static Span<T> AsSpan<T>(this ArraySegment<T> segment, Range range)
        {
            var offsets = range.GetOffsetAndLength(segment.Count);
            return AsSpan(segment, offsets.Item1, offsets.Item2);
        }
        // The upstream implementation projects the string's raw UTF-16 storage directly.
        // NetWasm's scalar string profile exposes character storage through String's
        // ordinary character-access contract, so materialize that immutable storage before
        // constructing the read-only view while preserving the upstream API semantics.
        public static ReadOnlySpan<char> AsSpan(this string? text) =>
            text is null ? default : new ReadOnlySpan<char>(text.ToCharArray());

        public static ReadOnlySpan<char> AsSpan(this string? text, int start)
        {
            if (text is null)
            {
                if (start != 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(start));
                }
                return default;
            }

            if ((uint)start > (uint)text.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(start));
            }

            return new ReadOnlySpan<char>(text.ToCharArray(), start, text.Length - start);
        }

        public static ReadOnlySpan<char> AsSpan(this string? text, int start, int length)
        {
            if (text is null)
            {
                if (start != 0 || length != 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(start));
                }
                return default;
            }

            if ((uint)start > (uint)text.Length ||
                (uint)length > (uint)(text.Length - start))
            {
                throw new ArgumentOutOfRangeException(nameof(start));
            }

            return new ReadOnlySpan<char>(text.ToCharArray(), start, length);
        }

        public static ReadOnlySpan<char> AsSpan(this string? text, Index startIndex)
        {
            if (text is null)
            {
                if (!startIndex.Equals(Index.Start))
                {
                    throw new ArgumentOutOfRangeException(nameof(startIndex));
                }
                return default;
            }

            var actualIndex = startIndex.GetOffset(text.Length);
            if ((uint)actualIndex > (uint)text.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(startIndex));
            }

            return new ReadOnlySpan<char>(text.ToCharArray(), actualIndex, text.Length - actualIndex);
        }

        public static ReadOnlySpan<char> AsSpan(this string? text, Range range)
        {
            if (text is null)
            {
                if (!range.Start.Equals(Index.Start) || !range.End.Equals(Index.Start))
                {
                    throw new ArgumentNullException(nameof(text));
                }
                return default;
            }

            var offsets = range.GetOffsetAndLength(text.Length);
            return new ReadOnlySpan<char>(text.ToCharArray(), offsets.Item1, offsets.Item2);
        }

        public static Memory<T> AsMemory<T>(this T[]? array) => new(array);
        public static Memory<T> AsMemory<T>(this T[]? array, int start) =>
            array == null ? default : new(array, start, array.Length - start);
        public static Memory<T> AsMemory<T>(this T[]? array, int start, int length) => new(array!, start, length);
        public static Memory<T> AsMemory<T>(this T[]? array, Index start) =>
            array == null ? default : AsMemory(array, start.GetOffset(array.Length));
        public static Memory<T> AsMemory<T>(this T[]? array, Range range)
        {
            if (array == null) return default;
            var offsets = range.GetOffsetAndLength(array.Length);
            return new Memory<T>(array, offsets.Item1, offsets.Item2);
        }
        public static Memory<T> AsMemory<T>(this ArraySegment<T> segment) =>
            new(segment.Array!, segment.Offset, segment.Count);
        public static Memory<T> AsMemory<T>(this ArraySegment<T> segment, int start) =>
            new(segment.Array!, segment.Offset + start, segment.Count - start);
        public static Memory<T> AsMemory<T>(this ArraySegment<T> segment, int start, int length) =>
            new(segment.Array!, segment.Offset + start, length);
        public static Memory<T> AsMemory<T>(this ArraySegment<T> segment, Index start) =>
            AsMemory(segment, start.GetOffset(segment.Count));
        public static Memory<T> AsMemory<T>(this ArraySegment<T> segment, Range range)
        {
            var offsets = range.GetOffsetAndLength(segment.Count);
            return AsMemory(segment, offsets.Item1, offsets.Item2);
        }
        public static ReadOnlyMemory<char> AsMemory(this string? text) =>
            text is null ? default : new ReadOnlyMemory<char>(text, 0, text.Length);

        public static ReadOnlyMemory<char> AsMemory(this string? text, int start)
        {
            if (text is null)
            {
                if (start != 0) throw new ArgumentOutOfRangeException(nameof(start));
                return default;
            }
            if ((uint)start > (uint)text.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(start));
            }
            return new ReadOnlyMemory<char>(text, start, text.Length - start);
        }

        public static ReadOnlyMemory<char> AsMemory(this string? text, int start, int length)
        {
            if (text is null)
            {
                if (start != 0 || length != 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(start));
                }
                return default;
            }
            return new ReadOnlyMemory<char>(text, start, length);
        }

        public static ReadOnlyMemory<char> AsMemory(this string? text, Index start)
        {
            if (text is null)
            {
                if (!start.Equals(Index.Start))
                {
                    throw new ArgumentOutOfRangeException(nameof(start));
                }
                return default;
            }
            return text.AsMemory(start.GetOffset(text.Length));
        }

        public static ReadOnlyMemory<char> AsMemory(this string? text, Range range)
        {
            if (text is null)
            {
                if (!range.Start.Equals(Index.Start) || !range.End.Equals(Index.Start))
                {
                    throw new ArgumentNullException(nameof(text));
                }
                return default;
            }
            var offsets = range.GetOffsetAndLength(text.Length);
            return new ReadOnlyMemory<char>(text, offsets.Item1, offsets.Item2);
        }

        public static int IndexOf<T>(this ReadOnlySpan<T> span, T value) where T : IEquatable<T>
        {
            for (var index = 0; index < span.Length; index++) if (Equal(span[index], value, null)) return index;
            return -1;
        }
        public static int IndexOf<T>(this Span<T> span, T value) where T : IEquatable<T> => ((ReadOnlySpan<T>)span).IndexOf(value);
        public static int IndexOf<T>(this ReadOnlySpan<T> span, T value, IEqualityComparer<T>? comparer = null)
        {
            for (var index = 0; index < span.Length; index++) if (Equal(span[index], value, comparer)) return index;
            return -1;
        }
        public static int IndexOf<T>(this Span<T> span, T value, IEqualityComparer<T>? comparer = null) => ((ReadOnlySpan<T>)span).IndexOf(value, comparer);
        public static int IndexOf<T>(this ReadOnlySpan<T> span, ReadOnlySpan<T> value) where T : IEquatable<T> => Find(span, value, false, null);
        public static int IndexOf<T>(this Span<T> span, ReadOnlySpan<T> value) where T : IEquatable<T> => ((ReadOnlySpan<T>)span).IndexOf(value);
        public static int IndexOf<T>(this ReadOnlySpan<T> span, ReadOnlySpan<T> value, IEqualityComparer<T>? comparer = null) => Find(span, value, false, comparer);
        public static int IndexOf<T>(this Span<T> span, ReadOnlySpan<T> value, IEqualityComparer<T>? comparer = null) => ((ReadOnlySpan<T>)span).IndexOf(value, comparer);

        public static int LastIndexOf<T>(this ReadOnlySpan<T> span, T value) where T : IEquatable<T>
        {
            for (var index = span.Length - 1; index >= 0; index--) if (Equal(span[index], value, null)) return index;
            return -1;
        }
        public static int LastIndexOf<T>(this Span<T> span, T value) where T : IEquatable<T> => ((ReadOnlySpan<T>)span).LastIndexOf(value);
        public static int LastIndexOf<T>(this ReadOnlySpan<T> span, T value, IEqualityComparer<T>? comparer = null)
        {
            for (var index = span.Length - 1; index >= 0; index--) if (Equal(span[index], value, comparer)) return index;
            return -1;
        }
        public static int LastIndexOf<T>(this Span<T> span, T value, IEqualityComparer<T>? comparer = null) => ((ReadOnlySpan<T>)span).LastIndexOf(value, comparer);
        public static int LastIndexOf<T>(this ReadOnlySpan<T> span, ReadOnlySpan<T> value) where T : IEquatable<T> => Find(span, value, true, null);
        public static int LastIndexOf<T>(this Span<T> span, ReadOnlySpan<T> value) where T : IEquatable<T> => ((ReadOnlySpan<T>)span).LastIndexOf(value);
        public static int LastIndexOf<T>(this ReadOnlySpan<T> span, ReadOnlySpan<T> value, IEqualityComparer<T>? comparer = null) => Find(span, value, true, comparer);
        public static int LastIndexOf<T>(this Span<T> span, ReadOnlySpan<T> value, IEqualityComparer<T>? comparer = null) => ((ReadOnlySpan<T>)span).LastIndexOf(value, comparer);

        private static int Find<T>(ReadOnlySpan<T> span, ReadOnlySpan<T> value, bool reverse, IEqualityComparer<T>? comparer)
        {
            if (value.Length == 0) return reverse ? span.Length : 0;
            if (value.Length > span.Length) return -1;
            if (reverse)
            {
                for (var index = span.Length - value.Length; index >= 0; index--)
                {
                    var matched = true;
                    for (var offset = 0; offset < value.Length; offset++) if (!Equal(span[index + offset], value[offset], comparer)) { matched = false; break; }
                    if (matched) return index;
                }
            }
            else
            {
                for (var index = 0; index <= span.Length - value.Length; index++)
                {
                    var matched = true;
                    for (var offset = 0; offset < value.Length; offset++) if (!Equal(span[index + offset], value[offset], comparer)) { matched = false; break; }
                    if (matched) return index;
                }
            }
            return -1;
        }

        public static bool Contains<T>(this ReadOnlySpan<T> span, T value) where T : IEquatable<T> => span.IndexOf(value) >= 0;
        public static bool Contains<T>(this Span<T> span, T value) where T : IEquatable<T> => span.IndexOf(value) >= 0;
        public static bool Contains<T>(this ReadOnlySpan<T> span, T value, IEqualityComparer<T>? comparer = null) => span.IndexOf(value, comparer) >= 0;
        public static bool Contains(this ReadOnlySpan<char> span, ReadOnlySpan<char> value, StringComparison comparisonType) =>
            span.IndexOf(value, comparisonType) >= 0;
        public static bool StartsWith<T>(this ReadOnlySpan<T> span, T value) where T : IEquatable<T> => span.Length != 0 && Equal(span[0], value, null);
        public static bool StartsWith<T>(this ReadOnlySpan<T> span, T value, IEqualityComparer<T>? comparer = null) => span.Length != 0 && Equal(span[0], value, comparer);
        public static bool StartsWith<T>(this Span<T> span, ReadOnlySpan<T> value) where T : IEquatable<T> => ((ReadOnlySpan<T>)span).StartsWith(value);
        public static bool StartsWith<T>(this ReadOnlySpan<T> span, ReadOnlySpan<T> value) where T : IEquatable<T> => Find(span, value, false, null) == 0;
        public static bool StartsWith<T>(this ReadOnlySpan<T> span, ReadOnlySpan<T> value, IEqualityComparer<T>? comparer = null) => Find(span, value, false, comparer) == 0;
        public static bool StartsWith(this ReadOnlySpan<char> span, ReadOnlySpan<char> value, StringComparison comparisonType)
        {
            ValidateOrdinalComparison(comparisonType);
            return value.Length <= span.Length && OrdinalEqualsAt(span, value, 0, comparisonType);
        }
        public static bool EndsWith<T>(this ReadOnlySpan<T> span, T value) where T : IEquatable<T> => span.Length != 0 && Equal(span[span.Length - 1], value, null);
        public static bool EndsWith<T>(this ReadOnlySpan<T> span, T value, IEqualityComparer<T>? comparer = null) => span.Length != 0 && Equal(span[span.Length - 1], value, comparer);
        public static bool EndsWith<T>(this ReadOnlySpan<T> span, ReadOnlySpan<T> value) where T : IEquatable<T> => value.Length <= span.Length && Find(span, value, true, null) == span.Length - value.Length;
        public static bool EndsWith<T>(this ReadOnlySpan<T> span, ReadOnlySpan<T> value, IEqualityComparer<T>? comparer = null) => value.Length <= span.Length && Find(span, value, true, comparer) == span.Length - value.Length;
        public static bool EndsWith<T>(this Span<T> span, ReadOnlySpan<T> value) where T : IEquatable<T> => ((ReadOnlySpan<T>)span).EndsWith(value);
        public static bool EndsWith(this ReadOnlySpan<char> span, ReadOnlySpan<char> value, StringComparison comparisonType)
        {
            ValidateOrdinalComparison(comparisonType);
            return value.Length <= span.Length &&
                OrdinalEqualsAt(span, value, span.Length - value.Length, comparisonType);
        }

        public static bool SequenceEqual<T>(this ReadOnlySpan<T> left, ReadOnlySpan<T> right) where T : IEquatable<T> => SequenceEqual(left, right, null);
        public static bool SequenceEqual<T>(this Span<T> left, ReadOnlySpan<T> right) where T : IEquatable<T> => SequenceEqual((ReadOnlySpan<T>)left, right, null);
        public static bool SequenceEqual<T>(this ReadOnlySpan<T> left, ReadOnlySpan<T> right, IEqualityComparer<T>? comparer = null)
        {
            if (left.Length != right.Length) return false;
            for (var index = 0; index < left.Length; index++) if (!Equal(left[index], right[index], comparer)) return false;
            return true;
        }
        public static bool SequenceEqual<T>(this Span<T> left, ReadOnlySpan<T> right, IEqualityComparer<T>? comparer = null) => SequenceEqual((ReadOnlySpan<T>)left, right, comparer);
        public static int SequenceCompareTo<T>(this ReadOnlySpan<T> left, ReadOnlySpan<T> right) where T : IComparable<T> => SequenceCompareTo(left, right, null);
        public static int SequenceCompareTo<T>(this Span<T> left, ReadOnlySpan<T> right) where T : IComparable<T> => SequenceCompareTo((ReadOnlySpan<T>)left, right, null);
        public static int SequenceCompareTo<T>(this ReadOnlySpan<T> left, ReadOnlySpan<T> right, IComparer<T>? comparer = null)
        {
            var length = left.Length < right.Length ? left.Length : right.Length;
            for (var index = 0; index < length; index++) { var result = Compare(left[index], right[index], comparer); if (result != 0) return result; }
            return left.Length.CompareTo(right.Length);
        }

        public static int CommonPrefixLength<T>(this ReadOnlySpan<T> left, ReadOnlySpan<T> right) => CommonPrefixLength(left, right, null);
        public static int CommonPrefixLength<T>(this Span<T> left, ReadOnlySpan<T> right) => CommonPrefixLength((ReadOnlySpan<T>)left, right, null);
        public static int CommonPrefixLength<T>(this ReadOnlySpan<T> left, ReadOnlySpan<T> right, IEqualityComparer<T>? comparer)
        { var length = left.Length < right.Length ? left.Length : right.Length; var index = 0; while (index < length && Equal(left[index], right[index], comparer)) index++; return index; }
        public static int CommonPrefixLength<T>(this Span<T> left, ReadOnlySpan<T> right, IEqualityComparer<T>? comparer) => CommonPrefixLength((ReadOnlySpan<T>)left, right, comparer);

        public static int Count<T>(this ReadOnlySpan<T> span, T value) where T : IEquatable<T> { var count = 0; for (var i = 0; i < span.Length; i++) if (Equal(span[i], value, null)) count++; return count; }
        public static int Count<T>(this Span<T> span, T value) where T : IEquatable<T> => ((ReadOnlySpan<T>)span).Count(value);
        public static int Count<T>(this ReadOnlySpan<T> span, T value, IEqualityComparer<T>? comparer = null) { var count = 0; for (var i = 0; i < span.Length; i++) if (Equal(span[i], value, comparer)) count++; return count; }
        public static int Count<T>(this ReadOnlySpan<T> span, ReadOnlySpan<T> value) where T : IEquatable<T> { if (value.Length == 0) return 0; var count = 0; for (var i = 0; i <= span.Length - value.Length; i++) if (Find(span.Slice(i), value, false, null) == 0) { count++; i += value.Length - 1; } return count; }
        public static int Count<T>(this ReadOnlySpan<T> span, ReadOnlySpan<T> value, IEqualityComparer<T>? comparer = null) { if (value.Length == 0) return 0; var count = 0; for (var i = 0; i <= span.Length - value.Length; i++) if (Find(span.Slice(i), value, false, comparer) == 0) { count++; i += value.Length - 1; } return count; }
        public static int Count<T>(this Span<T> span, ReadOnlySpan<T> value) where T : IEquatable<T> => ((ReadOnlySpan<T>)span).Count(value);
        public static int CountAny<T>(this ReadOnlySpan<T> span, params ReadOnlySpan<T> values) where T : IEquatable<T> { var count = 0; for (var i = 0; i < span.Length; i++) for (var j = 0; j < values.Length; j++) if (Equal(span[i], values[j], null)) { count++; break; } return count; }
        public static int CountAny<T>(this ReadOnlySpan<T> span, ReadOnlySpan<T> values, IEqualityComparer<T>? comparer = null) { var count = 0; for (var i = 0; i < span.Length; i++) for (var j = 0; j < values.Length; j++) if (Equal(span[i], values[j], comparer)) { count++; break; } return count; }
        public static int CountAny<T>(this ReadOnlySpan<T> span, System.Buffers.SearchValues<T> values) where T : IEquatable<T> { var count = 0; for (var i = 0; i < span.Length; i++) if (values.Contains(span[i])) count++; return count; }

        public static int IndexOfAny<T>(this ReadOnlySpan<T> span, T value0, T value1) where T : IEquatable<T> => AnyIndex(span, value0, value1, default, 2, false);
        public static int IndexOfAny<T>(this Span<T> span, T value0, T value1) where T : IEquatable<T> => ((ReadOnlySpan<T>)span).IndexOfAny(value0, value1);
        public static int IndexOfAny<T>(this ReadOnlySpan<T> span, T value0, T value1, T value2) where T : IEquatable<T> => AnyIndex(span, value0, value1, value2, 3, false);
        public static int IndexOfAny<T>(this Span<T> span, T value0, T value1, T value2) where T : IEquatable<T> => ((ReadOnlySpan<T>)span).IndexOfAny(value0, value1, value2);
        public static int IndexOfAny<T>(this ReadOnlySpan<T> span, ReadOnlySpan<T> values) where T : IEquatable<T> => AnyIndex(span, values, false);
        public static int IndexOfAny<T>(this Span<T> span, ReadOnlySpan<T> values) where T : IEquatable<T> => ((ReadOnlySpan<T>)span).IndexOfAny(values);
        public static int IndexOfAny<T>(this ReadOnlySpan<T> span, ReadOnlySpan<T> values, IEqualityComparer<T>? comparer = null) => AnyIndex(span, values, false, comparer);
        public static int IndexOfAny<T>(this ReadOnlySpan<T> span, System.Buffers.SearchValues<T> values) where T : IEquatable<T> { for (var i = 0; i < span.Length; i++) if (values.Contains(span[i])) return i; return -1; }
        public static int IndexOfAny<T>(this Span<T> span, System.Buffers.SearchValues<T> values) where T : IEquatable<T> => ((ReadOnlySpan<T>)span).IndexOfAny(values);
        public static int IndexOfAny(this ReadOnlySpan<char> span, System.Buffers.SearchValues<string> values)
        {
            if (values is null) throw new ArgumentNullException(nameof(values));
            return values.IndexOfAny(span);
        }
        public static int IndexOfAny(this Span<char> span, System.Buffers.SearchValues<string> values) =>
            ((ReadOnlySpan<char>)span).IndexOfAny(values);
        public static int IndexOfAny<T>(this ReadOnlySpan<T> span, T value0, T value1, IEqualityComparer<T>? comparer = null) => AnyIndex(span, value0, value1, default(T)!, 2, false, comparer);
        public static int IndexOfAny<T>(this ReadOnlySpan<T> span, T value0, T value1, T value2, IEqualityComparer<T>? comparer = null) => AnyIndex(span, value0, value1, value2, 3, false, comparer);
        public static int IndexOfAny<T>(this Span<T> span, T value0, T value1, T value2, IEqualityComparer<T>? comparer = null) => ((ReadOnlySpan<T>)span).IndexOfAny(value0, value1, value2, comparer);
        private static int AnyIndex<T>(ReadOnlySpan<T> span, T value0, T value1, T value2, int count, bool reverse, IEqualityComparer<T>? comparer = null) { var start = reverse ? span.Length - 1 : 0; var end = reverse ? -1 : span.Length; var step = reverse ? -1 : 1; for (var i = start; i != end; i += step) if (Equal(span[i], value0, comparer) || count > 1 && Equal(span[i], value1, comparer) || count > 2 && Equal(span[i], value2, comparer)) return i; return -1; }
        private static int AnyIndex<T>(ReadOnlySpan<T> span, ReadOnlySpan<T> values, bool reverse, IEqualityComparer<T>? comparer = null) { var start = reverse ? span.Length - 1 : 0; var end = reverse ? -1 : span.Length; var step = reverse ? -1 : 1; for (var i = start; i != end; i += step) for (var j = 0; j < values.Length; j++) if (Equal(span[i], values[j], comparer)) return i; return -1; }
        public static int LastIndexOfAny<T>(this ReadOnlySpan<T> span, T value0, T value1) where T : IEquatable<T> => AnyIndex(span, value0, value1, default, 2, true);
        public static int LastIndexOfAny<T>(this Span<T> span, T value0, T value1) where T : IEquatable<T> => ((ReadOnlySpan<T>)span).LastIndexOfAny(value0, value1);
        public static int LastIndexOfAny<T>(this ReadOnlySpan<T> span, T value0, T value1, T value2) where T : IEquatable<T> => AnyIndex(span, value0, value1, value2, 3, true);
        public static int LastIndexOfAny<T>(this Span<T> span, T value0, T value1, T value2) where T : IEquatable<T> => ((ReadOnlySpan<T>)span).LastIndexOfAny(value0, value1, value2);
        public static int LastIndexOfAny<T>(this ReadOnlySpan<T> span, ReadOnlySpan<T> values) where T : IEquatable<T> => AnyIndex(span, values, true);
        public static int LastIndexOfAny<T>(this Span<T> span, ReadOnlySpan<T> values) where T : IEquatable<T> => ((ReadOnlySpan<T>)span).LastIndexOfAny(values);
        public static int LastIndexOfAny<T>(this ReadOnlySpan<T> span, ReadOnlySpan<T> values, IEqualityComparer<T>? comparer = null) => AnyIndex(span, values, true, comparer);
        public static int LastIndexOfAny<T>(this ReadOnlySpan<T> span, System.Buffers.SearchValues<T> values) where T : IEquatable<T> { for (var i = span.Length - 1; i >= 0; i--) if (values.Contains(span[i])) return i; return -1; }
        public static int LastIndexOfAny<T>(this Span<T> span, System.Buffers.SearchValues<T> values) where T : IEquatable<T> => ((ReadOnlySpan<T>)span).LastIndexOfAny(values);
        public static int LastIndexOfAny<T>(this ReadOnlySpan<T> span, T value0, T value1, IEqualityComparer<T>? comparer = null) => AnyIndex(span, value0, value1, default(T)!, 2, true, comparer);
        public static int LastIndexOfAny<T>(this ReadOnlySpan<T> span, T value0, T value1, T value2, IEqualityComparer<T>? comparer = null) => AnyIndex(span, value0, value1, value2, 3, true, comparer);
        public static int LastIndexOfAny<T>(this Span<T> span, T value0, T value1, T value2, IEqualityComparer<T>? comparer = null) => ((ReadOnlySpan<T>)span).LastIndexOfAny(value0, value1, value2, comparer);

        public static int IndexOfAnyInRange<T>(this ReadOnlySpan<T> span, T minimum, T maximum) where T : IComparable<T> { for (var i = 0; i < span.Length; i++) if (Compare(span[i], minimum, null) >= 0 && Compare(span[i], maximum, null) <= 0) return i; return -1; }
        public static int IndexOfAnyInRange<T>(this Span<T> span, T minimum, T maximum) where T : IComparable<T> => ((ReadOnlySpan<T>)span).IndexOfAnyInRange(minimum, maximum);
        public static int LastIndexOfAnyInRange<T>(this ReadOnlySpan<T> span, T minimum, T maximum) where T : IComparable<T> { for (var i = span.Length - 1; i >= 0; i--) if (Compare(span[i], minimum, null) >= 0 && Compare(span[i], maximum, null) <= 0) return i; return -1; }
        public static int LastIndexOfAnyInRange<T>(this Span<T> span, T minimum, T maximum) where T : IComparable<T> => ((ReadOnlySpan<T>)span).LastIndexOfAnyInRange(minimum, maximum);
        public static bool ContainsAnyInRange<T>(this ReadOnlySpan<T> span, T minimum, T maximum) where T : IComparable<T> => span.IndexOfAnyInRange(minimum, maximum) >= 0;
        public static bool ContainsAnyInRange<T>(this Span<T> span, T minimum, T maximum) where T : IComparable<T> => span.IndexOfAnyInRange(minimum, maximum) >= 0;
        public static int IndexOfAnyExceptInRange<T>(this ReadOnlySpan<T> span, T minimum, T maximum) where T : IComparable<T> { for (var i = 0; i < span.Length; i++) if (Compare(span[i], minimum, null) < 0 || Compare(span[i], maximum, null) > 0) return i; return -1; }
        public static int IndexOfAnyExceptInRange<T>(this Span<T> span, T minimum, T maximum) where T : IComparable<T> => ((ReadOnlySpan<T>)span).IndexOfAnyExceptInRange(minimum, maximum);
        public static int LastIndexOfAnyExceptInRange<T>(this ReadOnlySpan<T> span, T minimum, T maximum) where T : IComparable<T> { for (var i = span.Length - 1; i >= 0; i--) if (Compare(span[i], minimum, null) < 0 || Compare(span[i], maximum, null) > 0) return i; return -1; }
        public static int LastIndexOfAnyExceptInRange<T>(this Span<T> span, T minimum, T maximum) where T : IComparable<T> => ((ReadOnlySpan<T>)span).LastIndexOfAnyExceptInRange(minimum, maximum);
        public static bool ContainsAnyExceptInRange<T>(this ReadOnlySpan<T> span, T minimum, T maximum) where T : IComparable<T> => span.IndexOfAnyExceptInRange(minimum, maximum) >= 0;
        public static bool ContainsAnyExceptInRange<T>(this Span<T> span, T minimum, T maximum) where T : IComparable<T> => span.IndexOfAnyExceptInRange(minimum, maximum) >= 0;

        public static bool ContainsAny<T>(this ReadOnlySpan<T> span, T value0, T value1) where T : IEquatable<T> => span.IndexOfAny(value0, value1) >= 0;
        public static bool ContainsAny<T>(this Span<T> span, T value0, T value1) where T : IEquatable<T> => span.IndexOfAny(value0, value1) >= 0;
        public static bool ContainsAny<T>(this Span<T> span, T value0, T value1, T value2) where T : IEquatable<T> => span.IndexOfAny(value0, value1, value2) >= 0;
        public static bool ContainsAny<T>(this ReadOnlySpan<T> span, ReadOnlySpan<T> values) where T : IEquatable<T> => span.IndexOfAny(values) >= 0;
        public static bool ContainsAny<T>(this Span<T> span, ReadOnlySpan<T> values) where T : IEquatable<T> => ((ReadOnlySpan<T>)span).ContainsAny(values);
        public static bool ContainsAny<T>(this ReadOnlySpan<T> span, ReadOnlySpan<T> values, IEqualityComparer<T>? comparer = null) => span.IndexOfAny(values, comparer) >= 0;
        public static bool ContainsAny<T>(this ReadOnlySpan<T> span, System.Buffers.SearchValues<T> values) where T : IEquatable<T> => span.IndexOfAny(values) >= 0;
        public static bool ContainsAny<T>(this Span<T> span, System.Buffers.SearchValues<T> values) where T : IEquatable<T> => span.IndexOfAny(values) >= 0;
        public static bool ContainsAny(this ReadOnlySpan<char> span, System.Buffers.SearchValues<string> values) => span.IndexOfAny(values) >= 0;
        public static bool ContainsAny(this Span<char> span, System.Buffers.SearchValues<string> values) => span.IndexOfAny(values) >= 0;
        public static bool ContainsAny<T>(this ReadOnlySpan<T> span, T value0, T value1, IEqualityComparer<T>? comparer = null) => span.IndexOfAny(value0, value1, comparer) >= 0;
        public static bool ContainsAny<T>(this ReadOnlySpan<T> span, T value0, T value1, T value2, IEqualityComparer<T>? comparer = null) => span.IndexOfAny(value0, value1, value2, comparer) >= 0;
        public static bool ContainsAny<T>(this Span<T> span, T value0, T value1, T value2, IEqualityComparer<T>? comparer = null) => ((ReadOnlySpan<T>)span).ContainsAny(value0, value1, value2, comparer);
        public static bool ContainsAny<T>(this ReadOnlySpan<T> span, T value0, T value1, T value2) where T : IEquatable<T> => span.IndexOfAny(value0, value1, value2) >= 0;

        public static int IndexOfAnyExcept<T>(this ReadOnlySpan<T> span, T value) where T : IEquatable<T> { for (var i = 0; i < span.Length; i++) if (!Equal(span[i], value, null)) return i; return -1; }
        public static int IndexOfAnyExcept<T>(this Span<T> span, T value) where T : IEquatable<T> => ((ReadOnlySpan<T>)span).IndexOfAnyExcept(value);
        public static int IndexOfAnyExcept<T>(this ReadOnlySpan<T> span, T value, IEqualityComparer<T>? comparer = null) { for (var i = 0; i < span.Length; i++) if (!Equal(span[i], value, comparer)) return i; return -1; }
        public static int IndexOfAnyExcept<T>(this ReadOnlySpan<T> span, T value0, T value1) where T : IEquatable<T> => AnyExceptIndex(span, value0, value1, default(T)!, 2, false, null);
        public static int IndexOfAnyExcept<T>(this Span<T> span, T value0, T value1) where T : IEquatable<T> => ((ReadOnlySpan<T>)span).IndexOfAnyExcept(value0, value1);
        public static int IndexOfAnyExcept<T>(this ReadOnlySpan<T> span, T value0, T value1, T value2) where T : IEquatable<T> => AnyExceptIndex(span, value0, value1, value2, 3, false, null);
        public static int IndexOfAnyExcept<T>(this Span<T> span, T value0, T value1, T value2) where T : IEquatable<T> => ((ReadOnlySpan<T>)span).IndexOfAnyExcept(value0, value1, value2);
        public static int IndexOfAnyExcept<T>(this ReadOnlySpan<T> span, T value0, T value1, IEqualityComparer<T>? comparer = null) => AnyExceptIndex(span, value0, value1, default(T)!, 2, false, comparer);
        public static int IndexOfAnyExcept<T>(this ReadOnlySpan<T> span, T value0, T value1, T value2, IEqualityComparer<T>? comparer = null) => AnyExceptIndex(span, value0, value1, value2, 3, false, comparer);
        public static int IndexOfAnyExcept<T>(this ReadOnlySpan<T> span, ReadOnlySpan<T> values) where T : IEquatable<T> { for (var i = 0; i < span.Length; i++) if (span.Slice(i, 1).IndexOfAny(values) < 0) return i; return -1; }
        public static int IndexOfAnyExcept<T>(this Span<T> span, ReadOnlySpan<T> values) where T : IEquatable<T> => ((ReadOnlySpan<T>)span).IndexOfAnyExcept(values);
        public static int IndexOfAnyExcept<T>(this ReadOnlySpan<T> span, ReadOnlySpan<T> values, IEqualityComparer<T>? comparer = null) { for (var i = 0; i < span.Length; i++) if (span.Slice(i, 1).IndexOfAny(values, comparer) < 0) return i; return -1; }
        public static int IndexOfAnyExcept<T>(this ReadOnlySpan<T> span, System.Buffers.SearchValues<T> values) where T : IEquatable<T> { for (var i = 0; i < span.Length; i++) if (!values.Contains(span[i])) return i; return -1; }
        public static int IndexOfAnyExcept<T>(this Span<T> span, System.Buffers.SearchValues<T> values) where T : IEquatable<T> => ((ReadOnlySpan<T>)span).IndexOfAnyExcept(values);
        public static int LastIndexOfAnyExcept<T>(this ReadOnlySpan<T> span, T value) where T : IEquatable<T> { for (var i = span.Length - 1; i >= 0; i--) if (!Equal(span[i], value, null)) return i; return -1; }
        public static int LastIndexOfAnyExcept<T>(this Span<T> span, T value) where T : IEquatable<T> => ((ReadOnlySpan<T>)span).LastIndexOfAnyExcept(value);
        public static int LastIndexOfAnyExcept<T>(this ReadOnlySpan<T> span, T value, IEqualityComparer<T>? comparer = null) { for (var i = span.Length - 1; i >= 0; i--) if (!Equal(span[i], value, comparer)) return i; return -1; }
        public static int LastIndexOfAnyExcept<T>(this ReadOnlySpan<T> span, T value0, T value1) where T : IEquatable<T> => AnyExceptIndex(span, value0, value1, default(T)!, 2, true, null);
        public static int LastIndexOfAnyExcept<T>(this Span<T> span, T value0, T value1) where T : IEquatable<T> => ((ReadOnlySpan<T>)span).LastIndexOfAnyExcept(value0, value1);
        public static int LastIndexOfAnyExcept<T>(this ReadOnlySpan<T> span, T value0, T value1, T value2) where T : IEquatable<T> => AnyExceptIndex(span, value0, value1, value2, 3, true, null);
        public static int LastIndexOfAnyExcept<T>(this Span<T> span, T value0, T value1, T value2) where T : IEquatable<T> => ((ReadOnlySpan<T>)span).LastIndexOfAnyExcept(value0, value1, value2);
        public static int LastIndexOfAnyExcept<T>(this ReadOnlySpan<T> span, T value0, T value1, IEqualityComparer<T>? comparer = null) => AnyExceptIndex(span, value0, value1, default(T)!, 2, true, comparer);
        public static int LastIndexOfAnyExcept<T>(this ReadOnlySpan<T> span, T value0, T value1, T value2, IEqualityComparer<T>? comparer = null) => AnyExceptIndex(span, value0, value1, value2, 3, true, comparer);
        public static int LastIndexOfAnyExcept<T>(this ReadOnlySpan<T> span, ReadOnlySpan<T> values) where T : IEquatable<T> { for (var i = span.Length - 1; i >= 0; i--) if (span.Slice(i, 1).IndexOfAny(values) < 0) return i; return -1; }
        public static int LastIndexOfAnyExcept<T>(this Span<T> span, ReadOnlySpan<T> values) where T : IEquatable<T> => ((ReadOnlySpan<T>)span).LastIndexOfAnyExcept(values);
        public static int LastIndexOfAnyExcept<T>(this ReadOnlySpan<T> span, ReadOnlySpan<T> values, IEqualityComparer<T>? comparer = null) { for (var i = span.Length - 1; i >= 0; i--) if (span.Slice(i, 1).IndexOfAny(values, comparer) < 0) return i; return -1; }
        public static int LastIndexOfAnyExcept<T>(this ReadOnlySpan<T> span, System.Buffers.SearchValues<T> values) where T : IEquatable<T> { for (var i = span.Length - 1; i >= 0; i--) if (!values.Contains(span[i])) return i; return -1; }
        public static int LastIndexOfAnyExcept<T>(this Span<T> span, System.Buffers.SearchValues<T> values) where T : IEquatable<T> => ((ReadOnlySpan<T>)span).LastIndexOfAnyExcept(values);
        private static int AnyExceptIndex<T>(ReadOnlySpan<T> span, T value0, T value1, T value2, int count, bool reverse, IEqualityComparer<T>? comparer)
        {
            var start = reverse ? span.Length - 1 : 0;
            var end = reverse ? -1 : span.Length;
            var step = reverse ? -1 : 1;
            for (var index = start; index != end; index += step)
            {
                var value = span[index];
                if (!Equal(value, value0, comparer) && (count < 2 || !Equal(value, value1, comparer)) && (count < 3 || !Equal(value, value2, comparer))) return index;
            }
            return -1;
        }
        public static bool ContainsAnyExcept<T>(this ReadOnlySpan<T> span, T value) where T : IEquatable<T> => span.IndexOfAnyExcept(value) >= 0;
        public static bool ContainsAnyExcept<T>(this Span<T> span, T value) where T : IEquatable<T> => span.IndexOfAnyExcept(value) >= 0;
        public static bool ContainsAnyExcept<T>(this ReadOnlySpan<T> span, T value, IEqualityComparer<T>? comparer = null) => span.IndexOfAnyExcept(value, comparer) >= 0;
        public static bool ContainsAnyExcept<T>(this ReadOnlySpan<T> span, ReadOnlySpan<T> values) where T : IEquatable<T> => span.IndexOfAnyExcept(values) >= 0;
        public static bool ContainsAnyExcept<T>(this Span<T> span, ReadOnlySpan<T> values) where T : IEquatable<T> => ((ReadOnlySpan<T>)span).ContainsAnyExcept(values);
        public static bool ContainsAnyExcept<T>(this ReadOnlySpan<T> span, ReadOnlySpan<T> values, IEqualityComparer<T>? comparer = null) => span.IndexOfAnyExcept(values, comparer) >= 0;
        public static bool ContainsAnyExcept<T>(this ReadOnlySpan<T> span, System.Buffers.SearchValues<T> values) where T : IEquatable<T> => span.IndexOfAnyExcept(values) >= 0;
        public static bool ContainsAnyExcept<T>(this Span<T> span, System.Buffers.SearchValues<T> values) where T : IEquatable<T> => span.IndexOfAnyExcept(values) >= 0;
        public static bool ContainsAnyExcept<T>(this ReadOnlySpan<T> span, T value0, T value1) where T : IEquatable<T> => span.IndexOfAnyExcept(value0, value1) >= 0;
        public static bool ContainsAnyExcept<T>(this Span<T> span, T value0, T value1) where T : IEquatable<T> => span.IndexOfAnyExcept(value0, value1) >= 0;
        public static bool ContainsAnyExcept<T>(this ReadOnlySpan<T> span, T value0, T value1, IEqualityComparer<T>? comparer = null) => span.IndexOfAnyExcept(value0, value1, comparer) >= 0;
        public static bool ContainsAnyExcept<T>(this ReadOnlySpan<T> span, T value0, T value1, T value2) where T : IEquatable<T> => span.IndexOfAnyExcept(value0, value1, value2) >= 0;
        public static bool ContainsAnyExcept<T>(this Span<T> span, T value0, T value1, T value2) where T : IEquatable<T> => span.IndexOfAnyExcept(value0, value1, value2) >= 0;
        public static bool ContainsAnyExcept<T>(this ReadOnlySpan<T> span, T value0, T value1, T value2, IEqualityComparer<T>? comparer = null) => span.IndexOfAnyExcept(value0, value1, value2, comparer) >= 0;

        public static int IndexOfAnyWhiteSpace(this ReadOnlySpan<char> span) { for (var i = 0; i < span.Length; i++) if (IsWhiteSpace(span[i])) return i; return -1; }
        public static int LastIndexOfAnyWhiteSpace(this ReadOnlySpan<char> span) { for (var i = span.Length - 1; i >= 0; i--) if (IsWhiteSpace(span[i])) return i; return -1; }
        public static int IndexOfAnyExceptWhiteSpace(this ReadOnlySpan<char> span) { for (var i = 0; i < span.Length; i++) if (!IsWhiteSpace(span[i])) return i; return -1; }
        public static int LastIndexOfAnyExceptWhiteSpace(this ReadOnlySpan<char> span) { for (var i = span.Length - 1; i >= 0; i--) if (!IsWhiteSpace(span[i])) return i; return -1; }
        public static bool ContainsAnyWhiteSpace(this ReadOnlySpan<char> span) => span.IndexOfAnyWhiteSpace() >= 0;
        public static bool IsWhiteSpace(this ReadOnlySpan<char> span) => span.IndexOfAnyExceptWhiteSpace() < 0;

        public static int BinarySearch<T>(this Span<T> span, T value) where T : IComparable<T> => BinarySearch((ReadOnlySpan<T>)span, value, null);
        public static int BinarySearch<T>(this ReadOnlySpan<T> span, T value) where T : IComparable<T> => BinarySearch(span, value, null);
        public static int BinarySearch<T>(this Span<T> span, T value, IComparer<T>? comparer = null) => BinarySearch((ReadOnlySpan<T>)span, value, comparer);
        public static int BinarySearch<T>(this ReadOnlySpan<T> span, T value, IComparer<T>? comparer = null)
        { var low = 0; var high = span.Length - 1; while (low <= high) { var mid = low + ((high - low) >> 1); var result = Compare(span[mid], value, comparer); if (result == 0) return mid; if (result < 0) low = mid + 1; else high = mid - 1; } return ~low; }
        public static int BinarySearch<T>(this Span<T> span, IComparable<T> value) => BinarySearch((ReadOnlySpan<T>)span, value);
        public static int BinarySearch<T>(this ReadOnlySpan<T> span, IComparable<T> value)
        {
            if (value is null) throw new ArgumentNullException();
            var low = 0;
            var high = span.Length - 1;
            while (low <= high)
            {
                var mid = low + ((high - low) >> 1);
                var result = value.CompareTo(span[mid]);
                if (result == 0) return mid;
                if (result > 0) low = mid + 1;
                else high = mid - 1;
            }
            return ~low;
        }
        public static int BinarySearch<T, TComparable>(this Span<T> span, TComparable value) where TComparable : IComparable<T>, allows ref struct => BinarySearch((ReadOnlySpan<T>)span, value);
        public static int BinarySearch<T, TComparable>(this ReadOnlySpan<T> span, TComparable value) where TComparable : IComparable<T>, allows ref struct { var low = 0; var high = span.Length - 1; while (low <= high) { var mid = low + ((high - low) >> 1); var result = value.CompareTo(span[mid]); if (result == 0) return mid; if (result > 0) low = mid + 1; else high = mid - 1; } return ~low; }
        public static int BinarySearch<T, TComparer>(this Span<T> span, T value, TComparer comparer) where TComparer : IComparer<T>, allows ref struct => BinarySearch((ReadOnlySpan<T>)span, value, comparer);
        public static int BinarySearch<T, TComparer>(this ReadOnlySpan<T> span, T value, TComparer comparer) where TComparer : IComparer<T>, allows ref struct
        {
            var low = 0;
            var high = span.Length - 1;
            while (low <= high)
            {
                var mid = low + ((high - low) >> 1);
                var result = comparer.Compare(span[mid], value);
                if (result == 0) return mid;
                if (result < 0) low = mid + 1;
                else high = mid - 1;
            }
            return ~low;
        }

        public static T Min<T>(this ReadOnlySpan<T> span) => Min(span, null);
        public static T Min<T>(this ReadOnlySpan<T> span, IComparer<T>? comparer)
        { if (span.Length == 0) throw new InvalidOperationException(); var result = span[0]; for (var i = 1; i < span.Length; i++) if (Compare(span[i], result, comparer) < 0) result = span[i]; return result; }
        public static T Max<T>(this ReadOnlySpan<T> span) => Max(span, null);
        public static T Max<T>(this ReadOnlySpan<T> span, IComparer<T>? comparer)
        { if (span.Length == 0) throw new InvalidOperationException(); var result = span[0]; for (var i = 1; i < span.Length; i++) if (Compare(span[i], result, comparer) > 0) result = span[i]; return result; }

        public static void Reverse<T>(this Span<T> span) { var left = 0; var right = span.Length - 1; while (left < right) { var value = span[left]; span[left] = span[right]; span[right] = value; left++; right--; } }
        public static void Replace<T>(this Span<T> span, T oldValue, T newValue) where T : IEquatable<T> => Replace(span, oldValue, newValue, null);
        public static void Replace<T>(this Span<T> span, T oldValue, T newValue, IEqualityComparer<T>? comparer = null) { for (var i = 0; i < span.Length; i++) if (Equal(span[i], oldValue, comparer)) span[i] = newValue; }
        public static void Replace<T>(this ReadOnlySpan<T> source, Span<T> destination, T oldValue, T newValue) where T : IEquatable<T> => Replace(source, destination, oldValue, newValue, null);
        public static void Replace<T>(this ReadOnlySpan<T> source, Span<T> destination, T oldValue, T newValue, IEqualityComparer<T>? comparer = null) { if (destination.Length < source.Length) throw new ArgumentException(); for (var i = 0; i < source.Length; i++) destination[i] = Equal(source[i], oldValue, comparer) ? newValue : source[i]; }
        public static void ReplaceAny<T>(this Span<T> span, System.Buffers.SearchValues<T> values, T newValue) where T : IEquatable<T> { for (var i = 0; i < span.Length; i++) if (values.Contains(span[i])) span[i] = newValue; }
        public static void ReplaceAny<T>(this ReadOnlySpan<T> source, Span<T> destination, System.Buffers.SearchValues<T> values, T newValue) where T : IEquatable<T> { if (destination.Length < source.Length) throw new ArgumentException(); for (var i = 0; i < source.Length; i++) destination[i] = values.Contains(source[i]) ? newValue : source[i]; }
        public static void ReplaceAnyExcept<T>(this Span<T> span, System.Buffers.SearchValues<T> values, T newValue) where T : IEquatable<T> { for (var i = 0; i < span.Length; i++) if (!values.Contains(span[i])) span[i] = newValue; }
        public static void ReplaceAnyExcept<T>(this ReadOnlySpan<T> source, Span<T> destination, System.Buffers.SearchValues<T> values, T newValue) where T : IEquatable<T> { if (destination.Length < source.Length) throw new ArgumentException(); for (var i = 0; i < source.Length; i++) destination[i] = values.Contains(source[i]) ? source[i] : newValue; }

        public static void Sort<T>(this Span<T> span) => Sort(span, Comparer<T>.Default);
        public static void Sort<T>(this Span<T> span, Comparison<T> comparison) { for (var i = 1; i < span.Length; i++) { var value = span[i]; var j = i - 1; while (j >= 0 && comparison(span[j], value) > 0) { span[j + 1] = span[j]; j--; } span[j + 1] = value; } }
        public static void Sort<T, TComparer>(this Span<T> span, TComparer comparer) where TComparer : IComparer<T> => Sort(span, (IComparer<T>)comparer);
        public static void Sort<T>(this Span<T> span, IComparer<T>? comparer = null) { comparer ??= Comparer<T>.Default; Sort(span, (x, y) => comparer.Compare(x, y)); }
        public static void Sort<TKey, TValue>(this Span<TKey> keys, Span<TValue> values) => Sort(keys, values, Comparer<TKey>.Default);
        public static void Sort<TKey, TValue>(this Span<TKey> keys, Span<TValue> values, Comparison<TKey> comparison) { if (keys.Length != values.Length) throw new ArgumentException(); for (var i = 1; i < keys.Length; i++) { var key = keys[i]; var value = values[i]; var j = i - 1; while (j >= 0 && comparison(keys[j], key) > 0) { keys[j + 1] = keys[j]; values[j + 1] = values[j]; j--; } keys[j + 1] = key; values[j + 1] = value; } }
        public static void Sort<TKey, TValue, TComparer>(this Span<TKey> keys, Span<TValue> values, TComparer comparer) where TComparer : IComparer<TKey> => Sort(keys, values, comparer.Compare);
        public static void Sort<TKey, TValue>(this Span<TKey> keys, Span<TValue> values, IComparer<TKey>? comparer) { comparer ??= Comparer<TKey>.Default; Sort(keys, values, comparer.Compare); }

        public static void CopyTo<T>(this T[] source, Span<T> destination) => new ReadOnlySpan<T>(source).CopyTo(destination);
        public static void CopyTo<T>(this T[] source, Memory<T> destination) => new ReadOnlySpan<T>(source).CopyTo(destination.Span);

        public static bool Overlaps<T>(this Span<T> first, ReadOnlySpan<T> second)
            => ((ReadOnlySpan<T>)first).Overlaps(second);

        public static bool Overlaps<T>(
            this Span<T> first,
            ReadOnlySpan<T> second,
            out int elementOffset)
            => ((ReadOnlySpan<T>)first).Overlaps(
                second,
                out elementOffset);

        public static bool Overlaps<T>(
            this ReadOnlySpan<T> first,
            ReadOnlySpan<T> second)
        {
            if (first.IsEmpty || second.IsEmpty)
            {
                return false;
            }

            var elementSize = (nuint)Unsafe.SizeOf<T>();
            var byteOffset = Unsafe.ByteOffset(
                in MemoryMarshal.GetReference(first),
                in MemoryMarshal.GetReference(second));
            return (nuint)byteOffset < (nuint)first.Length * elementSize ||
                (nuint)(-byteOffset) < (nuint)second.Length * elementSize;
        }

        public static bool Overlaps<T>(
            this ReadOnlySpan<T> first,
            ReadOnlySpan<T> second,
            out int elementOffset)
        {
            if (first.IsEmpty || second.IsEmpty)
            {
                elementOffset = 0;
                return false;
            }

            var elementSize = (nuint)Unsafe.SizeOf<T>();
            var byteOffset = Unsafe.ByteOffset(
                in MemoryMarshal.GetReference(first),
                in MemoryMarshal.GetReference(second));
            if ((nuint)byteOffset >= (nuint)first.Length * elementSize &&
                (nuint)(-byteOffset) >= (nuint)second.Length * elementSize)
            {
                elementOffset = 0;
                return false;
            }
            if (byteOffset % (nint)elementSize != 0)
            {
                throw new ArgumentException();
            }

            elementOffset = (int)(byteOffset / (nint)elementSize);
            return true;
        }

        public static int ToLowerInvariant(this ReadOnlySpan<char> source, Span<char> destination) => ToCase(source, destination, false);
        public static int ToLowerOrdinal(this ReadOnlySpan<char> source, Span<char> destination) => ToCase(source, destination, false);
        public static int ToUpperInvariant(this ReadOnlySpan<char> source, Span<char> destination) => ToCase(source, destination, true);
        public static int ToUpperOrdinal(this ReadOnlySpan<char> source, Span<char> destination) => ToCase(source, destination, true);
        private static int ToCase(ReadOnlySpan<char> source, Span<char> destination, bool upper)
        {
            if (destination.Length < source.Length) throw new ArgumentException();
            for (var i = 0; i < source.Length; i++)
            {
                destination[i] = upper
                    ? char.ToUpperInvariant(source[i])
                    : char.ToLowerInvariant(source[i]);
            }
            return source.Length;
        }

        public ref struct SpanSplitEnumerator<T> : IEnumerator<Range>, System.Collections.IEnumerator, IDisposable where T : IEquatable<T>
        {
            private readonly ReadOnlySpan<T> _source;
            private readonly T _separator;
            private readonly ReadOnlySpan<T> _separatorBuffer;
            private readonly System.Buffers.SearchValues<T>? _searchValues;
            private int _mode;
            private int _startCurrent;
            private int _endCurrent;
            private int _startNext;

            internal SpanSplitEnumerator(ReadOnlySpan<T> source, T separator)
            { _source = source; _separator = separator; _separatorBuffer = default; _searchValues = null; _mode = 1; _startCurrent = _endCurrent = _startNext = 0; }
            internal SpanSplitEnumerator(ReadOnlySpan<T> source, ReadOnlySpan<T> separators)
            { _source = source; _separator = default(T)!; _separatorBuffer = separators; _searchValues = null; _mode = separators.Length == 0 && typeof(T) == typeof(char) ? 6 : 2; _startCurrent = _endCurrent = _startNext = 0; }
            internal SpanSplitEnumerator(ReadOnlySpan<T> source, ReadOnlySpan<T> separator, bool treatAsSingleSeparator)
            { _source = source; _separator = default(T)!; _separatorBuffer = separator; _searchValues = null; _mode = treatAsSingleSeparator ? (separator.Length == 0 ? 5 : 3) : 2; _startCurrent = _endCurrent = _startNext = 0; }
            internal SpanSplitEnumerator(ReadOnlySpan<T> source, System.Buffers.SearchValues<T> searchValues)
            { _source = source; _separator = default(T)!; _separatorBuffer = default; _searchValues = searchValues; _mode = 4; _startCurrent = _endCurrent = _startNext = 0; }

            public SpanSplitEnumerator<T> GetEnumerator() => this;
            public readonly ReadOnlySpan<T> Source
            {
                get => _source;
            }
            public readonly Range Current
            {
                get => new Range(_startCurrent, _endCurrent);
            }
            object System.Collections.IEnumerator.Current => Current;
            void System.Collections.IEnumerator.Reset() => throw new NotSupportedException();
            void IDisposable.Dispose() { }

            public bool MoveNext()
            {
                if (_mode == 0) return false;
                var remaining = _source.Slice(_startNext);
                var separatorIndex = _mode switch
                {
                    1 => remaining.IndexOf(_separator),
                    2 => remaining.IndexOfAny(_separatorBuffer),
                    3 => remaining.IndexOf(_separatorBuffer),
                    4 => remaining.IndexOfAny(_searchValues!),
                    5 => -1,
                    6 => FindWhitespace(remaining),
                    _ => -1
                };
                var separatorLength = _mode == 3 ? _separatorBuffer.Length : 1;
                _startCurrent = _startNext;
                if (separatorIndex >= 0)
                {
                    _endCurrent = _startCurrent + separatorIndex;
                    _startNext = _endCurrent + separatorLength;
                }
                else
                {
                    _startNext = _endCurrent = _source.Length;
                    _mode = 0;
                }
                return true;
            }

            private static int FindWhitespace(ReadOnlySpan<T> span)
            {
                if (typeof(T) != typeof(char)) return -1;
                for (var index = 0; index < span.Length; index++)
                {
                    if (IsWhiteSpace((char)(object)span[index]!)) return index;
                }
                return -1;
            }
        }
        public static SpanSplitEnumerator<T> Split<T>(this ReadOnlySpan<T> span, T separator) where T : IEquatable<T> => new(span, separator);
        public static SpanSplitEnumerator<T> Split<T>(this ReadOnlySpan<T> span, ReadOnlySpan<T> separator) where T : IEquatable<T> => new(span, separator, true);
        public static SpanSplitEnumerator<T> SplitAny<T>(this ReadOnlySpan<T> span, [System.Diagnostics.CodeAnalysis.UnscopedRef] params ReadOnlySpan<T> separators) where T : IEquatable<T> => new(span, separators);
        public static SpanSplitEnumerator<T> SplitAny<T>(this ReadOnlySpan<T> span, System.Buffers.SearchValues<T> separators) where T : IEquatable<T> => new(span, separators);

        private static int SplitInto(ReadOnlySpan<char> source, Span<Range> destination, ReadOnlySpan<char> separators, bool anySeparator, StringSplitOptions options)
        {
            const StringSplitOptions supportedOptions = StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries;
            if ((options & ~supportedOptions) != 0) throw new ArgumentOutOfRangeException();
            if (destination.Length == 0) return 0;

            var keepEmptyEntries = (options & StringSplitOptions.RemoveEmptyEntries) == 0;
            var trimEntries = (options & StringSplitOptions.TrimEntries) != 0;
            if (destination.Length == 1)
            {
                TrimSplitEntry(source, trimEntries, out var onlyStart, out var onlyLength);
                if (onlyLength != 0 || keepEmptyEntries)
                {
                    destination[0] = new Range(onlyStart, onlyStart + onlyLength);
                    return 1;
                }
                return 0;
            }

            var count = 0;
            var start = 0;
            while (true)
            {
                var separatorIndex = FindSeparator(source, start, separators, anySeparator, out var separatorLength);
                var end = separatorIndex < 0 ? source.Length : separatorIndex;
                TrimSplitEntry(source.Slice(start, end - start), trimEntries, out var itemStart, out var itemLength);
                itemStart += start;

                if (itemLength != 0 || keepEmptyEntries)
                {
                    if (count == destination.Length - 1 && separatorIndex >= 0)
                    {
                        TrimSplitEntry(source.Slice(start), trimEntries, out var remainderStart, out var remainderLength);
                        remainderStart += start;
                        if (remainderLength != 0 || keepEmptyEntries)
                        {
                            destination[count] = new Range(remainderStart, remainderStart + remainderLength);
                            return count + 1;
                        }
                        return count;
                    }

                    destination[count++] = new Range(itemStart, itemStart + itemLength);
                }

                if (separatorIndex < 0) return count;
                start = separatorIndex + separatorLength;
            }
        }

        private static int FindSeparator(ReadOnlySpan<char> source, int start, ReadOnlySpan<char> separators, bool anySeparator, out int separatorLength)
        {
            separatorLength = anySeparator ? 1 : separators.Length;
            if (separatorLength == 0) return -1;
            for (var index = start; index <= source.Length - separatorLength; index++)
            {
                var matches = true;
                for (var offset = 0; offset < separatorLength; offset++)
                {
                    if (anySeparator)
                    {
                        if (separators.Length == 0
                            ? !IsWhiteSpace(source[index + offset])
                            : separators.IndexOf(source[index + offset]) < 0)
                        {
                            matches = false;
                            break;
                        }
                    }
                    else if (source[index + offset] != separators[offset]) { matches = false; break; }
                }
                if (matches) return index;
            }
            return -1;
        }

        private static int SplitInto(
            ReadOnlySpan<char> source,
            Span<Range> destination,
            ReadOnlySpan<string> separators,
            StringSplitOptions options)
        {
            const StringSplitOptions supportedOptions =
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries;
            if ((options & ~supportedOptions) != 0) throw new ArgumentOutOfRangeException();
            if (destination.Length == 0) return 0;

            var keepEmptyEntries = (options & StringSplitOptions.RemoveEmptyEntries) == 0;
            var trimEntries = (options & StringSplitOptions.TrimEntries) != 0;
            if (destination.Length == 1)
            {
                TrimSplitEntry(source, trimEntries, out var onlyStart, out var onlyLength);
                if (onlyLength == 0 && !keepEmptyEntries) return 0;
                destination[0] = new Range(onlyStart, onlyStart + onlyLength);
                return 1;
            }

            var count = 0;
            var start = 0;
            while (true)
            {
                var separatorIndex = FindStringSeparator(
                    source, start, separators, out var separatorLength);
                var end = separatorIndex < 0 ? source.Length : separatorIndex;
                TrimSplitEntry(
                    source.Slice(start, end - start),
                    trimEntries,
                    out var itemStart,
                    out var itemLength);
                itemStart += start;

                if (itemLength != 0 || keepEmptyEntries)
                {
                    if (count == destination.Length - 1 && separatorIndex >= 0)
                    {
                        TrimSplitEntry(
                            source.Slice(start),
                            trimEntries,
                            out var remainderStart,
                            out var remainderLength);
                        remainderStart += start;
                        if (remainderLength == 0 && !keepEmptyEntries) return count;
                        destination[count] = new Range(
                            remainderStart, remainderStart + remainderLength);
                        return count + 1;
                    }

                    destination[count++] = new Range(itemStart, itemStart + itemLength);
                }

                if (separatorIndex < 0) return count;
                start = separatorIndex + separatorLength;
            }
        }

        private static int FindStringSeparator(
            ReadOnlySpan<char> source,
            int start,
            ReadOnlySpan<string> separators,
            out int separatorLength)
        {
            if (separators.Length == 0)
            {
                separatorLength = 1;
                for (var index = start; index < source.Length; index++)
                {
                    if (IsWhiteSpace(source[index])) return index;
                }
                return -1;
            }

            for (var index = start; index < source.Length; index++)
            {
                for (var separatorIndex = 0; separatorIndex < separators.Length; separatorIndex++)
                {
                    var separator = separators[separatorIndex];
                    if (String.IsNullOrEmpty(separator) || index + separator.Length > source.Length)
                    {
                        continue;
                    }
                    if (!source.Slice(index, separator.Length).SequenceEqual(separator.AsSpan()))
                    {
                        continue;
                    }

                    separatorLength = separator.Length;
                    return index;
                }
            }

            separatorLength = 0;
            return -1;
        }

        private static void TrimSplitEntry(ReadOnlySpan<char> source, bool trimEntries, out int start, out int length)
        {
            if (trimEntries)
            {
                TrimCharBounds(source, true, true, default, out start, out length);
                return;
            }
            start = 0;
            length = source.Length;
        }

        public static int Split(this ReadOnlySpan<char> source, Span<Range> destination, char separator, StringSplitOptions options = System.StringSplitOptions.None) => SplitInto(source, destination, new[] { separator }, false, options);
        public static int Split(this ReadOnlySpan<char> source, Span<Range> destination, ReadOnlySpan<char> separator, StringSplitOptions options = System.StringSplitOptions.None) => SplitInto(source, destination, separator, false, options);
        public static int SplitAny(this ReadOnlySpan<char> source, Span<Range> destination, ReadOnlySpan<char> separators, StringSplitOptions options = System.StringSplitOptions.None) => SplitInto(source, destination, separators, true, options);
        public static int SplitAny(this ReadOnlySpan<char> source, Span<Range> destination, ReadOnlySpan<string> separators, StringSplitOptions options = System.StringSplitOptions.None) => SplitInto(source, destination, separators, options);

        public static ReadOnlySpan<T> Trim<T>(this ReadOnlySpan<T> span, T value) where T : IEquatable<T> => Trim(span, new ReadOnlySpan<T>(new[] { value }));
        public static ReadOnlySpan<T> Trim<T>(this ReadOnlySpan<T> span, ReadOnlySpan<T> values) where T : IEquatable<T> { TrimBounds(span, values, out var start, out var length); return span.Slice(start, length); }
        public static ReadOnlySpan<T> Trim<T>(this ReadOnlySpan<T> span) where T : IEquatable<T> => TrimWhitespace(span);
        public static ReadOnlySpan<T> TrimStart<T>(this ReadOnlySpan<T> span, T value) where T : IEquatable<T> => TrimStart(span, new ReadOnlySpan<T>(new[] { value }));
        public static ReadOnlySpan<T> TrimStart<T>(this ReadOnlySpan<T> span, ReadOnlySpan<T> values) where T : IEquatable<T> { var start = 0; while (start < span.Length && span.Slice(start, 1).IndexOfAny(values) >= 0) start++; return span.Slice(start); }
        public static ReadOnlySpan<T> TrimStart<T>(this ReadOnlySpan<T> span) where T : IEquatable<T> => TrimWhitespaceStart(span);
        public static ReadOnlySpan<T> TrimEnd<T>(this ReadOnlySpan<T> span, T value) where T : IEquatable<T> => TrimEnd(span, new ReadOnlySpan<T>(new[] { value }));
        public static ReadOnlySpan<T> TrimEnd<T>(this ReadOnlySpan<T> span, ReadOnlySpan<T> values) where T : IEquatable<T> { var end = span.Length; while (end > 0 && span.Slice(end - 1, 1).IndexOfAny(values) >= 0) end--; return span.Slice(0, end); }
        public static ReadOnlySpan<T> TrimEnd<T>(this ReadOnlySpan<T> span) where T : IEquatable<T> => TrimWhitespaceEnd(span);
        public static Span<T> Trim<T>(this Span<T> span, T value) where T : IEquatable<T> => Trim(span, new ReadOnlySpan<T>(new[] { value }));
        public static Span<T> Trim<T>(this Span<T> span, ReadOnlySpan<T> values) where T : IEquatable<T> { TrimBounds((ReadOnlySpan<T>)span, values, out var start, out var length); return span.Slice(start, length); }
        public static Span<T> Trim<T>(this Span<T> span) where T : IEquatable<T> => throw new PlatformNotSupportedException();
        public static Span<T> TrimStart<T>(this Span<T> span, T value) where T : IEquatable<T> => TrimStart(span, new ReadOnlySpan<T>(new[] { value }));
        public static Span<T> TrimStart<T>(this Span<T> span, ReadOnlySpan<T> values) where T : IEquatable<T> { var start = 0; while (start < span.Length && span.Slice(start, 1).IndexOfAny(values) >= 0) start++; return span.Slice(start); }
        public static Span<T> TrimStart<T>(this Span<T> span) where T : IEquatable<T> => throw new PlatformNotSupportedException();
        public static Span<T> TrimEnd<T>(this Span<T> span, T value) where T : IEquatable<T> => TrimEnd(span, new ReadOnlySpan<T>(new[] { value }));
        public static Span<T> TrimEnd<T>(this Span<T> span, ReadOnlySpan<T> values) where T : IEquatable<T> { var end = span.Length; while (end > 0 && span.Slice(end - 1, 1).IndexOfAny(values) >= 0) end--; return span.Slice(0, end); }
        public static Span<T> TrimEnd<T>(this Span<T> span) where T : IEquatable<T> => throw new PlatformNotSupportedException();

        private static ReadOnlySpan<T> TrimWhitespace<T>(ReadOnlySpan<T> span) where T : IEquatable<T> => throw new PlatformNotSupportedException();
        private static ReadOnlySpan<T> TrimWhitespaceStart<T>(ReadOnlySpan<T> span) where T : IEquatable<T> => throw new PlatformNotSupportedException();
        private static ReadOnlySpan<T> TrimWhitespaceEnd<T>(ReadOnlySpan<T> span) where T : IEquatable<T> => throw new PlatformNotSupportedException();
        private static void TrimBounds<T>(ReadOnlySpan<T> span, ReadOnlySpan<T> values, out int start, out int length) where T : IEquatable<T>
        {
            start = 0;
            var end = span.Length;
            while (start < end && span.Slice(start, 1).IndexOfAny(values) >= 0) start++;
            while (end > start && span.Slice(end - 1, 1).IndexOfAny(values) >= 0) end--;
            length = end - start;
        }
        public static ReadOnlySpan<char> Trim(this ReadOnlySpan<char> span) => TrimChar(span, true, true, default);
        public static ReadOnlySpan<char> Trim(this ReadOnlySpan<char> span, char value) => TrimChar(span, true, true, new[] { value });
        public static ReadOnlySpan<char> Trim(this ReadOnlySpan<char> span, ReadOnlySpan<char> values) => TrimChar(span, true, true, values);
        public static ReadOnlySpan<char> TrimStart(this ReadOnlySpan<char> span) => TrimChar(span, true, false, default);
        public static ReadOnlySpan<char> TrimStart(this ReadOnlySpan<char> span, char value) => TrimChar(span, true, false, new[] { value });
        public static ReadOnlySpan<char> TrimStart(this ReadOnlySpan<char> span, ReadOnlySpan<char> values) => TrimChar(span, true, false, values);
        public static ReadOnlySpan<char> TrimEnd(this ReadOnlySpan<char> span) => TrimChar(span, false, true, default);
        public static ReadOnlySpan<char> TrimEnd(this ReadOnlySpan<char> span, char value) => TrimChar(span, false, true, new[] { value });
        public static ReadOnlySpan<char> TrimEnd(this ReadOnlySpan<char> span, ReadOnlySpan<char> values) => TrimChar(span, false, true, values);
        private static ReadOnlySpan<char> TrimChar(ReadOnlySpan<char> span, bool trimStart, bool trimEnd, ReadOnlySpan<char> values)
        {
            TrimCharBounds(span, trimStart, trimEnd, values, out var start, out var length);
            return span.Slice(start, length);
        }
        private static void TrimCharBounds(ReadOnlySpan<char> span, bool trimStart, bool trimEnd, ReadOnlySpan<char> values, out int start, out int length)
        {
            start = 0;
            var end = span.Length;
            while (trimStart && start < end && (values.Length == 0 ? IsWhiteSpace(span[start]) : values.IndexOf(span[start]) >= 0)) start++;
            while (trimEnd && end > start && (values.Length == 0 ? IsWhiteSpace(span[end - 1]) : values.IndexOf(span[end - 1]) >= 0)) end--;
            length = end - start;
        }
        public static Span<char> Trim(this Span<char> span) => TrimCharWritable(span, true, true, default);
        public static Span<char> Trim(this Span<char> span, char value) => TrimCharWritable(span, true, true, new[] { value });
        public static Span<char> Trim(this Span<char> span, ReadOnlySpan<char> values) => TrimCharWritable(span, true, true, values);
        public static Span<char> TrimStart(this Span<char> span) => TrimCharWritable(span, true, false, default);
        public static Span<char> TrimStart(this Span<char> span, char value) => TrimCharWritable(span, true, false, new[] { value });
        public static Span<char> TrimStart(this Span<char> span, ReadOnlySpan<char> values) => TrimCharWritable(span, true, false, values);
        public static Span<char> TrimEnd(this Span<char> span) => TrimCharWritable(span, false, true, default);
        public static Span<char> TrimEnd(this Span<char> span, char value) => TrimCharWritable(span, false, true, new[] { value });
        public static Span<char> TrimEnd(this Span<char> span, ReadOnlySpan<char> values) => TrimCharWritable(span, false, true, values);
        private static Span<char> TrimCharWritable(Span<char> span, bool trimStart, bool trimEnd, ReadOnlySpan<char> values)
        {
            TrimCharBounds((ReadOnlySpan<char>)span, trimStart, trimEnd, values, out var start, out var length);
            return span.Slice(start, length);
        }
        public static Memory<char> Trim(this Memory<char> memory) { TrimCharBounds(memory.Span, true, true, default, out var start, out var length); return memory.Slice(start, length); }
        public static Memory<char> TrimStart(this Memory<char> memory) { TrimCharBounds(memory.Span, true, false, default, out var start, out var length); return memory.Slice(start, length); }
        public static Memory<char> TrimEnd(this Memory<char> memory) { TrimCharBounds(memory.Span, false, true, default, out var start, out var length); return memory.Slice(start, length); }
        public static ReadOnlyMemory<char> Trim(this ReadOnlyMemory<char> memory) { TrimCharBounds(memory.Span, true, true, default, out var start, out var length); return memory.Slice(start, length); }
        public static ReadOnlyMemory<char> TrimStart(this ReadOnlyMemory<char> memory) { TrimCharBounds(memory.Span, true, false, default, out var start, out var length); return memory.Slice(start, length); }
        public static ReadOnlyMemory<char> TrimEnd(this ReadOnlyMemory<char> memory) { TrimCharBounds(memory.Span, false, true, default, out var start, out var length); return memory.Slice(start, length); }
        public static Memory<T> Trim<T>(this Memory<T> memory, T value) where T : IEquatable<T> => Trim(memory, new ReadOnlySpan<T>(new[] { value }));
        public static Memory<T> Trim<T>(this Memory<T> memory, ReadOnlySpan<T> values) where T : IEquatable<T> { TrimBounds(memory.Span, values, out var start, out var length); return memory.Slice(start, length); }
        public static Memory<T> TrimStart<T>(this Memory<T> memory, T value) where T : IEquatable<T> => TrimStart(memory, new ReadOnlySpan<T>(new[] { value }));
        public static Memory<T> TrimStart<T>(this Memory<T> memory, ReadOnlySpan<T> values) where T : IEquatable<T> { var start = 0; while (start < memory.Length && memory.Span.Slice(start, 1).IndexOfAny(values) >= 0) start++; return memory.Slice(start); }
        public static Memory<T> TrimEnd<T>(this Memory<T> memory, T value) where T : IEquatable<T> => TrimEnd(memory, new ReadOnlySpan<T>(new[] { value }));
        public static Memory<T> TrimEnd<T>(this Memory<T> memory, ReadOnlySpan<T> values) where T : IEquatable<T> { var end = memory.Length; while (end > 0 && memory.Span.Slice(end - 1, 1).IndexOfAny(values) >= 0) end--; return memory.Slice(0, end); }
        public static ReadOnlyMemory<T> Trim<T>(this ReadOnlyMemory<T> memory, T value) where T : IEquatable<T> => Trim(memory, new ReadOnlySpan<T>(new[] { value }));
        public static ReadOnlyMemory<T> Trim<T>(this ReadOnlyMemory<T> memory, ReadOnlySpan<T> values) where T : IEquatable<T> { TrimBounds(memory.Span, values, out var start, out var length); return memory.Slice(start, length); }
        public static ReadOnlyMemory<T> TrimStart<T>(this ReadOnlyMemory<T> memory, T value) where T : IEquatable<T> => TrimStart(memory, new ReadOnlySpan<T>(new[] { value }));
        public static ReadOnlyMemory<T> TrimStart<T>(this ReadOnlyMemory<T> memory, ReadOnlySpan<T> values) where T : IEquatable<T> { var start = 0; while (start < memory.Length && memory.Span.Slice(start, 1).IndexOfAny(values) >= 0) start++; return memory.Slice(start); }
        public static ReadOnlyMemory<T> TrimEnd<T>(this ReadOnlyMemory<T> memory, T value) where T : IEquatable<T> => TrimEnd(memory, new ReadOnlySpan<T>(new[] { value }));
        public static ReadOnlyMemory<T> TrimEnd<T>(this ReadOnlyMemory<T> memory, ReadOnlySpan<T> values) where T : IEquatable<T> { var end = memory.Length; while (end > 0 && memory.Span.Slice(end - 1, 1).IndexOfAny(values) >= 0) end--; return memory.Slice(0, end); }

        public static bool IsWhiteSpace(char value) => value is
            '\u0009' or '\u000a' or '\u000b' or '\u000c' or '\u000d' or
            '\u0020' or '\u0085' or '\u00a0' or '\u1680' or
            >= '\u2000' and <= '\u200a' or
            '\u2028' or '\u2029' or '\u202f' or '\u205f' or '\u3000';
        public static bool Equals(this ReadOnlySpan<char> left, ReadOnlySpan<char> right, StringComparison comparisonType)
        {
            ValidateOrdinalComparison(comparisonType);
            return left.Length == right.Length && OrdinalEqualsAt(left, right, 0, comparisonType);
        }

        public static int CompareTo(this ReadOnlySpan<char> left, ReadOnlySpan<char> right, StringComparison comparisonType)
        {
            ValidateOrdinalComparison(comparisonType);
            var length = left.Length < right.Length ? left.Length : right.Length;
            for (var index = 0; index < length; index++)
            {
                var leftValue = FoldOrdinal(left[index], comparisonType);
                var rightValue = FoldOrdinal(right[index], comparisonType);
                if (leftValue != rightValue) return leftValue - rightValue;
            }
            return left.Length - right.Length;
        }

        public static int IndexOf(this ReadOnlySpan<char> left, ReadOnlySpan<char> right, StringComparison comparisonType)
        {
            ValidateOrdinalComparison(comparisonType);
            for (var index = 0; index <= left.Length - right.Length; index++)
            {
                if (OrdinalEqualsAt(left, right, index, comparisonType)) return index;
            }
            return -1;
        }

        public static int LastIndexOf(this ReadOnlySpan<char> left, ReadOnlySpan<char> right, StringComparison comparisonType)
        {
            ValidateOrdinalComparison(comparisonType);
            for (var index = left.Length - right.Length; index >= 0; index--)
            {
                if (OrdinalEqualsAt(left, right, index, comparisonType)) return index;
            }
            return -1;
        }

        public static bool TryWrite(
            this Span<char> destination,
            [System.Runtime.CompilerServices.InterpolatedStringHandlerArgument(nameof(destination))]
            ref TryWriteInterpolatedStringHandler handler,
            out int charsWritten) => CompleteTryWrite(ref handler, out charsWritten);

        public static bool TryWrite(
            this Span<char> destination,
            IFormatProvider? provider,
            [System.Runtime.CompilerServices.InterpolatedStringHandlerArgument(nameof(destination), nameof(provider))]
            ref TryWriteInterpolatedStringHandler handler,
            out int charsWritten) => CompleteTryWrite(ref handler, out charsWritten);

        public static bool TryWrite(
            this Span<char> destination,
            IFormatProvider? provider,
            CompositeFormat format,
            out int charsWritten,
            params object?[] args)
        {
            if (format is null) throw new ArgumentNullException(nameof(format));
            if (args is null) throw new ArgumentNullException(nameof(args));
            return TryWriteFormatted(destination, provider, format, args, out charsWritten);
        }

        public static bool TryWrite(
            this Span<char> destination,
            IFormatProvider? provider,
            CompositeFormat format,
            out int charsWritten,
            params ReadOnlySpan<object?> args)
        {
            if (format is null) throw new ArgumentNullException(nameof(format));
            var values = args.ToArray();
            return TryWriteFormatted(destination, provider, format, values, out charsWritten);
        }

        public static bool TryWrite<TArg0>(this Span<char> destination, IFormatProvider? provider, CompositeFormat format, out int charsWritten, TArg0 arg0) =>
            TryWriteFormattedChecked(destination, provider, format, new object?[] { arg0 }, out charsWritten);

        public static bool TryWrite<TArg0, TArg1>(this Span<char> destination, IFormatProvider? provider, CompositeFormat format, out int charsWritten, TArg0 arg0, TArg1 arg1) =>
            TryWriteFormattedChecked(destination, provider, format, new object?[] { arg0, arg1 }, out charsWritten);

        public static bool TryWrite<TArg0, TArg1, TArg2>(this Span<char> destination, IFormatProvider? provider, CompositeFormat format, out int charsWritten, TArg0 arg0, TArg1 arg1, TArg2 arg2) =>
            TryWriteFormattedChecked(destination, provider, format, new object?[] { arg0, arg1, arg2 }, out charsWritten);

        private static bool TryWriteFormattedChecked(Span<char> destination, IFormatProvider? provider, CompositeFormat format, object?[] args, out int charsWritten)
        {
            if (format is null) throw new ArgumentNullException(nameof(format));
            return TryWriteFormatted(destination, provider, format, args, out charsWritten);
        }

        private static bool TryWriteFormatted(Span<char> destination, IFormatProvider? provider, CompositeFormat format, object?[] args, out int charsWritten)
        {
            var builder = new Text.StringBuilder();
            builder.AppendFormat(provider, format, args);
            if (builder.Length > destination.Length)
            {
                charsWritten = 0;
                return false;
            }
            for (var index = 0; index < builder.Length; index++) destination[index] = builder[index];
            charsWritten = builder.Length;
            return true;
        }

        [System.Runtime.CompilerServices.InterpolatedStringHandler]
        public ref struct TryWriteInterpolatedStringHandler
        {
            private Span<char> _destination;
            private IFormatProvider? _provider;
            private int _index;
            private bool _success;

            public TryWriteInterpolatedStringHandler(int literalLength, int formattedCount, Span<char> destination, out bool shouldAppend)
                : this(literalLength, formattedCount, destination, null, out shouldAppend)
            {
            }

            public TryWriteInterpolatedStringHandler(int literalLength, int formattedCount, Span<char> destination, IFormatProvider? provider, out bool shouldAppend)
            {
                _destination = destination;
                _provider = provider;
                _index = 0;
                _success = shouldAppend = destination.Length >= literalLength;
            }

            internal int Written => _index;
            internal bool Success => _success;

            public bool AppendLiteral(string value) => AppendText(value);
            public bool AppendFormatted<T>(T value) => AppendFormatted(value, 0, null);
            public bool AppendFormatted<T>(T value, string? format) => AppendFormatted(value, 0, format);
            public bool AppendFormatted<T>(T value, int alignment) => AppendFormatted(value, alignment, null);
            public bool AppendFormatted<T>(T value, int alignment, string? format) => AppendAligned(Text.StringBuilder.FormatValue(value, format, _provider), alignment);
            public bool AppendFormatted(string? value) => AppendText(value);
            public bool AppendFormatted(string? value, int alignment = 0, string? format = null) => AppendAligned(Text.StringBuilder.FormatValue(value, format, _provider), alignment);
            public bool AppendFormatted(object? value, int alignment = 0, string? format = null) => AppendAligned(Text.StringBuilder.FormatValue(value, format, _provider), alignment);
            public bool AppendFormatted(scoped ReadOnlySpan<char> value) => AppendText(value);
            public bool AppendFormatted(scoped ReadOnlySpan<char> value, int alignment = 0, string? format = null) => AppendAligned(value, alignment);

            private bool AppendAligned(string value, int alignment)
            {
                var padding = Math.Max(0, (alignment < 0 ? -alignment : alignment) - value.Length);
                if (alignment > 0 && !AppendText(' ', padding)) return false;
                if (!AppendText(value)) return false;
                return alignment >= 0 || AppendText(' ', padding);
            }

            private bool AppendAligned(scoped ReadOnlySpan<char> value, int alignment)
            {
                var padding = Math.Max(0, (alignment < 0 ? -alignment : alignment) - value.Length);
                if (alignment > 0 && !AppendText(' ', padding)) return false;
                if (!AppendText(value)) return false;
                return alignment >= 0 || AppendText(' ', padding);
            }

            private bool AppendText(string? value) => value is null || AppendText(value.AsSpan());

            private bool AppendText(char value, int count)
            {
                if (!_success || count == 0) return _success;
                if (count > _destination.Length - _index) { _success = false; return false; }
                for (var offset = 0; offset < count; offset++) _destination[_index++] = value;
                return true;
            }

            private bool AppendText(scoped ReadOnlySpan<char> value)
            {
                if (!_success) return false;
                if (value.Length > _destination.Length - _index) { _success = false; return false; }
                for (var offset = 0; offset < value.Length; offset++) _destination[_index++] = value[offset];
                return true;
            }
        }

        private static bool CompleteTryWrite(ref TryWriteInterpolatedStringHandler handler, out int charsWritten)
        {
            charsWritten = handler.Success ? handler.Written : 0;
            return handler.Success;
        }

        private static void ValidateOrdinalComparison(StringComparison comparisonType)
        {
            if (comparisonType is not StringComparison.Ordinal and not StringComparison.OrdinalIgnoreCase)
                throw new PlatformNotSupportedException();
        }

        private static bool OrdinalEqualsAt(
            ReadOnlySpan<char> source,
            ReadOnlySpan<char> value,
            int start,
            StringComparison comparisonType)
        {
            for (var index = 0; index < value.Length; index++)
            {
                if (FoldOrdinal(source[start + index], comparisonType) !=
                    FoldOrdinal(value[index], comparisonType))
                    return false;
            }
            return true;
        }

        private static char FoldOrdinal(char value, StringComparison comparisonType) =>
            comparisonType == StringComparison.OrdinalIgnoreCase
                ? char.ToUpperInvariant(value)
                : value;
    }
}
