// Portions adapted from dotnet/runtime System.Private.CoreLib NativeMemory.cs.
// The upstream implementation is licensed under the MIT license.
// Source reference: dotnet/runtime commit 811225a482702af7ecc35d817966bc70b88a3a23.

using System.Runtime.CompilerServices;

namespace System.Runtime.InteropServices
{
    /// <summary>Provides access to unmanaged memory allocation primitives.</summary>
    public static unsafe partial class NativeMemory
    {
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void* Alloc(nuint byteCount);

        /// <summary>Allocates a block of memory of the specified size, in elements.</summary>
        public static void* Alloc(nuint elementCount, nuint elementSize) =>
            Alloc(GetByteCount(elementCount, elementSize));

        /// <summary>Allocates and clears a block of memory of the specified size, in bytes.</summary>
        public static void* AllocZeroed(nuint byteCount) =>
            AllocZeroed(byteCount, elementSize: 1);

        /// <summary>Allocates and clears a block of memory of the specified size, in elements.</summary>
        public static void* AllocZeroed(nuint elementCount, nuint elementSize)
        {
            var byteCount = GetByteCount(elementCount, elementSize);
            var result = Alloc(byteCount);
            Clear(result, byteCount);
            return result;
        }

        /// <summary>Clears a block of unmanaged memory.</summary>
        public static void Clear(void* ptr, nuint byteCount)
        {
            var bytes = (byte*)ptr;
            for (nuint index = 0; index < byteCount; index++)
            {
                bytes[index] = 0;
            }
        }

        /// <summary>Copies a block of unmanaged memory, preserving overlap semantics.</summary>
        public static void Copy(void* source, void* destination, nuint byteCount)
        {
            if (byteCount == 0 || source == destination)
            {
                return;
            }

            var sourceAddress = (nuint)source;
            var destinationAddress = (nuint)destination;
            var sourceBytes = (byte*)source;
            var destinationBytes = (byte*)destination;

            if (destinationAddress < sourceAddress ||
                destinationAddress - sourceAddress >= byteCount)
            {
                for (nuint index = 0; index < byteCount; index++)
                {
                    destinationBytes[index] = sourceBytes[index];
                }
                return;
            }

            for (var index = byteCount; index != 0;)
            {
                index--;
                destinationBytes[index] = sourceBytes[index];
            }
        }

        /// <summary>Fills a block of unmanaged memory with a byte value.</summary>
        public static void Fill(void* ptr, nuint byteCount, byte value)
        {
            var bytes = (byte*)ptr;
            for (nuint index = 0; index < byteCount; index++)
            {
                bytes[index] = value;
            }
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

        private static nuint GetByteCount(nuint elementCount, nuint elementSize) =>
            elementSize != 0 && elementCount > nuint.MaxValue / elementSize
                ? nuint.MaxValue
                : elementCount * elementSize;
    }
}
