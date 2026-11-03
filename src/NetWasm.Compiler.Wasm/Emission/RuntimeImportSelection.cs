namespace NetWasm.Compiler.Wasm.Emission;

internal readonly record struct RuntimeImportSelection(
    WasmModuleProfile ModuleProfile,
    bool IncludeTerminalExceptionReporter,
    bool IncludeStackTrace = false,
    bool IncludeExceptionCapture = false,
    bool IncludeEphemeronHandles = false,
    bool IncludeTerminalExceptionRaise = true,
    bool IncludeFinalization = false,
    bool IncludeWeakHandles = false,
    bool IncludeRandomSeed = false,
    bool IncludeArraySetValue = false,
    bool IncludeUnhandledExceptionHint = false)
{
    // Whether a module with this selection imports the runtime function.
    public bool Includes(RuntimeImportSymbol symbol) =>
        (IncludeExceptionCapture || symbol != RuntimeImportSymbol.ManagedExceptionCapture) &&
        (IncludeStackTrace || symbol is not
            (RuntimeImportSymbol.StackTraceFrameEnter or
             RuntimeImportSymbol.StackTraceFrameLocation or
             RuntimeImportSymbol.StackTraceFrameLeave or
             RuntimeImportSymbol.StackTraceInitialize or
             RuntimeImportSymbol.StackTraceRegisterSymbol)) &&
        (IncludeEphemeronHandles || symbol is not
            (RuntimeImportSymbol.EphemeronHandleCreate or
             RuntimeImportSymbol.EphemeronHandleGetKey or
             RuntimeImportSymbol.EphemeronHandleGetValue or
             RuntimeImportSymbol.EphemeronHandleSetTargetToNull or
             RuntimeImportSymbol.EphemeronHandleSetValue or
             RuntimeImportSymbol.EphemeronHandleRelease)) &&
        (IncludeFinalization || symbol is not
            (RuntimeImportSymbol.InitializeFinalization or
             RuntimeImportSymbol.AllocateFinalizable or
             RuntimeImportSymbol.SuppressFinalize or
             RuntimeImportSymbol.ReRegisterForFinalize or
             RuntimeImportSymbol.FinalizerSafepoint or
             RuntimeImportSymbol.GcWaitForPendingFinalizers)) &&
        (IncludeRandomSeed || symbol != RuntimeImportSymbol.RandomSeed) &&
        (IncludeArraySetValue || symbol != RuntimeImportSymbol.ArraySetValue) &&
        (IncludeUnhandledExceptionHint || symbol != RuntimeImportSymbol.ReportUnhandledException) &&
        (IncludeTerminalExceptionRaise ||
            symbol != RuntimeImportSymbol.ManagedTerminalExceptionRaise) &&
        (ModuleProfile == WasmModuleProfile.ComponentCoreModule
            ? symbol != RuntimeImportSymbol.ManagedTerminalExceptionReport ||
              IncludeTerminalExceptionReporter
            : symbol is not RuntimeImportSymbol.ComponentReallocate
                and not RuntimeImportSymbol.ComponentFree);
}
