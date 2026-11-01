// Portions adapted from dotnet/runtime System.Private.CoreLib MemoryMarshal.cs.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace System.Runtime.InteropServices
{
    public static class MemoryMarshal
    {
        // TryGetString, AsMemory, CreateFromPinnedArray, TryGetArray and TryGetMemoryManager are
        // upstream's, from the same file at 474ed963699fc29809015392738a15231aaae2de.

        public static bool TryGetString(ReadOnlyMemory<char> memory, [NotNullWhen(true)] out string? text, out int start, out int length)
        {
            if (memory.GetObjectStartLength(out int offset, out int count) is string s)
            {
                Debug.Assert(offset >= 0);
                Debug.Assert(count >= 0);
                text = s;
                start = offset;
                length = count;
                return true;
            }
            else
            {
                text = null;
                start = 0;
                length = 0;
                return false;
            }
        }

        public static Memory<T> AsMemory<T>(ReadOnlyMemory<T> memory) =>
            new Memory<T>(memory._object, memory._index, memory._length);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Memory<T> CreateFromPinnedArray<T>(T[]? array, int start, int length)
        {
            if (array == null)
            {
                if (start != 0 || length != 0)
                    ThrowHelper.ThrowArgumentOutOfRangeException();
                return default;
            }
            if (!typeof(T).IsValueType && array.GetType() != typeof(T[]))
                ThrowHelper.ThrowArrayTypeMismatchException();
            if ((uint)start > (uint)array.Length || (uint)length > (uint)(array.Length - start))
                ThrowHelper.ThrowArgumentOutOfRangeException();

            // Before using _index, check if _index < 0, then 'and' it with RemoveFlagsBitMask
            return new Memory<T>((object)array, start | (1 << 31), length);
        }

        public static bool TryGetArray<T>(ReadOnlyMemory<T> memory, out ArraySegment<T> segment)
        {
            object? obj = memory.GetObjectStartLength(out int index, out int length);

            // As an optimization, we skip the "is string?" check below if typeof(T) is not char,
            // as Memory<T> / ROM<T> can't possibly contain a string instance in this case.

            if (obj != null && !(
                (typeof(T) == typeof(char) && obj.GetType() == typeof(string))
                ))
            {
                if (RuntimeHelpers.ObjectHasComponentSize(obj))
                {
                    // The object has a component size, which means it's variable-length, but we already
                    // checked above that it's not a string. The only remaining option is that it's a T[]
                    // or a U[] which is blittable to a T[] (e.g., int[] and uint[]).

                    // The array may be prepinned, so remove the high bit from the start index in the line below.
                    // The ArraySegment<T> ctor will perform bounds checking on index & length.

                    segment = new ArraySegment<T>(Unsafe.As<T[]>(obj), index & ReadOnlyMemory<T>.RemoveFlagsBitMask, length);
                    return true;
                }
                else
                {
                    // The object isn't null, and it's not variable-length, so the only remaining option
                    // is MemoryManager<T>. The ArraySegment<T> ctor will perform bounds checking on index & length.

                    Debug.Assert(obj is MemoryManager<T>);
                    if (Unsafe.As<MemoryManager<T>>(obj).TryGetArray(out ArraySegment<T> tempArraySegment))
                    {
                        segment = new ArraySegment<T>(tempArraySegment.Array!, tempArraySegment.Offset + index, length);
                        return true;
                    }
                }
            }

            // If we got to this point, the object is null, or it's a string, or it's a MemoryManager<T>
            // which isn't backed by an array. We'll quickly homogenize all zero-length Memory<T> instances
            // to an empty array for the purposes of reporting back to our caller.

            if (length == 0)
            {
                segment = ArraySegment<T>.Empty;
                return true;
            }

            // Otherwise, there's *some* data, but it's not convertible to T[].

            segment = default;
            return false;
        }

        public static bool TryGetMemoryManager<T, TManager>(ReadOnlyMemory<T> memory, [NotNullWhen(true)] out TManager? manager)
            where TManager : MemoryManager<T>
        {
            TManager? localManager; // Use register for null comparison rather than byref
            manager = localManager = memory.GetObjectStartLength(out _, out _) as TManager;
#pragma warning disable 8762 // "Parameter 'manager' may not have a null value when exiting with 'true'."
            return localManager != null;
#pragma warning restore 8762
        }

        public static bool TryGetMemoryManager<T, TManager>(ReadOnlyMemory<T> memory, [NotNullWhen(true)] out TManager? manager, out int start, out int length)
           where TManager : MemoryManager<T>
        {
            TManager? localManager; // Use register for null comparison rather than byref
            manager = localManager = memory.GetObjectStartLength(out start, out length) as TManager;

            Debug.Assert(length >= 0);

            if (localManager == null)
            {
                start = default;
                length = default;
                return false;
            }
#pragma warning disable 8762 // "Parameter 'manager' may not have a null value when exiting with 'true'."
            return true;
#pragma warning restore 8762
        }

        // The current ReadOnlyMemory representation is either an array slice or
        // a MemoryManager slice.  Keep the enumerable as a view over that
        // representation instead of materializing a copy; this preserves the
        // observable behavior of the upstream API for mutable backing stores
        // and for managers whose span changes between iterations.
        public static IEnumerable<T> ToEnumerable<T>(System.ReadOnlyMemory<T> memory) =>
            memory.IsEmpty
                ? System.Array.Empty<T>()
                : new ReadOnlyMemoryEnumerable<T>(memory);

        public static ref T GetReference<T>(System.Span<T> span) =>
            ref span._reference;

        public static ref T GetReference<T>(System.ReadOnlySpan<T> span) =>
            ref span._reference;

        public static ref T GetArrayDataReference<T>(T[] array)
        {
            if (array.Length == 0)
            {
                return ref System.Runtime.CompilerServices.Unsafe.NullRef<T>();
            }

            return ref array[0];
        }

        public static unsafe ref byte GetArrayDataReference(System.Array array) =>
            throw new System.PlatformNotSupportedException();

        public static System.Span<T> CreateSpan<T>(scoped ref T reference, int length) =>
            length < 0
                ? throw new ArgumentOutOfRangeException()
                : new(ref Unsafe.AsRef(in reference), length);

        public static System.ReadOnlySpan<T> CreateReadOnlySpan<T>(
            scoped ref readonly T reference,
            int length) =>
            length < 0
                ? throw new ArgumentOutOfRangeException()
                : new(ref Unsafe.AsRef(in reference), length);

        // Exact-layout projections adapted from dotnet/runtime
        // System.Private.CoreLib MemoryMarshal (MIT). The compiler lowers the
        // Unsafe operations below; no managed pointer arithmetic is emulated.
        public static unsafe System.ReadOnlySpan<byte> AsBytes<T>(
            System.ReadOnlySpan<T> span)
            where T : struct
        {
            ThrowIfContainsReferences<T>();
            ref T source = ref Unsafe.AsRef(in GetReference(span));
            ref byte bytes = ref Unsafe.As<T, byte>(ref source);
            return new System.ReadOnlySpan<byte>(
                ref bytes,
                GetByteLength<T>(span.Length));
        }

        public static unsafe System.Span<byte> AsBytes<T>(System.Span<T> span)
            where T : struct
        {
            ThrowIfContainsReferences<T>();
            ref T source = ref GetReference(span);
            ref byte bytes = ref Unsafe.As<T, byte>(ref source);
            return new System.Span<byte>(ref bytes, GetByteLength<T>(span.Length));
        }

        public static unsafe ref readonly T AsRef<T>(
            System.ReadOnlySpan<byte> span)
            where T : struct
        {
            ThrowIfContainsReferences<T>();
            EnsureCanRead<T>(span.Length);
            ref byte source = ref Unsafe.AsRef(in GetReference(span));
            return ref Unsafe.As<byte, T>(ref source);
        }

        public static unsafe ref T AsRef<T>(System.Span<byte> span)
            where T : struct
        {
            ThrowIfContainsReferences<T>();
            EnsureCanRead<T>(span.Length);
            ref byte source = ref GetReference(span);
            return ref Unsafe.As<byte, T>(ref source);
        }

        public static unsafe System.ReadOnlySpan<TTo> Cast<TFrom, TTo>(
            System.ReadOnlySpan<TFrom> span)
            where TFrom : struct
            where TTo : struct
        {
            ThrowIfContainsReferences<TFrom>();
            ThrowIfContainsReferences<TTo>();
            var length = GetCastLength<TFrom, TTo>(span.Length);
            ref TFrom source = ref Unsafe.AsRef(in GetReference(span));
            ref TTo target = ref Unsafe.As<TFrom, TTo>(ref source);
            return new System.ReadOnlySpan<TTo>(ref target, length);
        }

        public static unsafe System.Span<TTo> Cast<TFrom, TTo>(
            System.Span<TFrom> span)
            where TFrom : struct
            where TTo : struct
        {
            ThrowIfContainsReferences<TFrom>();
            ThrowIfContainsReferences<TTo>();
            var length = GetCastLength<TFrom, TTo>(span.Length);
            ref TFrom source = ref GetReference(span);
            ref TTo target = ref Unsafe.As<TFrom, TTo>(ref source);
            return new System.Span<TTo>(ref target, length);
        }

        public static unsafe T Read<T>(System.ReadOnlySpan<byte> source)
            where T : struct
        {
            ThrowIfContainsReferences<T>();
            EnsureCanRead<T>(source.Length);
            return AsRef<T>(source);
        }

        public static unsafe bool TryRead<T>(
            System.ReadOnlySpan<byte> source,
            out T value)
            where T : struct
        {
            ThrowIfContainsReferences<T>();
            if (source.Length < Unsafe.SizeOf<T>())
            {
                value = default(T)!;
                return false;
            }

            value = AsRef<T>(source);
            return true;
        }

        public static unsafe void Write<T>(
            System.Span<byte> destination,
            in T value)
            where T : struct
        {
            ThrowIfContainsReferences<T>();
            EnsureCanWrite<T>(destination.Length);
            ref T target = ref AsRef<T>(destination);
            target = value;
        }

        public static unsafe bool TryWrite<T>(
            System.Span<byte> destination,
            in T value)
            where T : struct
        {
            ThrowIfContainsReferences<T>();
            if (destination.Length < Unsafe.SizeOf<T>())
            {
                return false;
            }

            Write(destination, in value);
            return true;
        }

        public static unsafe System.ReadOnlySpan<byte>
            CreateReadOnlySpanFromNullTerminated(byte* value)
        {
            if (value == null)
            {
                return default;
            }

            var length = 0;
            while (value[length] != 0)
            {
                length++;
            }

            return new System.ReadOnlySpan<byte>(value, length);
        }

        public static unsafe System.ReadOnlySpan<char>
            CreateReadOnlySpanFromNullTerminated(char* value)
        {
            if (value == null)
            {
                return default;
            }

            var length = 0;
            while (value[length] != '\0')
            {
                length++;
            }

            return new System.ReadOnlySpan<char>(value, length);
        }

        private static void ThrowIfContainsReferences<T>()
            where T : struct
        {
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            {
                throw new ArgumentException();
            }
        }

        private static int GetByteLength<T>(int elementCount)
            where T : struct =>
            checked(elementCount * Unsafe.SizeOf<T>());

        private static int GetCastLength<TFrom, TTo>(int elementCount)
            where TFrom : struct
            where TTo : struct
        {
            var byteLength = GetByteLength<TFrom>(elementCount);
            var targetSize = Unsafe.SizeOf<TTo>();
            if (targetSize == 0 || byteLength % targetSize != 0)
            {
                throw new ArgumentException();
            }

            return byteLength / targetSize;
        }

        private static void EnsureCanRead<T>(int byteLength)
            where T : struct
        {
            if (byteLength < Unsafe.SizeOf<T>())
            {
                throw new ArgumentOutOfRangeException();
            }
        }

        private static void EnsureCanWrite<T>(int byteLength)
            where T : struct => EnsureCanRead<T>(byteLength);

        private sealed class ReadOnlyMemoryEnumerable<T> : IEnumerable<T>
        {
            private readonly System.ReadOnlyMemory<T> _memory;

            public ReadOnlyMemoryEnumerable(System.ReadOnlyMemory<T> memory) =>
                _memory = memory;

            public IEnumerator<T> GetEnumerator() => new Enumerator(_memory);

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            private sealed class Enumerator : IEnumerator<T>
            {
                private readonly System.ReadOnlyMemory<T> _memory;
                private int _index = -1;

                public Enumerator(System.ReadOnlyMemory<T> memory) =>
                    _memory = memory;

                public bool MoveNext()
                {
                    if (_index < _memory.Length - 1)
                    {
                        _index++;
                        return true;
                    }

                    _index = _memory.Length;
                    return false;
                }

                public T Current
                {
                    get
                    {
                        if ((uint)_index >= (uint)_memory.Length)
                        {
                            throw new InvalidOperationException();
                        }

                        return _memory.Span[_index];
                    }
                }

                object IEnumerator.Current => Current!;

                public void Reset() => _index = -1;

                public void Dispose() { }
            }
        }
    }
}
