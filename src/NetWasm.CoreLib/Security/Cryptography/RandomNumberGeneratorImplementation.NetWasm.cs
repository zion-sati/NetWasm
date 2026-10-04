using System.Runtime.InteropServices.Cryptography;

namespace System.Security.Cryptography;

internal sealed partial class RandomNumberGeneratorImplementation
{
    // The upstream platform hook delegates to NetWasm's existing WASI secure
    // entropy service. That service retains canonical memory ownership and the
    // host's capability boundary; this API adds no fallback random source.
    private static unsafe void GetBytes(byte* destination, int length) =>
        CryptographicRandomServices.Filler.Fill((nuint)destination, length);
}
