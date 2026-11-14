// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

using System.Runtime.InteropServices;

namespace System.Security.Cryptography
{
    internal struct PinAndClear : IDisposable
    {
        private byte[] _data;
        private PinnedGCHandle<byte[]> _gcHandle;

        internal static PinAndClear Track(byte[] data)
        {
            return new PinAndClear
            {
                _gcHandle = new PinnedGCHandle<byte[]>(data),
                _data = data,
            };
        }

        public void Dispose()
        {
            Array.Clear(_data);
            _gcHandle.Dispose();
        }
    }
}
