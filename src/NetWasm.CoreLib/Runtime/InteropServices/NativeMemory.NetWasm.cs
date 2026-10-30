// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// NetWasm's counterpart of upstream's NativeMemory.Unix.cs at dotnet/runtime
// 474ed963699fc29809015392738a15231aaae2de: the allocator members, which the NetWasm runtime implements.

using System.Runtime.CompilerServices;

namespace System.Runtime.InteropServices
{
    /// <summary>Provides access to unmanaged memory allocation primitives.</summary>
    public static unsafe partial class NativeMemory
    {
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void* Alloc(nuint byteCount);

        /// <summary>Allocates and clears a block of memory of the specified size, in elements.</summary>
        public static void* AllocZeroed(nuint elementCount, nuint elementSize)
        {
            var byteCount = GetByteCount(elementCount, elementSize);
            var result = Alloc(byteCount);
            Clear(result, byteCount);
            return result;
        }

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void Free(void* ptr);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void* Realloc(void* ptr, nuint byteCount);

        public static void* AlignedAlloc(nuint byteCount, nuint alignment)
        {
            ValidateAlignment(alignment);
            var adjustedAlignment = alignment < (nuint)sizeof(void*)
                ? (nuint)sizeof(void*)
                : alignment;
            var adjustedByteCount = byteCount != 0
                ? (byteCount + adjustedAlignment - 1) & ~(adjustedAlignment - 1)
                : adjustedAlignment;
            return AlignedAllocCore(
                adjustedByteCount < byteCount ? nuint.MaxValue : adjustedByteCount,
                adjustedAlignment);
        }

        public static void* AlignedRealloc(void* ptr, nuint byteCount, nuint alignment)
        {
            ValidateAlignment(alignment);
            var adjustedAlignment = alignment < (nuint)sizeof(void*)
                ? (nuint)sizeof(void*)
                : alignment;
            var adjustedByteCount = byteCount != 0
                ? (byteCount + adjustedAlignment - 1) & ~(adjustedAlignment - 1)
                : adjustedAlignment;
            return AlignedReallocCore(
                ptr,
                adjustedByteCount < byteCount ? nuint.MaxValue : adjustedByteCount,
                adjustedAlignment);
        }

        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void AlignedFree(void* ptr);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void* AlignedAllocCore(nuint byteCount, nuint alignment);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void* AlignedReallocCore(
            void* ptr,
            nuint byteCount,
            nuint alignment);

        private static void ValidateAlignment(nuint alignment)
        {
            if (alignment == 0 || (alignment & (alignment - 1)) != 0)
            {
                throw new ArgumentException("Alignment must be a power of two.");
            }
        }

    }
}
