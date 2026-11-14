// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// NetWasm: OpenSSL is linked statically, so it is always available, as CryptoNative_OpenSslAvailable answers in
// upstream's static build. Answering here keeps the IsSupported checks from linking OpenSSL.

internal static partial class Interop
{
    internal static partial class OpenSslNoInit
    {
        internal static bool OpenSslIsAvailable => true;
    }
}
