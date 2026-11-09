namespace System.Runtime.InteropServices.Network;

using System.Runtime.InteropServices.Concurrency;

// Composes WasiPlatform's network over the shared waits and canonical buffers.
internal static class WasiNetworkComposition
{
    internal static WasiNetwork Create()
    {
        var awaiter = WasiPlatformServicesComposition.CreateWaitableEventAwaiter();
        return new WasiNetwork(
            new SubtaskReturnAwaiter(awaiter, new WasiSubtaskDropInvoker()),
            awaiter,
            new CanonicalBufferAllocator());
    }
}
