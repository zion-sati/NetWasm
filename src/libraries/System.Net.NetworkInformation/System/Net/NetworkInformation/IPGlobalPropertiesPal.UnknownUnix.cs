// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// NetWasm uses upstream's unknown-Unix PAL: there is no host interface or statistics source,
// so GetIPGlobalProperties throws PlatformNotSupportedException.

using System.Runtime.Versioning;

namespace System.Net.NetworkInformation
{
    internal static class IPGlobalPropertiesPal
    {
        [UnsupportedOSPlatform("illumos")]
        [UnsupportedOSPlatform("solaris")]
        [UnsupportedOSPlatform("haiku")]
        public static IPGlobalProperties GetIPGlobalProperties()
        {
            throw new PlatformNotSupportedException();
        }
    }
}
