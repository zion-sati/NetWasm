// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Net
{
    /// <summary>
    /// Provides the credential lookup contract consumed by resolver APIs.
    /// </summary>
    /// <remarks>
    /// The NetWasm profile treats credentials as inert data. Implementations do
    /// not grant a transport, proxy, filesystem, or ambient host capability.
    /// </remarks>
    public interface ICredentials
    {
        NetworkCredential? GetCredential(Uri uri, string authType);
    }
}
