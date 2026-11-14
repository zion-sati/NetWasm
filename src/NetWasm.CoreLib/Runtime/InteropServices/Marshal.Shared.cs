// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce (Marshal.cs).
// NetWasm's Marshal.cs holds the allocation, HRESULT and UTF-8 members, which a desktop test
// project compiles on their own; this file holds the rest of upstream's shared members except
// the structure-layout ones. NetWasm adaptations: one CoreLib serves wasm32 and wasm64, so
// IntPtr-width reads and writes branch on IntPtr.Size instead of TARGET_64BIT. QueryInterface,
// AddRef and Release would call native COM vtables, and GenerateGuidForType,
// GenerateProgIdForType, PrelinkAll and the delegate/function-pointer conversions need type
// reflection or runtime-generated trampolines; all throw PlatformNotSupportedException after
// their argument checks. The non-generic UnsafeAddrOfPinnedArrayElement resolves primitive
// element arrays only, since NetWasm exposes no element size for an arbitrary array. Internal
// ThrowHelper argument helpers and string.strlen/wcslen map to their public equivalents, and the
// generic element address uses Unsafe.SizeOf<T>() for upstream's sizeof(T). NetWasm builds strings
// from spans and arrays, so the pointer string constructors become span construction with the
// same decoding (ANSI is UTF-8, as for upstream's sbyte* constructor on Unix).


using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Security;
using System.Text;

namespace System.Runtime.InteropServices
{
    public static partial class Marshal
    {
        public static unsafe string? PtrToStringAnsi(IntPtr ptr)
        {
            if (IsNullOrWin32Atom(ptr))
            {
                return null;
            }

            return Encoding.UTF8.GetString(MemoryMarshal.CreateReadOnlySpanFromNullTerminated((byte*)ptr));
        }

        public static unsafe string PtrToStringAnsi(IntPtr ptr, int len)
        {
            ArgumentNullException.ThrowIfNull(ptr);
            ArgumentOutOfRangeException.ThrowIfNegative(len);

            return Encoding.UTF8.GetString(new ReadOnlySpan<byte>((byte*)ptr, len));
        }

        public static unsafe string? PtrToStringUni(IntPtr ptr)
        {
            if (IsNullOrWin32Atom(ptr))
            {
                return null;
            }

            return new string(MemoryMarshal.CreateReadOnlySpanFromNullTerminated((char*)ptr));
        }

        public static unsafe string PtrToStringUni(IntPtr ptr, int len)
        {
            ArgumentNullException.ThrowIfNull(ptr);
            ArgumentOutOfRangeException.ThrowIfNegative(len);

            return new string(new ReadOnlySpan<char>((char*)ptr, len));
        }

        public static unsafe int QueryInterface(IntPtr pUnk, in Guid iid, out IntPtr ppv)
        {
            ArgumentNullException.ThrowIfNull(pUnk);
            ppv = IntPtr.Zero;

            throw new PlatformNotSupportedException(SR.PlatformNotSupported_ComInterop);
        }

        public static unsafe int AddRef(IntPtr pUnk)
        {
            ArgumentNullException.ThrowIfNull(pUnk);

            throw new PlatformNotSupportedException(SR.PlatformNotSupported_ComInterop);
        }

        public static unsafe int Release(IntPtr pUnk)
        {
            ArgumentNullException.ThrowIfNull(pUnk);

            throw new PlatformNotSupportedException(SR.PlatformNotSupported_ComInterop);
        }

        /// <summary>
        /// IMPORTANT NOTICE: This method does not do any verification on the array.
        /// It must be used with EXTREME CAUTION since passing in invalid index or
        /// an array that is not pinned can cause unexpected results.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static unsafe IntPtr UnsafeAddrOfPinnedArrayElement(Array arr, int index)
        {
            ArgumentNullException.ThrowIfNull(arr);

            return arr switch
            {
                byte[] a => UnsafeAddrOfPinnedArrayElement(a, index),
                sbyte[] a => UnsafeAddrOfPinnedArrayElement(a, index),
                bool[] a => UnsafeAddrOfPinnedArrayElement(a, index),
                char[] a => UnsafeAddrOfPinnedArrayElement(a, index),
                short[] a => UnsafeAddrOfPinnedArrayElement(a, index),
                ushort[] a => UnsafeAddrOfPinnedArrayElement(a, index),
                int[] a => UnsafeAddrOfPinnedArrayElement(a, index),
                uint[] a => UnsafeAddrOfPinnedArrayElement(a, index),
                long[] a => UnsafeAddrOfPinnedArrayElement(a, index),
                ulong[] a => UnsafeAddrOfPinnedArrayElement(a, index),
                float[] a => UnsafeAddrOfPinnedArrayElement(a, index),
                double[] a => UnsafeAddrOfPinnedArrayElement(a, index),
                nint[] a => UnsafeAddrOfPinnedArrayElement(a, index),
                nuint[] a => UnsafeAddrOfPinnedArrayElement(a, index),
                _ => throw new PlatformNotSupportedException(SR.PlatformNotSupported_ArrayElementAddress),
            };
        }

        public static unsafe IntPtr UnsafeAddrOfPinnedArrayElement<T>(T[] arr, int index)
        {
            ArgumentNullException.ThrowIfNull(arr);

            // Unsafe.AsPointer is safe since array must be pinned
            void* pRawData = Unsafe.AsPointer(ref MemoryMarshal.GetArrayDataReference(arr));
            return (IntPtr)((byte*)pRawData + (uint)index * (nuint)Unsafe.SizeOf<T>());
        }

        public static void Copy(int[] source, int startIndex, IntPtr destination, int length)
        {
            CopyToNative(source, startIndex, destination, length);
        }

        public static void Copy(char[] source, int startIndex, IntPtr destination, int length)
        {
            CopyToNative(source, startIndex, destination, length);
        }

        public static void Copy(short[] source, int startIndex, IntPtr destination, int length)
        {
            CopyToNative(source, startIndex, destination, length);
        }

        public static void Copy(long[] source, int startIndex, IntPtr destination, int length)
        {
            CopyToNative(source, startIndex, destination, length);
        }

        public static void Copy(float[] source, int startIndex, IntPtr destination, int length)
        {
            CopyToNative(source, startIndex, destination, length);
        }

        public static void Copy(double[] source, int startIndex, IntPtr destination, int length)
        {
            CopyToNative(source, startIndex, destination, length);
        }

        public static void Copy(byte[] source, int startIndex, IntPtr destination, int length)
        {
            CopyToNative(source, startIndex, destination, length);
        }

        public static void Copy(IntPtr[] source, int startIndex, IntPtr destination, int length)
        {
            CopyToNative(source, startIndex, destination, length);
        }

        private static unsafe void CopyToNative<T>(T[] source, int startIndex, IntPtr destination, int length)
        {
            ArgumentNullException.ThrowIfNull(source);

            ArgumentNullException.ThrowIfNull(destination);

            // The rest of the argument validation is done by CopyTo
            new Span<T>(source, startIndex, length).CopyTo(new Span<T>((void*)destination, length));
        }

        public static void Copy(IntPtr source, int[] destination, int startIndex, int length)
        {
            CopyToManaged(source, destination, startIndex, length);
        }

        public static void Copy(IntPtr source, char[] destination, int startIndex, int length)
        {
            CopyToManaged(source, destination, startIndex, length);
        }

        public static void Copy(IntPtr source, short[] destination, int startIndex, int length)
        {
            CopyToManaged(source, destination, startIndex, length);
        }

        public static void Copy(IntPtr source, long[] destination, int startIndex, int length)
        {
            CopyToManaged(source, destination, startIndex, length);
        }

        public static void Copy(IntPtr source, float[] destination, int startIndex, int length)
        {
            CopyToManaged(source, destination, startIndex, length);
        }

        public static void Copy(IntPtr source, double[] destination, int startIndex, int length)
        {
            CopyToManaged(source, destination, startIndex, length);
        }

        public static void Copy(IntPtr source, byte[] destination, int startIndex, int length)
        {
            CopyToManaged(source, destination, startIndex, length);
        }

        public static void Copy(IntPtr source, IntPtr[] destination, int startIndex, int length)
        {
            CopyToManaged(source, destination, startIndex, length);
        }

        private static unsafe void CopyToManaged<T>(IntPtr source, T[] destination, int startIndex, int length)
        {
            ArgumentNullException.ThrowIfNull(destination);

            ArgumentNullException.ThrowIfNull(source);
            ArgumentOutOfRangeException.ThrowIfNegative(startIndex);
            ArgumentOutOfRangeException.ThrowIfNegative(length);

            // The rest of the argument validation is done by CopyTo

            new Span<T>((void*)source, length).CopyTo(new Span<T>(destination, startIndex, length));
        }

        public static unsafe byte ReadByte(IntPtr ptr, int ofs)
        {
            try
            {
                byte* addr = (byte*)ptr + ofs;
                return *addr;
            }
            catch (NullReferenceException)
            {
                // Convert NRE for null/near-null pointers to match the documented AccessViolationException.
                throw new AccessViolationException();
            }
        }

        public static byte ReadByte(IntPtr ptr) => ReadByte(ptr, 0);

        public static unsafe short ReadInt16(IntPtr ptr, int ofs)
        {
            try
            {
                byte* addr = (byte*)ptr + ofs;
                if ((unchecked((int)addr) & 0x1) == 0)
                {
                    // aligned read
                    return *((short*)addr);
                }
                else
                {
                    return Unsafe.ReadUnaligned<short>(addr);
                }
            }
            catch (NullReferenceException)
            {
                // Convert NRE for null/near-null pointers to match the documented AccessViolationException.
                throw new AccessViolationException();
            }
        }

        public static short ReadInt16(IntPtr ptr) => ReadInt16(ptr, 0);

        public static unsafe int ReadInt32(IntPtr ptr, int ofs)
        {
            try
            {
                byte* addr = (byte*)ptr + ofs;
                if ((unchecked((int)addr) & 0x3) == 0)
                {
                    // aligned read
                    return *((int*)addr);
                }
                else
                {
                    return Unsafe.ReadUnaligned<int>(addr);
                }
            }
            catch (NullReferenceException)
            {
                // Convert NRE for null/near-null pointers to match the documented AccessViolationException.
                throw new AccessViolationException();
            }
        }

        public static int ReadInt32(IntPtr ptr) => ReadInt32(ptr, 0);

        [RequiresDynamicCode("Marshalling code for the object might not be available")]
        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("ReadIntPtr(Object, Int32) may be unavailable in future releases.")]
        public static nint ReadIntPtr(object ptr, int ofs)
        {
            return IntPtr.Size == 8 ? (nint)ReadInt64(ptr, ofs) : (nint)ReadInt32(ptr, ofs);
        }

        public static nint ReadIntPtr(IntPtr ptr, int ofs)
        {
            return IntPtr.Size == 8 ? (nint)ReadInt64(ptr, ofs) : (nint)ReadInt32(ptr, ofs);
        }

        public static nint ReadIntPtr(IntPtr ptr) => ReadIntPtr(ptr, 0);

        public static unsafe long ReadInt64(IntPtr ptr, int ofs)
        {
            try
            {
                byte* addr = (byte*)ptr + ofs;
                if ((unchecked((int)addr) & 0x7) == 0)
                {
                    // aligned read
                    return *((long*)addr);
                }
                else
                {
                    return Unsafe.ReadUnaligned<long>(addr);
                }
            }
            catch (NullReferenceException)
            {
                // Convert NRE for null/near-null pointers to match the documented AccessViolationException.
                throw new AccessViolationException();
            }
        }

        public static long ReadInt64(IntPtr ptr) => ReadInt64(ptr, 0);

        public static unsafe void WriteByte(IntPtr ptr, int ofs, byte val)
        {
            try
            {
                byte* addr = (byte*)ptr + ofs;
                *addr = val;
            }
            catch (NullReferenceException)
            {
                // Convert NRE for null/near-null pointers to match the documented AccessViolationException.
                throw new AccessViolationException();
            }
        }

        public static void WriteByte(IntPtr ptr, byte val) => WriteByte(ptr, 0, val);

        public static unsafe void WriteInt16(IntPtr ptr, int ofs, short val)
        {
            try
            {
                byte* addr = (byte*)ptr + ofs;
                if ((unchecked((int)addr) & 0x1) == 0)
                {
                    // aligned write
                    *((short*)addr) = val;
                }
                else
                {
                    Unsafe.WriteUnaligned(addr, val);
                }
            }
            catch (NullReferenceException)
            {
                // Convert NRE for null/near-null pointers to match the documented AccessViolationException.
                throw new AccessViolationException();
            }
        }

        public static void WriteInt16(IntPtr ptr, short val) => WriteInt16(ptr, 0, val);

        public static void WriteInt16(IntPtr ptr, int ofs, char val) => WriteInt16(ptr, ofs, (short)val);

        [RequiresDynamicCode("Marshalling code for the object might not be available")]
        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("WriteInt16(Object, Int32, Char) may be unavailable in future releases.")]
        public static void WriteInt16([In, Out] object ptr, int ofs, char val) => WriteInt16(ptr, ofs, (short)val);

        public static void WriteInt16(IntPtr ptr, char val) => WriteInt16(ptr, 0, (short)val);

        public static unsafe void WriteInt32(IntPtr ptr, int ofs, int val)
        {
            try
            {
                byte* addr = (byte*)ptr + ofs;
                if ((unchecked((int)addr) & 0x3) == 0)
                {
                    // aligned write
                    *((int*)addr) = val;
                }
                else
                {
                    Unsafe.WriteUnaligned(addr, val);
                }
            }
            catch (NullReferenceException)
            {
                // Convert NRE for null/near-null pointers to match the documented AccessViolationException.
                throw new AccessViolationException();
            }
        }

        public static void WriteInt32(IntPtr ptr, int val) => WriteInt32(ptr, 0, val);

        public static void WriteIntPtr(IntPtr ptr, int ofs, nint val)
        {
            if (IntPtr.Size == 8)
            {
                WriteInt64(ptr, ofs, (long)val);
            }
            else
            {
                WriteInt32(ptr, ofs, (int)val);
            }
        }

        [RequiresDynamicCode("Marshalling code for the object might not be available")]
        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("WriteIntPtr(Object, Int32, IntPtr) may be unavailable in future releases.")]
        public static void WriteIntPtr(object ptr, int ofs, nint val)
        {
            if (IntPtr.Size == 8)
            {
                WriteInt64(ptr, ofs, (long)val);
            }
            else
            {
                WriteInt32(ptr, ofs, (int)val);
            }
        }

        public static void WriteIntPtr(IntPtr ptr, nint val) => WriteIntPtr(ptr, 0, val);

        public static unsafe void WriteInt64(IntPtr ptr, int ofs, long val)
        {
            try
            {
                byte* addr = (byte*)ptr + ofs;
                if ((unchecked((int)addr) & 0x7) == 0)
                {
                    // aligned write
                    *((long*)addr) = val;
                }
                else
                {
                    Unsafe.WriteUnaligned(addr, val);
                }
            }
            catch (NullReferenceException)
            {
                // Convert NRE for null/near-null pointers to match the documented AccessViolationException.
                throw new AccessViolationException();
            }
        }

        public static void WriteInt64(IntPtr ptr, long val) => WriteInt64(ptr, 0, val);

        public static void Prelink(MethodInfo m)
        {
            ArgumentNullException.ThrowIfNull(m);

            PrelinkCore(m);
        }

        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2070:UnrecognizedReflectionPattern",
            Justification = "This only needs to prelink methods that are actually used")]
        public static void PrelinkAll(Type c)
        {
            ArgumentNullException.ThrowIfNull(c);

            throw new PlatformNotSupportedException(SR.PlatformNotSupported_Prelink);
        }

        [RequiresAssemblyFiles("Windows only assigns HINSTANCE to assemblies loaded from disk. " +
            "This API will return -1 for modules without a file on disk.")]
        public static IntPtr GetHINSTANCE(Module m)
        {
            ArgumentNullException.ThrowIfNull(m);

            return (IntPtr)(-1);
        }

        public static IntPtr SecureStringToBSTR(SecureString s)
        {
            if (s is null)
            {
                throw new ArgumentNullException(nameof(s));
            }

            return s.MarshalToBSTR();
        }

        public static IntPtr SecureStringToCoTaskMemAnsi(SecureString s)
        {
            if (s is null)
            {
                throw new ArgumentNullException(nameof(s));
            }

            return s.MarshalToString(globalAlloc: false, unicode: false);
        }

        public static IntPtr SecureStringToCoTaskMemUnicode(SecureString s)
        {
            if (s is null)
            {
                throw new ArgumentNullException(nameof(s));
            }

            return s.MarshalToString(globalAlloc: false, unicode: true);
        }

        public static IntPtr SecureStringToGlobalAllocAnsi(SecureString s)
        {
            if (s is null)
            {
                throw new ArgumentNullException(nameof(s));
            }

            return s.MarshalToString(globalAlloc: true, unicode: false);
        }

        public static IntPtr SecureStringToGlobalAllocUnicode(SecureString s)
        {
            if (s is null)
            {
                throw new ArgumentNullException(nameof(s));
            }

            return s.MarshalToString(globalAlloc: true, unicode: true);
        }

        public static unsafe IntPtr StringToHGlobalAnsi(string? s)
        {
            if (s is null)
            {
                return IntPtr.Zero;
            }

            long lnb = (s.Length + 1) * (long)SystemMaxDBCSCharSize;
            int nb = (int)lnb;

            // Overflow checking
            if (nb != lnb)
            {
                throw new ArgumentOutOfRangeException(nameof(s));
            }

            IntPtr ptr = AllocHGlobal((IntPtr)nb);

            StringToAnsiString(s, (byte*)ptr, nb);
            return ptr;
        }

        public static unsafe IntPtr StringToHGlobalUni(string? s)
        {
            if (s is null)
            {
                return IntPtr.Zero;
            }

            int nb = (s.Length + 1) * 2;

            // Overflow checking
            if (nb < s.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(s));
            }

            IntPtr ptr = AllocHGlobal((IntPtr)nb);

            s.CopyTo(new Span<char>((char*)ptr, s.Length));
            ((char*)ptr)[s.Length] = '\0';

            return ptr;
        }

        private static unsafe IntPtr StringToHGlobalUTF8(string? s)
        {
            if (s is null)
            {
                return IntPtr.Zero;
            }

            int nb = Encoding.UTF8.GetMaxByteCount(s.Length);

            IntPtr ptr = AllocHGlobal(checked(nb + 1));

            byte* pbMem = (byte*)ptr;
            int nbWritten = Encoding.UTF8.GetBytes(s, new Span<byte>(pbMem, nb));
            pbMem[nbWritten] = 0;

            return ptr;
        }

        public static unsafe IntPtr StringToCoTaskMemUni(string? s)
        {
            if (s is null)
            {
                return IntPtr.Zero;
            }

            int nb = (s.Length + 1) * 2;

            // Overflow checking
            if (nb < s.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(s));
            }

            IntPtr ptr = AllocCoTaskMem(nb);

            s.CopyTo(new Span<char>((char*)ptr, s.Length));
            ((char*)ptr)[s.Length] = '\0';

            return ptr;
        }

        public static unsafe IntPtr StringToCoTaskMemUTF8(string? s)
        {
            if (s is null)
            {
                return IntPtr.Zero;
            }

            int nb = Encoding.UTF8.GetMaxByteCount(s.Length);

            IntPtr ptr = AllocCoTaskMem(checked(nb + 1));

            byte* pbMem = (byte*)ptr;
            int nbWritten = Encoding.UTF8.GetBytes(s, new Span<byte>(pbMem, nb));
            pbMem[nbWritten] = 0;

            return ptr;
        }

        public static unsafe IntPtr StringToCoTaskMemAnsi(string? s)
        {
            if (s is null)
            {
                return IntPtr.Zero;
            }

            long lnb = (s.Length + 1) * (long)SystemMaxDBCSCharSize;
            int nb = (int)lnb;

            // Overflow checking
            if (nb != lnb)
            {
                throw new ArgumentOutOfRangeException(nameof(s));
            }

            IntPtr ptr = AllocCoTaskMem(nb);

            StringToAnsiString(s, (byte*)ptr, nb);
            return ptr;
        }

        /// <summary>
        /// Generates a GUID for the specified type. If the type has a GUID in the
        /// metadata then it is returned otherwise a stable guid is generated based
        /// on the fully qualified name of the type.
        /// </summary>
        public static Guid GenerateGuidForType(Type type)
        {
            ArgumentNullException.ThrowIfNull(type);

            throw new PlatformNotSupportedException(SR.PlatformNotSupported_TypeGuid);
        }

        /// <summary>
        /// This method generates a PROGID for the specified type. If the type has
        /// a PROGID in the metadata then it is returned otherwise a stable PROGID
        /// is generated based on the fully qualified name of the type.
        /// </summary>
        [RequiresUnreferencedCode("Built-in COM support is not trim compatible", Url = "https://aka.ms/dotnet-illink/com")]
        public static string? GenerateProgIdForType(Type type)
        {
            ArgumentNullException.ThrowIfNull(type);

            throw new PlatformNotSupportedException(SR.PlatformNotSupported_TypeGuid);
        }

        [RequiresDynamicCode("Marshalling code for the delegate might not be available. Use the GetDelegateForFunctionPointer<TDelegate> overload instead.")]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static Delegate GetDelegateForFunctionPointer(IntPtr ptr, Type t)
        {
            ArgumentNullException.ThrowIfNull(t);
            ArgumentNullException.ThrowIfNull(ptr);

            throw new PlatformNotSupportedException(SR.PlatformNotSupported_DelegateMarshalling);
        }

        public static TDelegate GetDelegateForFunctionPointer<TDelegate>(IntPtr ptr)
        {
            ArgumentNullException.ThrowIfNull(ptr);

            throw new PlatformNotSupportedException(SR.PlatformNotSupported_DelegateMarshalling);
        }

        [RequiresDynamicCode("Marshalling code for the delegate might not be available. Use the GetFunctionPointerForDelegate<TDelegate> overload instead.")]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static IntPtr GetFunctionPointerForDelegate(Delegate d)
        {
            ArgumentNullException.ThrowIfNull(d);

            throw new PlatformNotSupportedException(SR.PlatformNotSupported_DelegateMarshalling);
        }

        [UnconditionalSuppressMessage("AotAnalysis", "IL3050:AotUnfriendlyApi",
            Justification = "AOT compilers can see the T.")]
        public static IntPtr GetFunctionPointerForDelegate<TDelegate>(TDelegate d) where TDelegate : notnull
        {
            return GetFunctionPointerForDelegate((Delegate)(object)d);
        }

        public static int GetHRForLastWin32Error()
        {
            int dwLastError = GetLastPInvokeError();
            if ((dwLastError & 0x80000000) == 0x80000000)
            {
                return dwLastError;
            }

            return (dwLastError & 0x0000FFFF) | unchecked((int)0x80070000);
        }

        public static unsafe void ZeroFreeBSTR(IntPtr s)
        {
            if (s == IntPtr.Zero)
            {
                return;
            }
            NativeMemory.Clear((void*)s, SysStringByteLen(s));
            FreeBSTR(s);
        }

        public static void ZeroFreeCoTaskMemAnsi(IntPtr s)
        {
            ZeroFreeCoTaskMemUTF8(s);
        }

        public static unsafe void ZeroFreeCoTaskMemUnicode(IntPtr s)
        {
            if (s == IntPtr.Zero)
            {
                return;
            }
            NativeMemory.Clear((void*)s, (nuint)MemoryMarshal.CreateReadOnlySpanFromNullTerminated((char*)s).Length * sizeof(char));
            FreeCoTaskMem(s);
        }

        public static unsafe void ZeroFreeCoTaskMemUTF8(IntPtr s)
        {
            if (s == IntPtr.Zero)
            {
                return;
            }
            NativeMemory.Clear((void*)s, (nuint)MemoryMarshal.CreateReadOnlySpanFromNullTerminated((byte*)s).Length);
            FreeCoTaskMem(s);
        }

        public static unsafe void ZeroFreeGlobalAllocAnsi(IntPtr s)
        {
            if (s == IntPtr.Zero)
            {
                return;
            }
            NativeMemory.Clear((void*)s, (nuint)MemoryMarshal.CreateReadOnlySpanFromNullTerminated((byte*)s).Length);
            FreeHGlobal(s);
        }

        public static unsafe void ZeroFreeGlobalAllocUnicode(IntPtr s)
        {
            if (s == IntPtr.Zero)
            {
                return;
            }
            NativeMemory.Clear((void*)s, (nuint)MemoryMarshal.CreateReadOnlySpanFromNullTerminated((char*)s).Length * sizeof(char));
            FreeHGlobal(s);
        }

        public static unsafe IntPtr StringToBSTR(string? s)
        {
            if (s is null)
            {
                return IntPtr.Zero;
            }

            IntPtr bstr = AllocBSTR(s.Length);

            s.CopyTo(new Span<char>((char*)bstr, s.Length)); // AllocBSTR already included the null terminator

            return bstr;
        }

        public static string PtrToStringBSTR(IntPtr ptr)
        {
            ArgumentNullException.ThrowIfNull(ptr);

            return PtrToStringUni(ptr, (int)(SysStringByteLen(ptr) / sizeof(char)));
        }

        internal static unsafe uint SysStringByteLen(IntPtr s)
        {
            return *(((uint*)s) - 1);
        }

        [SupportedOSPlatform("windows")]
        public static Type? GetTypeFromCLSID(Guid clsid) => GetTypeFromCLSID(clsid, null, throwOnError: false);

        /// <summary>
        /// Initializes the underlying handle of a newly created <see cref="SafeHandle" /> to the provided value.
        /// </summary>
        /// <param name="safeHandle">The <see cref="SafeHandle"/> instance to update.</param>
        /// <param name="handle">The pre-existing handle.</param>
        public static void InitHandle(SafeHandle safeHandle, IntPtr handle)
        {
            // To help maximize performance of P/Invokes, don't check if safeHandle is null.
            safeHandle.SetHandle(handle);
        }

        public static int GetLastWin32Error()
        {
            return GetLastPInvokeError();
        }
    }
}
