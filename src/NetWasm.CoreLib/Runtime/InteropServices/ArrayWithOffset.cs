// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// NetWasm adaptation: only primitive element arrays can be sized here, so other arrays are rejected
// as not isomorphic, as upstream does for arrays that are not pinnable.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace System.Runtime.InteropServices
{
    public readonly struct ArrayWithOffset : IEquatable<ArrayWithOffset>
    {
        private readonly object? m_array;
        private readonly int m_offset;
        private readonly int m_count;

        // From MAX_SIZE_FOR_INTEROP in mlinfo.h
        private const int MaxSizeForInterop = 0x7ffffff0;

        public ArrayWithOffset(object? array, int offset)
        {
            int totalSize = 0;
            if (array != null)
            {
                int elementSize = array switch
                {
                    byte[] or sbyte[] or bool[] => 1,
                    char[] or short[] or ushort[] => 2,
                    int[] or uint[] or float[] => 4,
                    long[] or ulong[] or double[] => 8,
                    nint[] or nuint[] => IntPtr.Size,
                    _ => 0,
                };
                if (elementSize == 0)
                {
                    throw new ArgumentException(SR.ArgumentException_NotIsomorphic);
                }

                nuint nativeTotalSize = (nuint)((Array)array).LongLength * (nuint)elementSize;
                if (nativeTotalSize > MaxSizeForInterop)
                {
                    throw new ArgumentException(SR.Argument_StructArrayTooLarge);
                }

                totalSize = (int)nativeTotalSize;
            }

            if ((uint)offset > (uint)totalSize)
            {
                throw new IndexOutOfRangeException(SR.IndexOutOfRange_ArrayWithOffset);
            }

            m_array = array;
            m_offset = offset;
            m_count = totalSize - offset;
        }

        public object? GetArray() => m_array;

        public int GetOffset() => m_offset;

        public override int GetHashCode() => m_count + m_offset;

        public override bool Equals([NotNullWhen(true)] object? obj)
        {
            return obj is ArrayWithOffset awo && Equals(awo);
        }

        public bool Equals(ArrayWithOffset obj)
        {
            return obj.m_array == m_array && obj.m_offset == m_offset && obj.m_count == m_count;
        }

        public static bool operator ==(ArrayWithOffset a, ArrayWithOffset b) => a.Equals(b);

        public static bool operator !=(ArrayWithOffset a, ArrayWithOffset b) => !(a == b);
    }
}
