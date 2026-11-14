// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// NetWasm uses upstream's unknown-Unix PAL: there is no host interface discovery, so the
// static NetworkInterface queries throw PlatformNotSupportedException.

using System.Runtime.Versioning;

namespace System.Net.NetworkInformation
{
    internal static class NetworkInterfacePal
    {
        /// Returns objects that describe the network interfaces on the local computer.
        [UnsupportedOSPlatform("illumos")]
        [UnsupportedOSPlatform("solaris")]
        [UnsupportedOSPlatform("haiku")]
        public static NetworkInterface[] GetAllNetworkInterfaces()
        {
            throw new PlatformNotSupportedException();
        }

        [UnsupportedOSPlatform("illumos")]
        [UnsupportedOSPlatform("solaris")]
        [UnsupportedOSPlatform("haiku")]
        public static bool GetIsNetworkAvailable()
        {
            throw new PlatformNotSupportedException();
        }

        [UnsupportedOSPlatform("illumos")]
        [UnsupportedOSPlatform("solaris")]
        [UnsupportedOSPlatform("haiku")]
        public static int IPv6LoopbackInterfaceIndex
        {
            get { throw new PlatformNotSupportedException(); }
        }

        [UnsupportedOSPlatform("illumos")]
        [UnsupportedOSPlatform("solaris")]
        [UnsupportedOSPlatform("haiku")]
        public static int LoopbackInterfaceIndex
        {
            get { throw new PlatformNotSupportedException(); }
        }
    }
}
