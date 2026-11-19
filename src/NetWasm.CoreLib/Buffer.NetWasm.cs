// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// NetWasm's counterpart of upstream's Buffer.CoreCLR.cs: the runtime members that the copied
// Buffer.cs calls.

using System.Reflection;
using System.Runtime.CompilerServices;

namespace System
{
    public static partial class Buffer
    {
        // NetWasm's collectors do not move objects, so references copy as bytes. A collector with
        // generations is then told where they went.
        private static void BulkMoveWithWriteBarrierInternal(ref byte destination, ref byte source, nuint byteCount)
        {
            SpanHelpers.Memmove(ref destination, ref source, byteCount);
            RecordReferenceWrites(ref destination, byteCount);
        }

        // The write barrier for a range of memory that received references. The compiler emits it for a
        // program whose collector has generations, and nothing for any other.
        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void RecordReferenceWrites(ref byte destination, nuint byteCount);

        // Upstream's runtime polls for a pending suspension between chunks. NetWasm runs one thread,
        // so nothing waits.
        private static class Thread
        {
            internal static void FastPollGC()
            {
            }
        }

        // The Array members upstream's runtime provides.
        extension(Array array)
        {
            private nuint NativeLength => (nuint)(uint)array.Length;

            private CorElementType GetCorElementTypeOfElementType() => Array.GetPrimitiveElementType(array);

            private int GetElementSize() => array.GetCorElementTypeOfElementType() switch
            {
                CorElementType.ELEMENT_TYPE_BOOLEAN or CorElementType.ELEMENT_TYPE_I1 or CorElementType.ELEMENT_TYPE_U1 => 1,
                CorElementType.ELEMENT_TYPE_CHAR or CorElementType.ELEMENT_TYPE_I2 or CorElementType.ELEMENT_TYPE_U2 => 2,
                CorElementType.ELEMENT_TYPE_I4 or CorElementType.ELEMENT_TYPE_U4 or CorElementType.ELEMENT_TYPE_R4 => 4,
                CorElementType.ELEMENT_TYPE_I8 or CorElementType.ELEMENT_TYPE_U8 or CorElementType.ELEMENT_TYPE_R8 => 8,
                _ => IntPtr.Size,
            };
        }
    }
}
