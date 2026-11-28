// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

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
    bool IncludeUnhandledExceptionHint = false,
    bool IncludeObjectClone = false,
    bool IncludeUninitializedArrays = false,
    // The program's collector has generations and is told of each reference stored into memory.
    bool IncludeReferenceWriteBarrier = false,
    // The program asks an object's generation (GC.GetGeneration).
    bool IncludeObjectGeneration = false,
    // The program's event loop asks for idle and quiet collections.
    bool IncludeEventLoopCollection = false,
    // The program collects its collector's youngest generation alone (GC.Collect(0)).
    bool IncludeYoungCollection = false,
    // The program allocates shared arrays (SharedArrayShapes), so its casts, copies and stores use the runtime's
    // forms that read a shared array's element.
    bool IncludeSharedArrays = false)
{
    // The runtime's cast of an object: the form that also reads a shared array's element where the program has them.
    public RuntimeImportSymbol IsAssignable =>
        IncludeSharedArrays ? RuntimeImportSymbol.IsAssignableShared : RuntimeImportSymbol.IsAssignable;

    public RuntimeImportSymbol ArrayCopy =>
        IncludeSharedArrays ? RuntimeImportSymbol.ArrayCopyShared : RuntimeImportSymbol.ArrayCopy;

    public RuntimeImportSymbol ArraySetValue =>
        IncludeSharedArrays ? RuntimeImportSymbol.ArraySetValueShared : RuntimeImportSymbol.ArraySetValue;

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
        (IncludeObjectClone || symbol != RuntimeImportSymbol.ObjectClone) &&
        (IncludeUninitializedArrays || symbol != RuntimeImportSymbol.AllocateUninitializedValueArray) &&
        (IncludeReferenceWriteBarrier || symbol is not
            (RuntimeImportSymbol.RecordReferenceWrite or RuntimeImportSymbol.RecordReferenceWrites)) &&
        (IncludeObjectGeneration || symbol != RuntimeImportSymbol.GcObjectGeneration) &&
        (IncludeYoungCollection || symbol != RuntimeImportSymbol.GcCollectYoung) &&
        (IncludeSharedArrays || symbol is not
            (RuntimeImportSymbol.RegisterSharedArrays or RuntimeImportSymbol.IsAssignableShared or
             RuntimeImportSymbol.ArrayCopyShared)) &&
        (IncludeSharedArrays && IncludeArraySetValue || symbol != RuntimeImportSymbol.ArraySetValueShared) &&
        (IncludeEventLoopCollection || symbol is not
            (RuntimeImportSymbol.CollectWhenIdle or RuntimeImportSymbol.CollectWhenQuiet)) &&
        (IncludeUnhandledExceptionHint || symbol != RuntimeImportSymbol.ReportUnhandledException) &&
        (IncludeTerminalExceptionRaise ||
            symbol != RuntimeImportSymbol.ManagedTerminalExceptionRaise) &&
        (ModuleProfile == WasmModuleProfile.ComponentCoreModule
            ? symbol != RuntimeImportSymbol.ManagedTerminalExceptionReport ||
              IncludeTerminalExceptionReporter
            : symbol is not RuntimeImportSymbol.ComponentReallocate
                and not RuntimeImportSymbol.ComponentFree);
}
