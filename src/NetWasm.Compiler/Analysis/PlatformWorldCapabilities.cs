using System;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

// What the compile target, the host and the selected WIT world provide to CoreLib's
// composition (System.Runtime.InteropServices.PlatformWorld and NetWasmHost). Every component has an event loop: the
// Component Model's waitable sets wait for its subtasks, streams and futures. A delay also needs
// the monotonic clock's wait-for; the world declaring the clock is enough, since CoreLib calls
// wait-for as a Component Model built-in that wit-component resolves against the world, whether
// or not any reachable code binds the clock's now. A core module has neither. A synchronous
// export (an async func's export is lifted with a callback and is not in the contract's
// exports) is where a blocking wait must throw; a program without one leaves that check out.
public sealed record PlatformWorldCapabilities(bool EventLoop, bool MonotonicClock, bool SynchronousExports)
{
    internal const string MonotonicClockInterface = "wasi:clocks/monotonic-clock@0.3.2";

    internal const string ExitInterface = "wasi:cli/exit@0.3.2";

    // The compiled target's native integer and pointer size: IntPtr.Size, UIntPtr.Size and sizeof(nint).
    public required int NativeIntegerSize { get; init; }

    // Whether a JavaScript host runs the program (a raw build, or one with the JavaScript export
    // boundary): NetWasmHost.IsJavaScriptHost, behind OperatingSystem.IsBrowser and IsWasi.
    public required bool JavaScriptHost { get; init; }

    // A module a JavaScript host runs (a raw build): the host runs its event loop and can
    // complete a task through a JavaScript promise the program awaits.
    public bool HostEventLoop { get; init; }

    // Whether the program has WASI, so that NetWasm's WASI 0.3 platform services are the
    // defaults; a plain core module has none.
    public bool Wasi { get; init; } = true;

    // Whether the selected world imports wasi:cli/exit, whose exit-with-code ends a process with
    // a non-zero code; without it an int Main's result goes to wasi:cli/run's result.
    public bool ProcessExit { get; init; }

    // The program's declared platform (NetWasmPlatformAttribute), or null for the default one.
    public PlatformServices.DeclaredPlatform? DeclaredPlatform { get; init; }

    // The project's feature switches the compiler folds (Trim="true" RuntimeHostConfigurationOption items with a
    // Boolean value): calls to their [FeatureSwitchDefinition] getters become these values.
    public System.Collections.Immutable.ImmutableDictionary<string, bool> FeatureSwitches { get; init; } =
        System.Collections.Immutable.ImmutableDictionary<string, bool>.Empty;

    // The project's RuntimeHostConfigurationOption items, which AppContext reads as .NET reads runtimeconfig.json's
    // properties.
    public System.Collections.Immutable.ImmutableArray<(string Name, string Value)> RuntimeProperties { get; init; } = [];

    public static PlatformWorldCapabilities From(
        ComponentBoundaryContract contract, int nativeIntegerSize, bool javaScriptHost)
    {
        ArgumentNullException.ThrowIfNull(contract);
        if (nativeIntegerSize is not (4 or 8)) throw new ArgumentOutOfRangeException(nameof(nativeIntegerSize));
        var component = contract.World.Length != 0;
        return new(
            component,
            component && Declares(contract, MonotonicClockInterface, "now"),
            contract.Exports.Any(export => export.Kind is
                CanonicalAbiFunctionKind.Function or CanonicalAbiFunctionKind.ExportedResourceDestructor))
        {
            NativeIntegerSize = nativeIntegerSize,
            JavaScriptHost = javaScriptHost,
            ProcessExit = component && Declares(contract, ExitInterface, "exit-with-code"),
        };
    }

    private static bool Declares(ComponentBoundaryContract contract, string interfaceName, string function) =>
        contract.Imports.Any(import => import.Identity == new WitFunctionIdentity(interfaceName, function));
}
