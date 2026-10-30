// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// NetWasm's counterpart of upstream's Buffer.CoreCLR.cs: the runtime members that the copied
// Buffer.cs calls.

using System.Reflection;

namespace System
{
    public static partial class Buffer
    {
        // NetWasm's collectors neither move objects nor use write barriers, so references copy as
        // bytes.
        private static void BulkMoveWithWriteBarrierInternal(ref byte destination, ref byte source, nuint byteCount) =>
            SpanHelpers.Memmove(ref destination, ref source, byteCount);

        // Upstream's runtime polls for a pending suspension between chunks. NetWasm runs one thread,
        // so nothing waits.
        private static class Thread
        {
            internal static void FastPollGC()
            {
            }
        }

        // The Array members upstream's runtime provides. NetWasm reads no element type at run time,
        // so these recognize the primitive arrays, the only ones Buffer accepts; every other array
        // answers ELEMENT_TYPE_CLASS, which Buffer rejects.
        extension(Array array)
        {
            private nuint NativeLength => (nuint)(uint)array.Length;

            private CorElementType GetCorElementTypeOfElementType() => array switch
            {
                bool[] => CorElementType.ELEMENT_TYPE_BOOLEAN,
                char[] => CorElementType.ELEMENT_TYPE_CHAR,
                sbyte[] => CorElementType.ELEMENT_TYPE_I1,
                byte[] => CorElementType.ELEMENT_TYPE_U1,
                short[] => CorElementType.ELEMENT_TYPE_I2,
                ushort[] => CorElementType.ELEMENT_TYPE_U2,
                int[] => CorElementType.ELEMENT_TYPE_I4,
                uint[] => CorElementType.ELEMENT_TYPE_U4,
                long[] => CorElementType.ELEMENT_TYPE_I8,
                ulong[] => CorElementType.ELEMENT_TYPE_U8,
                float[] => CorElementType.ELEMENT_TYPE_R4,
                double[] => CorElementType.ELEMENT_TYPE_R8,
                nint[] => CorElementType.ELEMENT_TYPE_I,
                nuint[] => CorElementType.ELEMENT_TYPE_U,
                _ => CorElementType.ELEMENT_TYPE_CLASS,
            };

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
