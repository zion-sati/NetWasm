// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// NetWasm uses upstream's unknown-Unix NetworkChange: there are no host change
// notifications, so IsSupported is false and the events throw PlatformNotSupportedException.

using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.Versioning;
using System.Threading;

namespace System.Net.NetworkInformation
{
    public partial class NetworkChange
    {
        /// <summary>Gets a value that indicates whether network change notifications are supported on the current platform.</summary>
        /// <value><see langword="true" /> if network change notifications are supported on the current platform; otherwise, <see langword="false" />.</value>
        /// <remarks>A value of <see langword="false" /> indicates that the <see cref="NetworkAddressChanged" /> and <see cref="NetworkAvailabilityChanged" /> events will throw <see cref="PlatformNotSupportedException" />.</remarks>
        public static bool IsSupported => false;

        [EditorBrowsableAttribute(EditorBrowsableState.Never)]
        [Obsolete("This API supports the .NET Framework infrastructure and is not intended to be used directly from your code.", true)]
        public NetworkChange()
        {
        }

        [UnsupportedOSPlatform("illumos")]
        [UnsupportedOSPlatform("solaris")]
        [UnsupportedOSPlatform("haiku")]
        public static event NetworkAddressChangedEventHandler? NetworkAddressChanged
        {
            add { throw new PlatformNotSupportedException(); }
            remove { throw new PlatformNotSupportedException(); }
        }

        [UnsupportedOSPlatform("illumos")]
        [UnsupportedOSPlatform("solaris")]
        [UnsupportedOSPlatform("haiku")]
        public static event NetworkAvailabilityChangedEventHandler? NetworkAvailabilityChanged
        {
            add { throw new PlatformNotSupportedException(); }
            remove { throw new PlatformNotSupportedException(); }
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("This API supports the .NET Framework infrastructure and is not intended to be used directly from your code.", true)]
        public static void RegisterNetworkChange(NetworkChange nc) { }
    }
}
