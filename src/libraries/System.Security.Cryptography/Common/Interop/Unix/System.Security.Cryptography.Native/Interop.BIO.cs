// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// NetWasm: dotnet/runtime deae2e32's (.NET 11.0.0) declarations, with BioNewFile reading through the managed file APIs.

using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

internal static partial class Interop
{
    internal static partial class Crypto
    {
        [LibraryImport(Libraries.CryptoNative, EntryPoint = "CryptoNative_CreateMemoryBio")]
        internal static partial SafeBioHandle CreateMemoryBio();

        // NetWasm: native code has no file system, so a file is read with the managed file APIs (which the WASI host's
        // preopens govern) into a memory BIO. A file that cannot be read gives an invalid handle, as BIO_new_file
        // does, with no OpenSSL error queued. Writing is not supported: only the CRL cache writes, after a download
        // NetWasm does not make (X509ResourceClient has no client).
        internal static SafeBioHandle BioNewFile(string filename, string mode)
        {
            byte[] contents;
            try
            {
                if (mode is not ("rb" or "r"))
                {
                    return new SafeBioHandle();
                }

                contents = File.ReadAllBytes(filename);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                return new SafeBioHandle();
            }

            SafeBioHandle bio = CreateMemoryBio();
            if (!bio.IsInvalid && BioWrite(bio, contents) != contents.Length)
            {
                bio.Dispose();
                return new SafeBioHandle();
            }

            return bio;
        }

        [LibraryImport(Libraries.CryptoNative, EntryPoint = "CryptoNative_BioDestroy")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool BioDestroy(IntPtr a);

        [LibraryImport(Libraries.CryptoNative, EntryPoint = "CryptoNative_BioGets")]
        private static unsafe partial int BioGets(SafeBioHandle b, byte* buf, int size);

        internal static unsafe int BioGets(SafeBioHandle b, Span<byte> buf)
        {
            fixed (byte* pBuf = buf)
            {
                return BioGets(b, pBuf, buf.Length);
            }
        }

        [LibraryImport(Libraries.CryptoNative, EntryPoint = "CryptoNative_BioRead")]
        private static partial int BioRead(SafeBioHandle b, Span<byte> data, int len);
        internal static int BioRead(SafeBioHandle b, Span<byte> data) => BioRead(b, data, data.Length);

        [LibraryImport(Libraries.CryptoNative, EntryPoint = "CryptoNative_BioWrite")]
        internal static partial int BioWrite(SafeBioHandle b, byte[] data, int len);

        internal static int BioWrite(SafeBioHandle b, ReadOnlySpan<byte> data) =>
            BioWrite(b, ref MemoryMarshal.GetReference(data), data.Length);

        [LibraryImport(Libraries.CryptoNative, EntryPoint = "CryptoNative_BioWrite")]
        private static partial int BioWrite(SafeBioHandle b, ref byte data, int len);

        [LibraryImport(Libraries.CryptoNative, EntryPoint = "CryptoNative_GetMemoryBioSize")]
        internal static partial int GetMemoryBioSize(SafeBioHandle bio);
    }
}
