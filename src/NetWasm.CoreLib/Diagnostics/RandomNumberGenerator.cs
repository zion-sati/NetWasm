// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Diagnostics
{
    /// <summary>
    /// Supplies Activity identifiers from the program's secure random source.
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
            ulong value;
            Runtime.InteropServices.PlatformServices.Random.Fill(new Span<byte>(&value, sizeof(ulong)));
            return (long)value;
        }
    }
}
