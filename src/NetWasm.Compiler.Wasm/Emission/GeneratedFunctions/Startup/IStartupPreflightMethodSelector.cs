using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions.Startup;

// The preflights to call, in order, and whether local time is among them (the program then declares the
// local-time runtime feature, which ships time zone data).
internal sealed record StartupPreflights(ImmutableArray<EntityKey> Methods, bool UsesLocalTime)
{
    public static StartupPreflights None { get; } = new([], false);
}

internal interface IStartupPreflightMethodSelector
{
    StartupPreflights Select(WasmEmissionRequest request);
}
