// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

namespace System.Net.Security
{
    [Flags]
    public enum SslPolicyErrors
    {
        None = 0x0,
        RemoteCertificateNotAvailable = 0x1,
        RemoteCertificateNameMismatch = 0x2,
        RemoteCertificateChainErrors = 0x4
    }
}
