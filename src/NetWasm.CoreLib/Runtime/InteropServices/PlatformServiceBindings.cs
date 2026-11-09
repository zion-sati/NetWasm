namespace System.Runtime.InteropServices
{
    using System.Runtime.InteropServices.WebAssembly;

    // Where CoreLib creates the program's platform. The NetWasm compiler replaces a call to
    // CreatePlatform with the constructor of the platform the program declares
    // (NetWasmPlatformAttribute), and a call to DescribeDeclaredPlatform with that platform's name
    // and declaring assembly. Otherwise the bodies are the defaults: WasiPlatform where the world
    // provides WASI, one that provides nothing where it does not (a plain core module), and no
    // description.
    internal static class PlatformServiceBindings
    {
        internal static NetWasmPlatform CreatePlatform() =>
            PlatformWorld.ProvidesWasi() ? new WasiPlatform() : new UnsupportedPlatform();

        internal static string? DescribeDeclaredPlatform() => null;
    }

    internal sealed class UnsupportedPlatform : NetWasmPlatform
    {
    }
}
