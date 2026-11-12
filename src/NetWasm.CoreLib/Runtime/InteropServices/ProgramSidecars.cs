namespace System.Runtime.InteropServices
{
    using System.Runtime.CompilerServices;
    using System.Runtime.InteropServices.Sidecars;

    // NetWasm: a program's sidecars, files beside it that its host mounts under /netwasm-sidecars by their names
    // (issue 225): type-symbols, method-symbols and time zone data.
    internal static unsafe class ProgramSidecars
    {
        // The runtime initializer stores the compiler's sidecar descriptor here; zero when nothing that reads it
        // is in the program. No initializer: a static constructor would run on first use and clear it.
#pragma warning disable CS0649
        internal static nint s_descriptor;
#pragma warning restore CS0649

        internal static SidecarDescriptor? Descriptor =>
            s_descriptor == 0 ? null : SidecarDescriptor.Read((byte*)s_descriptor);

        // The name of this program's sidecar with that suffix (App.wasm.tz-info); none without a descriptor.
        internal static string? FileName(string suffix) =>
            Descriptor is { } descriptor ? descriptor.ProgramFileName + suffix : null;

        // The startup preflights the compiler places, each with its own switch so a program pays only for the
        // files it loads: type-symbols with NetWasmTypeNames, method-symbols with NetWasmManagedStackTrace. Neither
        // fails the program. Reading a file waits on wasi:filesystem through the Component Model's event loop, so
        // only a program whose world has one loads them; the host of any other registers method symbols with the
        // runtime itself.
        internal static void LoadTypeSymbols()
        {
            if (!PlatformWorld.ProvidesWasi() || !PlatformWorld.ImportsEventLoop())
            {
                return;
            }
            if (Descriptor is { LoadsTypeSymbols: true } descriptor)
            {
                WasiPlatformServicesComposition.CreateTypeSymbolLoader().Load(descriptor);
            }
        }

        internal static void LoadMethodSymbols()
        {
            if (!PlatformWorld.ProvidesWasi() || !PlatformWorld.ImportsEventLoop())
            {
                return;
            }
            if (Descriptor is { LoadsMethodSymbols: true } descriptor)
            {
                WasiPlatformServicesComposition.CreateMethodSymbolLoader().Load(descriptor);
            }
        }

        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static extern void RegisterMethodSymbol(int id, char* characters, int length);
    }
}
