// Portions adapted from dotnet/runtime System.Private.CoreLib Unsafe.cs.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace System.Runtime.CompilerServices
{
    public static unsafe class Unsafe
    {
        public static ref T Add<T>(ref T source, int elementOffset)
            where T : allows ref struct =>
            throw new System.PlatformNotSupportedException();

        public static ref T Add<T>(ref T source, nint elementOffset)
            where T : allows ref struct =>
            ref AddByteOffset(ref source, elementOffset * SizeOf<T>());

        public static ref T Add<T>(ref T source, nuint elementOffset)
            where T : allows ref struct =>
            ref AddByteOffset(ref source, elementOffset * (nuint)SizeOf<T>());

        public static void* Add<T>(void* source, int elementOffset)
            where T : allows ref struct =>
            (byte*)source + elementOffset * (nint)SizeOf<T>();

        public static ref T AddByteOffset<T>(ref T source, nint byteOffset)
            where T : allows ref struct =>
            throw new System.PlatformNotSupportedException();

        public static ref T AddByteOffset<T>(ref T source, nuint byteOffset)
            where T : allows ref struct =>
            ref AddByteOffset(ref source, (nint)byteOffset);

        public static void* AsPointer<T>(ref readonly T value)
            where T : allows ref struct =>
            (void*)ByteOffset(ref NullRef<T>(), ref AsRef(in value));

        [return: NotNullIfNotNull(nameof(value))]
        public static T? As<T>(object? value) where T : class? =>
            throw new System.PlatformNotSupportedException();

        public static ref TTo As<TFrom, TTo>(ref TFrom source)
            where TFrom : allows ref struct
            where TTo : allows ref struct =>
            throw new System.PlatformNotSupportedException();

        public static ref T AsRef<T>(void* source)
            where T : allows ref struct =>
            throw new System.PlatformNotSupportedException();

        public static ref T AsRef<T>(scoped ref readonly T source)
            where T : allows ref struct =>
            throw new System.PlatformNotSupportedException();

        public static ref T NullRef<T>()
            where T : allows ref struct =>
            throw new System.PlatformNotSupportedException();

        public static int SizeOf<T>()
            where T : allows ref struct =>
            throw new System.PlatformNotSupportedException();

        public static nint ByteOffset<T>(ref readonly T origin, ref readonly T target)
            where T : allows ref struct =>
            throw new System.PlatformNotSupportedException();

        public static bool IsAddressGreaterThan<T>(ref readonly T left, ref readonly T right)
            where T : allows ref struct =>
            throw new System.PlatformNotSupportedException();

        public static bool IsAddressGreaterThanOrEqualTo<T>(ref readonly T left, ref readonly T right)
            where T : allows ref struct =>
            !IsAddressGreaterThan(in right, in left);

        public static bool IsAddressLessThan<T>(ref readonly T left, ref readonly T right)
            where T : allows ref struct =>
            IsAddressGreaterThan(in right, in left);

        public static bool IsAddressLessThanOrEqualTo<T>(ref readonly T left, ref readonly T right)
            where T : allows ref struct =>
            !IsAddressGreaterThan(in left, in right);

        public static bool AreSame<T>(ref readonly T left, ref readonly T right)
            where T : allows ref struct =>
            !IsAddressGreaterThan(in left, in right) &&
            !IsAddressGreaterThan(in right, in left);

        public static bool IsNullRef<T>(ref readonly T source)
            where T : allows ref struct
        {
            ref T nullReference = ref NullRef<T>();
            return !IsAddressGreaterThan(in source, in nullReference);
        }

        public static TTo BitCast<TFrom, TTo>(TFrom source)
            where TFrom : allows ref struct
            where TTo : allows ref struct
        {
            if (SizeOf<TFrom>() != SizeOf<TTo>())
            {
                throw new System.NotSupportedException();
            }

            return ReadUnaligned<TTo>(ref As<TFrom, byte>(ref source));
        }

        public static void Copy<T>(void* destination, ref readonly T source)
            where T : allows ref struct =>
            AsRef<T>(destination) = source;

        public static void Copy<T>(ref T destination, void* source)
            where T : allows ref struct =>
            destination = AsRef<T>(source);

        public static void CopyBlock(void* destination, void* source, uint byteCount) =>
            NativeMemory.Copy(source, destination, byteCount);

        public static void CopyBlock(
            ref byte destination,
            ref readonly byte source,
            uint byteCount) =>
            CopyBlock(AsPointer(ref destination), AsPointer(in source), byteCount);

        public static void CopyBlockUnaligned(void* destination, void* source, uint byteCount) =>
            CopyBlock(destination, source, byteCount);

        public static void CopyBlockUnaligned(
            ref byte destination,
            ref readonly byte source,
            uint byteCount) =>
            CopyBlock(ref destination, in source, byteCount);

        public static void InitBlock(void* startAddress, byte value, uint byteCount) =>
            NativeMemory.Fill(startAddress, byteCount, value);

        public static void InitBlock(ref byte startAddress, byte value, uint byteCount) =>
            InitBlock(AsPointer(ref startAddress), value, byteCount);

        public static void InitBlockUnaligned(void* startAddress, byte value, uint byteCount) =>
            InitBlock(startAddress, value, byteCount);

        public static void InitBlockUnaligned(
            ref byte startAddress,
            byte value,
            uint byteCount) =>
            InitBlock(ref startAddress, value, byteCount);

        public static T Read<T>(void* source)
            where T : allows ref struct =>
            AsRef<T>(source);

        public static T ReadUnaligned<T>(void* source)
            where T : allows ref struct =>
            Read<T>(source);

        public static T ReadUnaligned<T>(scoped ref readonly byte source)
            where T : allows ref struct =>
            As<byte, T>(ref AsRef(in source));

        public static void Write<T>(void* destination, T value)
            where T : allows ref struct =>
            AsRef<T>(destination) = value;

        public static void WriteUnaligned<T>(void* destination, T value)
            where T : allows ref struct =>
            Write(destination, value);

        public static void WriteUnaligned<T>(ref byte destination, T value)
            where T : allows ref struct =>
            As<byte, T>(ref destination) = value;

        public static void SkipInit<T>(out T value)
            where T : allows ref struct =>
            value = default!;

        public static ref T Subtract<T>(ref T source, int elementOffset)
            where T : allows ref struct =>
            ref SubtractByteOffset(ref source, elementOffset * (nint)SizeOf<T>());

        public static ref T Subtract<T>(ref T source, nint elementOffset)
            where T : allows ref struct =>
            ref SubtractByteOffset(ref source, elementOffset * SizeOf<T>());

        public static ref T Subtract<T>(ref T source, nuint elementOffset)
            where T : allows ref struct =>
            ref SubtractByteOffset(ref source, elementOffset * (nuint)SizeOf<T>());

        public static void* Subtract<T>(void* source, int elementOffset)
            where T : allows ref struct =>
            (byte*)source - elementOffset * (nint)SizeOf<T>();

        public static ref T SubtractByteOffset<T>(ref T source, nint byteOffset)
            where T : allows ref struct =>
            ref AddByteOffset(ref source, unchecked(-byteOffset));

        public static ref T SubtractByteOffset<T>(ref T source, nuint byteOffset)
            where T : allows ref struct =>
            ref AddByteOffset(ref source, unchecked(0 - byteOffset));

        public static ref T Unbox<T>(object box) where T : struct =>
            throw new System.PlatformNotSupportedException();

        // Determines if the address is aligned at least to `alignment` bytes.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsOpportunisticallyAligned<T>(ref readonly T address, nuint alignment)
        {
            // `alignment` is expected to be a power of 2 in bytes.
            // We use Unsafe.AsPointer to convert to a pointer,
            // GC will keep alignment when moving objects (up to sizeof(void*)),
            // otherwise alignment should be considered a hint if not pinned.
            System.Diagnostics.Debug.Assert(nuint.IsPow2(alignment));
            return ((nuint)AsPointer(in address) & (alignment - 1)) == 0;
        }

        // Determines the misalignment of the address with respect to the specified `alignment`.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static nuint OpportunisticMisalignment<T>(ref readonly T address, nuint alignment)
        {
            // `alignment` is expected to be a power of 2 in bytes.
            // We use Unsafe.AsPointer to convert to a pointer,
            // GC will keep alignment when moving objects (up to sizeof(void*)),
            // otherwise alignment should be considered a hint if not pinned.
            System.Diagnostics.Debug.Assert(nuint.IsPow2(alignment));
            return (nuint)AsPointer(in address) & (alignment - 1);
        }
    }
}
