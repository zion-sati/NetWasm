namespace NetWasm.Compiler.Wasm.Emission;

internal readonly record struct RuntimeImportSelection(
    WasmModuleProfile ModuleProfile,
    bool IncludeTerminalExceptionReporter,
    bool IncludeStackTrace = false);
