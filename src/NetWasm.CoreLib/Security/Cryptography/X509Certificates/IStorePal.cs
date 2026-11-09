// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime 474ed963699fc29809015392738a15231aaae2de.

using System.Runtime.InteropServices;

namespace System.Security.Cryptography.X509Certificates
{
    internal interface IStorePal : IDisposable
    {
        void CloneTo(X509Certificate2Collection collection);
        void Add(ICertificatePal cert);
        void Remove(ICertificatePal cert);
        SafeHandle? SafeHandle { get; }
    }
}
