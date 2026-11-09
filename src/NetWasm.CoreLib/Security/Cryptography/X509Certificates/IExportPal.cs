// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime 474ed963699fc29809015392738a15231aaae2de.
// NetWasm adaptation: the ExportPkcs12 members are not ported.

using Microsoft.Win32.SafeHandles;

namespace System.Security.Cryptography.X509Certificates
{
    internal interface IExportPal : IDisposable
    {
        byte[]? Export(X509ContentType contentType, SafePasswordHandle password);
    }
}
