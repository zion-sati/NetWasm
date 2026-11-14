// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// NetWasm: dotnet/runtime deae2e32's (.NET 11.0.0) initialization, which first gives the shim the platform's random
// source, for OpenSSL's seed source, and the managed clock, for OpenSSL's time.

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

internal static partial class Interop
{
    // Initialization of libcrypto threading support is done in a static constructor.
    // This enables a project simply to include this file, and any usage of any of
    // the System.Security.Cryptography.Native functions will trigger
    // initialization of the threading support.

    internal static partial class Crypto
    {
        static Crypto()
        {
            CryptoInitializer.Initialize();
        }
    }

    internal static partial class OpenSsl
    {
        static OpenSsl()
        {
            CryptoInitializer.Initialize();
        }
    }

    internal static partial class CryptoInitializer
    {
        static unsafe CryptoInitializer()
        {
            // NetWasm: OpenSSL's seed source draws from the platform's random source, and its clock reads the managed
            // clock, through these callbacks (pal_netwasm_seed.c, pal_netwasm_clock.c), registered before OpenSSL
            // creates its generators.
            SetRandomSource(&GetRandomBytes);
            SetClock(&GetTime);

            if (EnsureOpenSslInitialized() != 0)
            {
                // Ideally this would be a CryptographicException, but we use
                // OpenSSL in libraries lower than System.Security.Cryptography.
                // It's not a big deal, though: this will already be wrapped in a
                // TypeLoadException, and this failing means something is very
                // wrong with the system's configuration and any code using
                // these libraries will be unable to operate correctly.
                throw new InvalidOperationException();
            }
        }

        internal static void Initialize()
        {
            // No-op that exists to provide a hook for other static constructors.
        }

        [LibraryImport(Libraries.CryptoNative, EntryPoint = "CryptoNative_EnsureOpenSslInitialized")]
        private static partial int EnsureOpenSslInitialized();

        // Fills the buffer from RandomNumberGenerator, which the platform's random source governs; 1 on success.
        [UnmanagedCallersOnly]
        private static unsafe int GetRandomBytes(byte* buffer, int length)
        {
            try
            {
                System.Security.Cryptography.RandomNumberGenerator.Fill(new Span<byte>(buffer, length));
                return 1;
            }
            catch
            {
                return 0;
            }
        }

        [LibraryImport(Libraries.CryptoNative, EntryPoint = "NetWasmCryptoNative_SetRandomSource")]
        private static unsafe partial void SetRandomSource(delegate* unmanaged<byte*, int, int> source);

        // Kind 0 is the wall clock since the Unix epoch, 1 the monotonic clock; 1 on success.
        [UnmanagedCallersOnly]
        private static unsafe int GetTime(int kind, long* seconds, int* nanoseconds)
        {
            try
            {
                if (kind == 0)
                {
                    long ticks = DateTime.UtcNow.Ticks - DateTime.UnixEpoch.Ticks;
                    *seconds = ticks / TimeSpan.TicksPerSecond;
                    *nanoseconds = (int)(ticks % TimeSpan.TicksPerSecond * TimeSpan.NanosecondsPerTick);
                }
                else
                {
                    long timestamp = Stopwatch.GetTimestamp();
                    *seconds = timestamp / Stopwatch.Frequency;
                    *nanoseconds = (int)(timestamp % Stopwatch.Frequency * 1_000_000_000 / Stopwatch.Frequency);
                }

                return 1;
            }
            catch
            {
                return 0;
            }
        }

        [LibraryImport(Libraries.CryptoNative, EntryPoint = "NetWasmCryptoNative_SetClock")]
        private static unsafe partial void SetClock(delegate* unmanaged<int, long*, int*, int> source);
    }
}
