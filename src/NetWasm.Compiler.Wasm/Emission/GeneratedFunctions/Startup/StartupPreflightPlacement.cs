// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions.Startup;

// The startup preflights (symbols sidecars, local time) may wait on
// wasi:filesystem, which only an async-typed call may do. A process entry
// (wasi:cli/run) runs them; any other module runs them when the component
// initializes.
internal static class StartupPreflightPlacement
{
    public static bool RunsInProcessEntry(WasmEmissionRequest request) =>
        request.EntryPoint is not null && request.EntryPointProfile == WasmEntryPointProfile.Process;
}
