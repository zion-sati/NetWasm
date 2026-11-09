namespace System.Security.Cryptography;

internal sealed partial class RandomNumberGeneratorImplementation
{
    // The upstream platform hook reads the program's secure random source (its
    // IRandomSource binding); this API adds no fallback random source.
    private static unsafe void GetBytes(byte* destination, int length) =>
        System.Runtime.InteropServices.PlatformServices.Random.Fill(new Span<byte>(destination, length));
}
