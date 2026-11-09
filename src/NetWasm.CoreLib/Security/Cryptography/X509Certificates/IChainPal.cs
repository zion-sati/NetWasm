// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime 474ed963699fc29809015392738a15231aaae2de.

using Microsoft.Win32.SafeHandles;

namespace System.Security.Cryptography.X509Certificates
{
    internal interface IChainPal : IDisposable
    {
        /// <summary>
        /// Does not throw on api error. Returns default(bool?) and sets "exception" instead.
        /// </summary>
        bool? Verify(X509VerificationFlags flags, out Exception? exception);

        X509ChainElement[]? ChainElements { get; }
        X509ChainStatus[]? ChainStatus { get; }
        SafeX509ChainHandle? SafeHandle { get; }
    }
}
