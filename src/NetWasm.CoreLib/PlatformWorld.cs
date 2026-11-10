namespace System.Runtime.InteropServices
{
    using CompilerServices;

    // World-dependent composition switches. The NetWasm compiler replaces every call to these
    // members with the value for the selected WIT world before reachability, in every build
    // configuration, so a program reaches only the services its world can bind. The bodies are
    // the values for code compiled without that rewrite, such as CoreLib's desktop actor tests.
    internal static class PlatformWorld
    {
        // True unless the program is a plain core module, which has no WASI: its platform services
        // are the program's own bindings or throw PlatformNotSupportedException.
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool ProvidesWasi() => true;

        // True for a component: the Component Model's waitable sets run its event loop.
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool ImportsEventLoop() => true;

        // True when the selected world imports wasi:clocks/monotonic-clock, whose wait-for a
        // delay needs.
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool ImportsMonotonicClock() => true;

        // True when the selected world imports wasi:cli/exit, whose exit-with-code ends the process
        // with a code that wasi:cli/run's result cannot carry.
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool ImportsProcessExit() => true;

        // True when the program has a synchronous WIT export, inside which a blocking wait
        // throws.
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool HasSynchronousExports() => true;

        // True for a module a JavaScript host runs: the host runs its event loop and can complete
        // a task through a JavaScript promise the program awaits.
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool HasHostEventLoop() => true;
    }
}
