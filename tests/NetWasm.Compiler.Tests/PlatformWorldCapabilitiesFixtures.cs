namespace NetWasm.Compiler.Analysis;

// A world without the event loop, for analyses
// a test runs without a component contract.
internal static class PlatformWorldCapabilitiesFixtures
{
    private static readonly PlatformWorldCapabilities NoCapabilities = new(EventLoop: false, MonotonicClock: false, SynchronousExports: false);

    extension(PlatformWorldCapabilities)
    {
        public static PlatformWorldCapabilities None => NoCapabilities;
    }
}
