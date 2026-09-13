// Portions adapted from dotnet/runtime System.Private.CoreLib MemoryMarshal.cs.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace System.Runtime.InteropServices
{
    public static class MemoryMarshal
    {
        public static bool TryGetString(
            ReadOnlyMemory<char> memory,
            out string? text,
            out int start,
            out int length) => memory.TryGetString(out text, out start, out length);

        public static System.Memory<T> AsMemory<T>(System.ReadOnlyMemory<T> memory) =>
            memory.DangerousAsMemory();

        public static bool TryGetArray<T>(
            System.ReadOnlyMemory<T> memory,
            out System.ArraySegment<T> segment) =>
            memory.TryGetArray(out segment);

        public static bool TryGetMemoryManager<T, TManager>(
            System.ReadOnlyMemory<T> memory,
            out TManager? manager)
            where TManager : System.Buffers.MemoryManager<T>
        {
            return memory.TryGetMemoryManager(out manager, out _, out _);
        }

        public static bool TryGetMemoryManager<T, TManager>(
            System.ReadOnlyMemory<T> memory,
            out TManager? manager,
            out int start,
            out int length)
            where TManager : System.Buffers.MemoryManager<T> =>
            memory.TryGetMemoryManager(out manager, out start, out length);

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
            ref span.DangerousReference;

        public static ref T GetReference<T>(System.ReadOnlySpan<T> span)
        {
            if (span.IsEmpty)
            {
                return ref System.Runtime.CompilerServices.Unsafe.NullRef<T>();
            }

            return ref System.Runtime.CompilerServices.Unsafe.AsRef(in span[0]);
        }

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
                throw new ArgumentException();
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
