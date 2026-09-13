// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices.Cryptography;

namespace System.Diagnostics
{
    /// <summary>
    /// Supplies Activity identifiers from the already-qualified WASI secure entropy capability.
    /// </summary>
    internal sealed class RandomNumberGenerator
    {
        private static readonly RandomNumberGenerator s_current = new();

        public static RandomNumberGenerator Current => s_current;

        private RandomNumberGenerator()
        {
        }

        public unsafe long Next()
        {
            byte[] bytes = new byte[sizeof(ulong)];
            fixed (byte* destination = bytes)
            {
                CryptographicRandomServices.Filler.Fill((nuint)destination, bytes.Length);
                return (long)(*(ulong*)destination);
            }
        }
    }
}
