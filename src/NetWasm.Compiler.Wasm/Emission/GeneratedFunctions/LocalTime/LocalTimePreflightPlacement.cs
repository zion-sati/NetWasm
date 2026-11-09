namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions.LocalTime;

// The local-time preflight may wait on wasi:filesystem, which only an
// async-typed call may do. A process entry (wasi:cli/run) runs it; any other
// module runs it when the component initializes.
internal static class LocalTimePreflightPlacement
{
    public static bool RunsInProcessEntry(WasmEmissionRequest request) =>
        request.EntryPoint is not null && request.EntryPointProfile == WasmEntryPointProfile.Process;
}
