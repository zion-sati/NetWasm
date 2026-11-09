// NetWasm's counterpart of dotnet/runtime 474ed963699fc29809015392738a15231aaae2de's
// NameResolutionPal.Unix.cs: a name resolves through the platform network
// (PlatformServices.Network), asynchronously when awaited. The network resolves names to addresses
// and does no reverse lookup, so an address's name is its text (getnameinfo's numeric host), and
// there is no host name to read, so the host name is "localhost", as WASI's libc reports.

using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using PlatformServices = System.Runtime.InteropServices.PlatformServices;

namespace System.Net
{
    internal static partial class NameResolutionPal
    {
        public const bool SupportsGetAddrInfoAsync = true;

        public static bool SupportsGetNameInfo => false;

        internal static Task? GetAddrInfoAsync(string hostName, bool justAddresses, AddressFamily family, CancellationToken cancellationToken) =>
            justAddresses
                ? ResolveAsync(hostName, family, cancellationToken)
                : ResolveEntryAsync(hostName, family, cancellationToken);

        public static SocketError TryGetAddrInfo(string name, bool justAddresses, AddressFamily addressFamily, out string? hostName,
            out string[] aliases, out IPAddress[] addresses, out int nativeErrorCode)
        {
            var error = PlatformServices.Network.ResolveAddresses(name, out var resolved);
            (hostName, aliases, nativeErrorCode) = (name, [], 0);
            return Filtered(error, resolved, addressFamily, out addresses);
        }

        public static string? TryGetNameInfo(IPAddress addr, out SocketError socketError, out int nativeErrorCode)
        {
            (socketError, nativeErrorCode) = (SocketError.Success, 0);
            return addr.ToString();
        }

        public static string GetHostName() => "localhost";

        private static async Task<IPAddress[]> ResolveAsync(string hostName, AddressFamily family, CancellationToken cancellationToken)
        {
            var result = await PlatformServices.Network.ResolveAddressesAsync(hostName, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var error = Filtered(result.Error, result.Addresses, family, out var addresses);
            return error == SocketError.Success ? addresses : throw new SocketException((int)error);
        }

        private static async Task<IPHostEntry> ResolveEntryAsync(string hostName, AddressFamily family, CancellationToken cancellationToken) =>
            new IPHostEntry
            {
                HostName = hostName,
                Aliases = [],
                AddressList = await ResolveAsync(hostName, family, cancellationToken).ConfigureAwait(false),
            };

        // The addresses of the family (both for Unspecified); none is an unknown host.
        private static SocketError Filtered(SocketError error, IPAddress[] resolved, AddressFamily family, out IPAddress[] addresses)
        {
            addresses = [];
            if (error != SocketError.Success)
            {
                return error;
            }

            addresses = family == AddressFamily.Unspecified ? resolved : Array.FindAll(resolved, address => address.AddressFamily == family);
            return addresses.Length == 0 ? SocketError.HostNotFound : SocketError.Success;
        }
    }
}
