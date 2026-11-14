// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Buffers
{
    internal sealed partial class ArrayMemoryPool<T> : MemoryPool<T>
    {
        public sealed override int MaxBufferSize => Array.MaxLength;

#pragma warning disable CS8500 // The unconstrained sizeof(T) is intentional and matches the upstream element-size heuristic.
        public sealed override unsafe IMemoryOwner<T> Rent(int minimumBufferSize = -1)
        {
            if (minimumBufferSize == -1)
            {
                minimumBufferSize = 1 + (4095 / sizeof(T));
            }
            else if ((uint)minimumBufferSize > Array.MaxLength)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.minimumBufferSize);
            }

            return new ArrayMemoryPoolBuffer(minimumBufferSize);
        }
#pragma warning restore CS8500

        // ArrayMemoryPool is a shared pool, so Dispose is a no-op even if native resources are
        // added to the implementation in the future.
        protected sealed override void Dispose(bool disposing) { }
    }
}
