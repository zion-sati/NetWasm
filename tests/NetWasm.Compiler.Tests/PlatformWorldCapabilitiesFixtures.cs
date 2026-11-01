namespace NetWasm.Compiler.Analysis;

// A world that grants neither the reactor host nor the event loop, for analyses
// a test runs without a component contract.
internal static class PlatformWorldCapabilitiesFixtures
{
    private static readonly PlatformWorldCapabilities NoCapabilities = new(ReactorHost: false, EventLoop: false);

    extension(PlatformWorldCapabilities)
    {
        public static PlatformWorldCapabilities None => NoCapabilities;
    }
}
