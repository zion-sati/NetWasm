// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

namespace System.Net.NetworkInformation
{
    /// <summary>
    /// Specified the list of Networking components that are supported on a Network Interface
    /// </summary>
    public enum NetworkInterfaceComponent
    {
        /// <summary>
        /// Internet Protocol version 4 is supported.
        /// </summary>
        IPv4,

        /// <summary>
        /// Internet Protocol version 6 is supported.
        /// </summary>
        IPv6
    }
}
