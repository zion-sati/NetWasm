using System;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

// What the selected WIT world provides to CoreLib's world-dependent composition
// (System.Runtime.InteropServices.PlatformWorld): a host-driven reactor, or the wasi:io/poll and
// monotonic-clock imports a program needs to run its own event loop.
public sealed record PlatformWorldCapabilities(bool ReactorHost, bool EventLoop)
{
    internal const string ReactorHostInterface = "netwasm:runtime/reactor-host@1.0.0";
    internal const string PollInterface = "wasi:io/poll@0.2.11";
    internal const string MonotonicClockInterface = "wasi:clocks/monotonic-clock@0.2.11";

    public static PlatformWorldCapabilities None { get; } = new(ReactorHost: false, EventLoop: false);

    public static PlatformWorldCapabilities From(ComponentBoundaryContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        return new(
            Declares(contract, ReactorHostInterface, "watch") &&
                Declares(contract, ReactorHostInterface, "cancel"),
            Declares(contract, PollInterface, "poll") &&
                Declares(contract, MonotonicClockInterface, "subscribe-duration"));
    }

    private static bool Declares(ComponentBoundaryContract contract, string interfaceName, string function) =>
        contract.Imports.Any(import =>
            import.HasManagedBinding &&
            import.Identity == new WitFunctionIdentity(interfaceName, function));
}
