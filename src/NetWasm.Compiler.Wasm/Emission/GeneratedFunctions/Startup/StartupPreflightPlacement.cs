// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions.Startup;

// The startup preflights (symbols sidecars, local time) may wait on
// wasi:filesystem, which only an async-typed call may do. A process entry
// (wasi:cli/run) runs them; any other module runs none, and reads local time
// and symbols at first use, as a raw module does: a component's instantiation
// may never wait.
internal static class StartupPreflightPlacement
{
    public static bool RunsInProcessEntry(WasmEmissionRequest request) =>
        request.EntryPoint is not null && request.EntryPointProfile == WasmEntryPointProfile.Process;

    // Whether the program may wait to load its symbols files: a module with synchronous WIT exports, whose tasks may
    // never wait, reads type and method IDs instead. A JavaScript host's raw module still loads them, as its host
    // completes the read or answers that it cannot.
    public static bool LoadsSymbols(WasmEmissionRequest request) =>
        RunsInProcessEntry(request) || request.UseJavaScriptExportBoundary ||
        !request.ComponentContract.Exports.Any(static export =>
            export.Kind is CanonicalAbiFunctionKind.Function or CanonicalAbiFunctionKind.ExportedResourceDestructor);
}
