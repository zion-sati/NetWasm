// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit 811225a482702af7ecc35d817966bc70b88a3a23.

using System.Runtime.CompilerServices;
using System.Text;

namespace System.Runtime.InteropServices.Marshalling
{
    [CustomMarshaller(typeof(string), MarshalMode.Default, typeof(Utf8StringMarshaller))]
    [CustomMarshaller(typeof(string), MarshalMode.ManagedToUnmanagedIn, typeof(ManagedToUnmanagedIn))]
    public static unsafe class Utf8StringMarshaller
    {
        public static byte* ConvertToUnmanaged(string? managed)
        {
            if (managed is null)
            {
                return null;
            }

            var exactByteCount = checked(Encoding.UTF8.GetByteCount(managed) + 1);
            var memory = (byte*)Marshal.AllocCoTaskMem(exactByteCount);
            var buffer = new Span<byte>(memory, exactByteCount);
            var byteCount = Encoding.UTF8.GetBytes(managed, buffer);
            buffer[byteCount] = 0;
            return memory;
        }

        public static string? ConvertToManaged(byte* unmanaged) =>
            Marshal.PtrToStringUTF8((IntPtr)unmanaged);

        public static void Free(byte* unmanaged) =>
            Marshal.FreeCoTaskMem((IntPtr)unmanaged);

        public ref struct ManagedToUnmanagedIn
        {
            private byte* _unmanagedValue;
            private bool _allocated;

            public static int BufferSize => 0x100;

            public void FromManaged(string? managed, Span<byte> buffer)
            {
                _allocated = false;
                if (managed is null)
                {
                    _unmanagedValue = null;
                    return;
                }

                const int MaxUtf8BytesPerChar = 3;
                if ((long)MaxUtf8BytesPerChar * managed.Length >= buffer.Length)
                {
                    var exactByteCount = checked(Encoding.UTF8.GetByteCount(managed) + 1);
                    if (exactByteCount > buffer.Length)
                    {
                        buffer = new Span<byte>(
                            NativeMemory.Alloc((nuint)exactByteCount),
                            exactByteCount);
                        _allocated = true;
                    }
                }

                _unmanagedValue = (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(buffer));
                var byteCount = Encoding.UTF8.GetBytes(managed, buffer);
                buffer[byteCount] = 0;
            }

            public byte* ToUnmanaged() => _unmanagedValue;

            public void Free()
            {
                if (_allocated)
                {
                    NativeMemory.Free(_unmanagedValue);
                }
            }
        }
    }
}
