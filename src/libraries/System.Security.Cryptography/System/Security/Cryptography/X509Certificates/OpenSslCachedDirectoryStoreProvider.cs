// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// NetWasm: dotnet/runtime deae2e32's (.NET 11.0.0) provider, which reads the store's files with the managed APIs.

using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32.SafeHandles;

namespace System.Security.Cryptography.X509Certificates
{
    internal sealed class OpenSslCachedDirectoryStoreProvider
    {
        // These intervals are mostly arbitrary.
        // Prior to this caching these stores were always read "hot" from disk, and 30 seconds
        // seems like "long enough" for performance gains with "short enough" that if the filesystem
        // has LastWrite updating disabled that the process will be mostly responsive.
        private static readonly TimeSpan s_lastWriteRecheckInterval = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan s_assumeInvalidInterval = TimeSpan.FromSeconds(30);

        private readonly Stopwatch _recheckStopwatch = new Stopwatch();
        private readonly DirectoryInfo _storeDirectoryInfo;

        private SafeX509StackHandle? _nativeCollection;
        private DateTime _loadLastWrite;
        private bool _forceRefresh;

        internal OpenSslCachedDirectoryStoreProvider(string storeName)
        {
            string storePath = OpenSslDirectoryBasedStoreProvider.GetStorePath(storeName);
            _storeDirectoryInfo = new DirectoryInfo(storePath);
        }

        internal void DoRefresh()
        {
            _forceRefresh = true;
        }

        internal SafeX509StackHandle GetNativeCollection()
        {
            SafeX509StackHandle? ret = _nativeCollection;

            TimeSpan elapsed = _recheckStopwatch.Elapsed;

            if (ret == null || elapsed >= s_lastWriteRecheckInterval || _forceRefresh)
            {
                lock (_recheckStopwatch)
                {
                    _storeDirectoryInfo.Refresh();
                    DirectoryInfo info = _storeDirectoryInfo;
                    ret = _nativeCollection;
                    elapsed = _recheckStopwatch.Elapsed;

                    if (ret == null ||
                        _forceRefresh ||
                        elapsed >= s_assumeInvalidInterval ||
                        (info.Exists && info.LastWriteTimeUtc != _loadLastWrite))
                    {
                        SafeX509StackHandle newColl = Interop.Crypto.NewX509Stack();
                        Interop.Crypto.CheckValidOpenSslHandle(newColl);

                        if (info.Exists)
                        {
                            AddDirectoryStore(newColl, info.FullName);
                            _loadLastWrite = info.LastWriteTimeUtc;
                        }


                        // The existing collection is not Disposed here, intentionally.
                        // It could be in the gap between when they are returned from this method and
                        // not yet used in a P/Invoke, which would result in an exception being thrown.
                        // In order to maintain "finalization-free" this method would need to always
                        // DangerousAddRef, and the callers would need to DangerousRelease,
                        // adding more interlocked operations on every call.
                        ret = newColl;
                        _nativeCollection = newColl;
                        _recheckStopwatch.Restart();
                        _forceRefresh = false;
                    }
                }
            }

            Debug.Assert(ret != null);
            return ret;
        }

        // NetWasm: native code cannot read directories, so the store's files are read here, as
        // CryptoNative_X509StackAddDirectoryStore reads them on Linux: each *.pfx file's certificate (a PKCS#12 file
        // without a password), public part only; a file that does not load is skipped.
        private static void AddDirectoryStore(SafeX509StackHandle stack, string path)
        {
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(path, "*.pfx", new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive });
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return;
            }

            foreach (string file in files)
            {
                X509Certificate2 certificate;
                try
                {
                    certificate = X509CertificateLoader.LoadPkcs12FromFile(file, null);
                }
                catch (Exception exception) when (exception is CryptographicException or IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                using (certificate)
                using (SafeX509Handle publicCertificate = Interop.Crypto.X509UpRef(certificate.Handle))
                {
                    if (!Interop.Crypto.PushX509StackField(stack, publicCertificate))
                    {
                        throw Interop.Crypto.CreateOpenSslCryptographicException();
                    }

                    // Ownership was transferred to the stack.
                    publicCertificate.SetHandleAsInvalid();
                }
            }
        }
    }
}
