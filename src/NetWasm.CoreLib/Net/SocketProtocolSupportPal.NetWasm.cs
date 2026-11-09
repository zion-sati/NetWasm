// NetWasm's counterpart of dotnet/runtime 474ed963699fc29809015392738a15231aaae2de's
// SocketProtocolSupportPal.Unix.cs and the DisableIPv6 switch of LocalAppContextSwitches.Net.cs.
// The platform network carries IPv4 and IPv6 sockets and no Unix domain sockets; asking it by
// opening a socket, as Unix does, would fail on a platform without a network.

using System.Net.Sockets;

namespace System.Net
{
    internal static partial class SocketProtocolSupportPal
    {
        private static bool IsSupported(AddressFamily af) =>
            af is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6;
    }
}

namespace System
{
    internal static partial class LocalAppContextSwitches
    {
        internal static bool DisableIPv6 =>
            AppContext.TryGetSwitch("System.Net.DisableIPv6", out bool disabled)
                ? disabled
                : Environment.GetEnvironmentVariable("DOTNET_SYSTEM_NET_DISABLEIPV6") is { } value &&
                  (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase));
    }
}
