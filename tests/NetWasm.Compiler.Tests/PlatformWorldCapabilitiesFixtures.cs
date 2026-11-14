namespace NetWasm.Compiler.Analysis;

// A world without the event loop, for analyses a test runs without a component contract, and
// worlds a test describes by their three capabilities; both compile wasm32 without a JavaScript host.
internal static class PlatformWorldCapabilitiesFixtures
{
    private static readonly PlatformWorldCapabilities NoCapabilities = PlatformWorldCapabilities.Of(false, false, false);

    extension(PlatformWorldCapabilities)
    {
        public static PlatformWorldCapabilities None => NoCapabilities;

        public static PlatformWorldCapabilities Of(bool eventLoop, bool monotonicClock, bool synchronousExports) =>
            new(eventLoop, monotonicClock, synchronousExports) { NativeIntegerSize = 4, JavaScriptHost = false };
    }
}
