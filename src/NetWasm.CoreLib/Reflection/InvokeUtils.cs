// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// PrimitiveWiden(ref byte, ref byte, ...) from upstream's InvokeUtils.cs (dotnet/runtime
// deae2e32bf0e018e09d64597b7c59b62b73bb3ce), unchanged: Array.Copy widens primitive elements with it.

using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace System.Reflection
{
    internal static class InvokeUtils
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void PrimitiveWiden(ref byte srcElement, ref byte destElement, CorElementType srcElType, CorElementType destElType)
        {
            switch (srcElType)
            {
                case CorElementType.ELEMENT_TYPE_U1:
                    switch (destElType)
                    {
                        case CorElementType.ELEMENT_TYPE_CHAR:
                        case CorElementType.ELEMENT_TYPE_I2:
                        case CorElementType.ELEMENT_TYPE_U2:
                            Unsafe.As<byte, ushort>(ref destElement) = srcElement; break;
                        case CorElementType.ELEMENT_TYPE_I4:
                        case CorElementType.ELEMENT_TYPE_U4:
                            Unsafe.As<byte, uint>(ref destElement) = srcElement; break;
                        case CorElementType.ELEMENT_TYPE_I8:
                        case CorElementType.ELEMENT_TYPE_U8:
                            Unsafe.As<byte, ulong>(ref destElement) = srcElement; break;
                        case CorElementType.ELEMENT_TYPE_R4:
                            Unsafe.As<byte, float>(ref destElement) = srcElement; break;
                        case CorElementType.ELEMENT_TYPE_R8:
                            Unsafe.As<byte, double>(ref destElement) = srcElement; break;
                        default:
                            Debug.Fail("Expected to be unreachable"); break;
                    }
                    break;

                case CorElementType.ELEMENT_TYPE_I1:
                    switch (destElType)
                    {
                        case CorElementType.ELEMENT_TYPE_I2:
                            Unsafe.As<byte, short>(ref destElement) = Unsafe.As<byte, sbyte>(ref srcElement); break;
                        case CorElementType.ELEMENT_TYPE_I4:
                            Unsafe.As<byte, int>(ref destElement) = Unsafe.As<byte, sbyte>(ref srcElement); break;
                        case CorElementType.ELEMENT_TYPE_I8:
                            Unsafe.As<byte, long>(ref destElement) = Unsafe.As<byte, sbyte>(ref srcElement); break;
                        case CorElementType.ELEMENT_TYPE_R4:
                            Unsafe.As<byte, float>(ref destElement) = Unsafe.As<byte, sbyte>(ref srcElement); break;
                        case CorElementType.ELEMENT_TYPE_R8:
                            Unsafe.As<byte, double>(ref destElement) = Unsafe.As<byte, sbyte>(ref srcElement); break;
                        default:
                            Debug.Fail("Array.Copy from I1 to another type hit unsupported widening conversion"); break;
                    }
                    break;

                case CorElementType.ELEMENT_TYPE_U2:
                case CorElementType.ELEMENT_TYPE_CHAR:
                    switch (destElType)
                    {
                        case CorElementType.ELEMENT_TYPE_U2:
                        case CorElementType.ELEMENT_TYPE_CHAR:
                            // U2 and CHAR are identical in conversion
                            Unsafe.As<byte, ushort>(ref destElement) = Unsafe.As<byte, ushort>(ref srcElement); break;
                        case CorElementType.ELEMENT_TYPE_I4:
                        case CorElementType.ELEMENT_TYPE_U4:
                            Unsafe.As<byte, uint>(ref destElement) = Unsafe.As<byte, ushort>(ref srcElement); break;
                        case CorElementType.ELEMENT_TYPE_I8:
                        case CorElementType.ELEMENT_TYPE_U8:
                            Unsafe.As<byte, ulong>(ref destElement) = Unsafe.As<byte, ushort>(ref srcElement); break;
                        case CorElementType.ELEMENT_TYPE_R4:
                            Unsafe.As<byte, float>(ref destElement) = Unsafe.As<byte, ushort>(ref srcElement); break;
                        case CorElementType.ELEMENT_TYPE_R8:
                            Unsafe.As<byte, double>(ref destElement) = Unsafe.As<byte, ushort>(ref srcElement); break;
                        default:
                            Debug.Fail("Array.Copy from U2 to another type hit unsupported widening conversion"); break;
                    }
                    break;

                case CorElementType.ELEMENT_TYPE_I2:
                    switch (destElType)
                    {
                        case CorElementType.ELEMENT_TYPE_I4:
                            Unsafe.As<byte, int>(ref destElement) = Unsafe.As<byte, short>(ref srcElement); break;
                        case CorElementType.ELEMENT_TYPE_I8:
                            Unsafe.As<byte, long>(ref destElement) = Unsafe.As<byte, short>(ref srcElement); break;
                        case CorElementType.ELEMENT_TYPE_R4:
                            Unsafe.As<byte, float>(ref destElement) = Unsafe.As<byte, short>(ref srcElement); break;
                        case CorElementType.ELEMENT_TYPE_R8:
                            Unsafe.As<byte, double>(ref destElement) = Unsafe.As<byte, short>(ref srcElement); break;
                        default:
                            Debug.Fail("Array.Copy from I2 to another type hit unsupported widening conversion"); break;
                    }
                    break;

                case CorElementType.ELEMENT_TYPE_U4:
                    switch (destElType)
                    {
                        case CorElementType.ELEMENT_TYPE_I8:
                        case CorElementType.ELEMENT_TYPE_U8:
                            Unsafe.As<byte, ulong>(ref destElement) = Unsafe.As<byte, uint>(ref srcElement); break;
                        case CorElementType.ELEMENT_TYPE_R4:
                            Unsafe.As<byte, float>(ref destElement) = Unsafe.As<byte, uint>(ref srcElement); break;
                        case CorElementType.ELEMENT_TYPE_R8:
                            Unsafe.As<byte, double>(ref destElement) = Unsafe.As<byte, uint>(ref srcElement); break;
                        default:
                            Debug.Fail("Array.Copy from U4 to another type hit unsupported widening conversion"); break;
                    }
                    break;

                case CorElementType.ELEMENT_TYPE_I4:
                    switch (destElType)
                    {
                        case CorElementType.ELEMENT_TYPE_I8:
                            Unsafe.As<byte, long>(ref destElement) = Unsafe.As<byte, int>(ref srcElement); break;
                        case CorElementType.ELEMENT_TYPE_R4:
                            Unsafe.As<byte, float>(ref destElement) = Unsafe.As<byte, int>(ref srcElement); break;
                        case CorElementType.ELEMENT_TYPE_R8:
                            Unsafe.As<byte, double>(ref destElement) = Unsafe.As<byte, int>(ref srcElement); break;
                        default:
                            Debug.Fail("Array.Copy from I4 to another type hit unsupported widening conversion"); break;
                    }
                    break;

                case CorElementType.ELEMENT_TYPE_U8:
                    switch (destElType)
                    {
                        case CorElementType.ELEMENT_TYPE_R4:
                            Unsafe.As<byte, float>(ref destElement) = Unsafe.As<byte, ulong>(ref srcElement); break;
                        case CorElementType.ELEMENT_TYPE_R8:
                            Unsafe.As<byte, double>(ref destElement) = Unsafe.As<byte, ulong>(ref srcElement); break;
                        default:
                            Debug.Fail("Array.Copy from U8 to another type hit unsupported widening conversion"); break;
                    }
                    break;

                case CorElementType.ELEMENT_TYPE_I8:
                    switch (destElType)
                    {
                        case CorElementType.ELEMENT_TYPE_R4:
                            Unsafe.As<byte, float>(ref destElement) = Unsafe.As<byte, long>(ref srcElement); break;
                        case CorElementType.ELEMENT_TYPE_R8:
                            Unsafe.As<byte, double>(ref destElement) = Unsafe.As<byte, long>(ref srcElement); break;
                        default:
                            Debug.Fail("Array.Copy from I8 to another type hit unsupported widening conversion"); break;
                    }
                    break;

                case CorElementType.ELEMENT_TYPE_R4:
                    Debug.Assert(destElType == CorElementType.ELEMENT_TYPE_R8);
                    Unsafe.As<byte, double>(ref destElement) = Unsafe.As<byte, float>(ref srcElement); break;

                default:
                    Debug.Fail("Fell through outer switch in PrimitiveWiden!  Unknown primitive type for source array!"); break;
            }
        }
    }
}
