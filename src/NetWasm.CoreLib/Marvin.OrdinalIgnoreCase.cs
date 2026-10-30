// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime at commit
// 474ed963699fc29809015392738a15231aaae2de. NetWasm uses its invariant
// UTF-16 code-unit casing boundary in place of Globalization.Ordinal.

using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Unicode;

namespace System
{
    internal static partial class Marvin
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int ComputeHash32OrdinalIgnoreCase(
            ref char data,
            int count,
            uint p0,
            uint p1)
        {
            uint ucount = (uint)count;
            nuint byteOffset = 0;
            uint tempValue;

            while (ucount >= 2)
            {
                tempValue = Unsafe.ReadUnaligned<uint>(ref Unsafe.As<char, byte>(ref Unsafe.AddByteOffset(ref data, byteOffset)));
                if (!Utf16Utility.AllCharsInUInt32AreAscii(tempValue))
                {
                    goto NotAscii;
                }
                p0 += Utf16Utility.ConvertAllAsciiCharsInUInt32ToUppercase(tempValue);
                Block(ref p0, ref p1);

                byteOffset += 4;
                ucount -= 2;
            }

            Debug.Assert(ucount < 2);

            if (ucount > 0)
            {
                tempValue = Unsafe.AddByteOffset(ref data, byteOffset);
                if (tempValue > 0x7Fu)
                {
                    goto NotAscii;
                }

                if (BitConverter.IsLittleEndian)
                {
                    p0 += Utf16Utility.ConvertAllAsciiCharsInUInt32ToUppercase(tempValue) + (0x800000u - 0x80u);
                }
                else
                {
                    p0 += (Utf16Utility.ConvertAllAsciiCharsInUInt32ToUppercase(tempValue) << 16) + 0x8000u - 0x80000000u;
                }
            }
            if (BitConverter.IsLittleEndian)
            {
                p0 += 0x80u;
            }
            else
            {
                p0 += 0x80000000u;
            }

            Block(ref p0, ref p1);
            Block(ref p0, ref p1);

            return (int)(p1 ^ p0);

        NotAscii:
            Debug.Assert(ucount <= int.MaxValue);
            return ComputeHash32OrdinalIgnoreCaseSlow(ref Unsafe.AddByteOffset(ref data, byteOffset), (int)ucount, p0, p1);
        }

        private static unsafe int ComputeHash32OrdinalIgnoreCaseSlow(ref char data, int count, uint p0, uint p1)
        {
            Debug.Assert(count > 0);

            char[]? borrowedArr = null;
            Span<char> scratch = (uint)count <= 64 ? stackalloc char[64] : (borrowedArr = ArrayPool<char>.Shared.Rent(count));

            int charsWritten = new ReadOnlySpan<char>(ref data, count).ToUpperOrdinal(scratch);
            Debug.Assert(charsWritten == count);

            int hash = ComputeHash32(ref Unsafe.As<char, byte>(ref MemoryMarshal.GetReference(scratch)), (uint)charsWritten * 2, p0, p1);

            if (borrowedArr != null)
            {
                ArrayPool<char>.Shared.Return(borrowedArr);
            }

            return hash;
        }
    }
}
