using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission;

internal static class WasmRuntimeImports
{
    public static RuntimeImportCatalog CreateCatalog() => new(CreateBindings());

    public static WasmFunctionImport[] CreateHostInteropStringResultImports() =>
    [
        new WasmFunctionImport(
            RuntimeAbi.HostModule,
            RuntimeAbi.HostInteropStringLength,
            WasmFunctionType.Create(CliValueKind.I4, CliValueKind.I4)),
        new WasmFunctionImport(
            RuntimeAbi.HostModule,
            RuntimeAbi.HostInteropCopyStringUtf16,
            WasmFunctionType.Create(
                CliValueKind.I4,
                CliValueKind.I4,
                CliValueKind.ManagedReference)),
    ];

    public static WasmFunctionImport[] CreateHostInteropHandleImports() =>
    [
        new WasmFunctionImport(
            RuntimeAbi.HostModule,
            RuntimeAbi.HostInteropReleaseHandle,
            WasmFunctionType.Create(CliValueKind.Void, CliValueKind.I4)),
    ];

    public static WasmFunctionImport[] CreateHostInteropSubscriptionImports() =>
    [
        new WasmFunctionImport(
            RuntimeAbi.HostModule,
            RuntimeAbi.HostInteropReleaseSubscription,
            WasmFunctionType.Create(CliValueKind.Void, CliValueKind.I4)),
    ];

    public static WasmFunctionImport[] CreateHostInteropByteResultImports() =>
    [
        new WasmFunctionImport(RuntimeAbi.HostModule, RuntimeAbi.HostInteropByteLength,
            WasmFunctionType.Create(CliValueKind.I4, CliValueKind.I4)),
        new WasmFunctionImport(RuntimeAbi.HostModule, RuntimeAbi.HostInteropCopyBytes,
            WasmFunctionType.Create(CliValueKind.I4, CliValueKind.I4,
                CliValueKind.ManagedReference)),
    ];

    public static WasmFunctionImport[] Create() => [.. CreateCatalog().Imports];

    private static RuntimeImportBinding[] CreateBindings() =>
    [
        .. CreateLegacyBindings(),
        new(RuntimeImportSymbol.ManagedTerminalExceptionReport, new WasmFunctionImport(RuntimeAbi.HostModule, RuntimeAbi.RuntimeReportTerminalException, WasmFunctionType.Create(CliValueKind.Void, CliValueKind.I4, CliValueKind.NativeInt, CliValueKind.I4))),
        new(RuntimeImportSymbol.StackTraceFrameEnter, new WasmFunctionImport(
            RuntimeAbi.RuntimeModule,
            RuntimeAbi.RuntimeStackTraceFrameEnter,
            WasmFunctionType.Create(CliValueKind.Void, CliValueKind.I4))),
        new(RuntimeImportSymbol.StackTraceFrameLeave, new WasmFunctionImport(
            RuntimeAbi.RuntimeModule,
            RuntimeAbi.RuntimeStackTraceFrameLeave,
            WasmFunctionType.Create(CliValueKind.Void, CliValueKind.I4))),
        new(RuntimeImportSymbol.StackTraceInitialize, new WasmFunctionImport(
            RuntimeAbi.RuntimeModule,
            RuntimeAbi.RuntimeStackTraceInitialize,
            WasmFunctionType.Create(
                CliValueKind.Void,
                CliValueKind.I4,
                CliValueKind.I4))),
    ];

    private static RuntimeImportBinding[] CreateLegacyBindings() =>
    [
        new(RuntimeImportSymbol.Initialize, new WasmFunctionImport(
            RuntimeAbi.RuntimeModule,
            RuntimeAbi.RuntimeInitialize,
            WasmFunctionType.Create(
                CliValueKind.Void,
                CliValueKind.ManagedAddress,
                CliValueKind.I4,
                CliValueKind.I4))),
        new(RuntimeImportSymbol.Allocate, new WasmFunctionImport(
            RuntimeAbi.RuntimeModule,
            RuntimeAbi.RuntimeAllocate,
            WasmFunctionType.Create(
                CliValueKind.ManagedReference,
                CliValueKind.ManagedAddress,
                CliValueKind.I4))),
        new(RuntimeImportSymbol.Collect, new WasmFunctionImport(
            RuntimeAbi.RuntimeModule,
            RuntimeAbi.RuntimeCollect,
            WasmFunctionType.Create(CliValueKind.Void))),
        new(RuntimeImportSymbol.RegisterType, new WasmFunctionImport(
            RuntimeAbi.RuntimeModule,
            RuntimeAbi.RuntimeRegisterType,
            WasmFunctionType.Create(
                CliValueKind.Void,
                CliValueKind.I4,
                CliValueKind.I4,
                CliValueKind.ManagedAddress,
                CliValueKind.ManagedAddress,
                CliValueKind.I4,
                CliValueKind.ManagedAddress,
                CliValueKind.I4,
                CliValueKind.I4))),
        new(RuntimeImportSymbol.RegisterStaticRoot, new WasmFunctionImport(
            RuntimeAbi.RuntimeModule,
            RuntimeAbi.RuntimeRegisterStaticRoot,
            WasmFunctionType.Create(CliValueKind.Void, CliValueKind.ManagedAddress))),
        new(RuntimeImportSymbol.RootFrameEnter, new WasmFunctionImport(
            RuntimeAbi.RuntimeModule,
            RuntimeAbi.RuntimeRootFrameEnter,
            WasmFunctionType.Create(CliValueKind.ManagedAddress, CliValueKind.I4))),
        new(RuntimeImportSymbol.RootFrameLeave, new WasmFunctionImport(
            RuntimeAbi.RuntimeModule,
            RuntimeAbi.RuntimeRootFrameLeave,
            WasmFunctionType.Create(CliValueKind.Void, CliValueKind.ManagedAddress))),
        new(RuntimeImportSymbol.ValueFrameEnter, new WasmFunctionImport(
            RuntimeAbi.RuntimeModule,
            RuntimeAbi.RuntimeValueFrameEnter,
            WasmFunctionType.Create(
                CliValueKind.ManagedAddress,
                CliValueKind.NativeInt))),
        new(RuntimeImportSymbol.ValueFrameLeave, new WasmFunctionImport(
            RuntimeAbi.RuntimeModule,
            RuntimeAbi.RuntimeValueFrameLeave,
            WasmFunctionType.Create(CliValueKind.Void, CliValueKind.ManagedAddress))),
        new(RuntimeImportSymbol.BeginThrow, new WasmFunctionImport(
            RuntimeAbi.RuntimeModule,
            RuntimeAbi.RuntimeBeginThrow,
            WasmFunctionType.Create(CliValueKind.Void, CliValueKind.ManagedReference))),
        new(RuntimeImportSymbol.BeginRethrow, new WasmFunctionImport(
            RuntimeAbi.RuntimeModule,
            RuntimeAbi.RuntimeBeginRethrow,
            WasmFunctionType.Create(CliValueKind.Void, CliValueKind.ManagedReference))),
        new(RuntimeImportSymbol.AllocateReferenceArray, new WasmFunctionImport(
            RuntimeAbi.RuntimeModule,
            RuntimeAbi.RuntimeAllocateReferenceArray,
            WasmFunctionType.Create(
                CliValueKind.ManagedReference,
                CliValueKind.I4,
                CliValueKind.I4,
                CliValueKind.I4))),
        new(RuntimeImportSymbol.AllocateString, new WasmFunctionImport(
            RuntimeAbi.RuntimeModule,
            RuntimeAbi.RuntimeAllocateString,
            WasmFunctionType.Create(
                CliValueKind.ManagedReference,
                CliValueKind.I4,
                CliValueKind.I4,
                CliValueKind.I4))),
        new(RuntimeImportSymbol.SuppressFinalize, new WasmFunctionImport(
            RuntimeAbi.RuntimeModule,
            RuntimeAbi.RuntimeSuppressFinalize,
            WasmFunctionType.Create(CliValueKind.Void, CliValueKind.ManagedReference))),
        new(RuntimeImportSymbol.ReRegisterForFinalize, new WasmFunctionImport(
            RuntimeAbi.RuntimeModule,
            RuntimeAbi.RuntimeReRegisterForFinalize,
            WasmFunctionType.Create(CliValueKind.Void, CliValueKind.ManagedReference))),
        new(RuntimeImportSymbol.ReportUnobservedTaskException, new WasmFunctionImport(
            RuntimeAbi.RuntimeModule,
            RuntimeAbi.RuntimeReportUnobservedTaskException,
            WasmFunctionType.Create(CliValueKind.Void))),
        new(RuntimeImportSymbol.IsAssignable, new WasmFunctionImport(
            RuntimeAbi.RuntimeModule,
            RuntimeAbi.RuntimeIsAssignable,
            WasmFunctionType.Create(
                CliValueKind.I4,
                CliValueKind.ManagedReference,
                CliValueKind.I4))),
        new(RuntimeImportSymbol.EndCatch, new WasmFunctionImport(
            RuntimeAbi.RuntimeModule,
            RuntimeAbi.RuntimeEndCatch,
            WasmFunctionType.Create(CliValueKind.Void))),
        new(RuntimeImportSymbol.ExceptionFrameEnter, new WasmFunctionImport(
            RuntimeAbi.RuntimeModule,
            RuntimeAbi.RuntimeExceptionFrameEnter,
            WasmFunctionType.Create(
                CliValueKind.I4,
                CliValueKind.ManagedAddress,
                CliValueKind.I4))),
        new(RuntimeImportSymbol.ExceptionFrameLeave, new WasmFunctionImport(
            RuntimeAbi.RuntimeModule,
            RuntimeAbi.RuntimeExceptionFrameLeave,
            WasmFunctionType.Create(CliValueKind.Void, CliValueKind.I4))),
        new(RuntimeImportSymbol.ExceptionFrameTargetClause, new WasmFunctionImport(
            RuntimeAbi.RuntimeModule,
            RuntimeAbi.RuntimeExceptionFrameTargetClause,
            WasmFunctionType.Create(CliValueKind.I4, CliValueKind.I4))),
        new(RuntimeImportSymbol.FinalizerSafepoint, new WasmFunctionImport(
            RuntimeAbi.RuntimeModule,
            RuntimeAbi.RuntimeFinalizerSafepoint,
            WasmFunctionType.Create(CliValueKind.ManagedReference))),
        new(RuntimeImportSymbol.RegisterValueType, new WasmFunctionImport(
            RuntimeAbi.RuntimeModule,
            RuntimeAbi.RuntimeRegisterValueType,
            WasmFunctionType.Create(
                CliValueKind.Void,
                CliValueKind.I4,
                CliValueKind.ManagedAddress,
                CliValueKind.ManagedAddress,
                CliValueKind.I4))),
        new(RuntimeImportSymbol.AllocateValueArray, new WasmFunctionImport(
            RuntimeAbi.RuntimeModule,
            RuntimeAbi.RuntimeAllocateValueArray,
            WasmFunctionType.Create(
                CliValueKind.ManagedReference,
                CliValueKind.I4,
                CliValueKind.I4,
                CliValueKind.I4,
                CliValueKind.I4))),
        new(RuntimeImportSymbol.AllocateRectangularArray, new WasmFunctionImport(
            RuntimeAbi.RuntimeModule,
            RuntimeAbi.RuntimeAllocateRectangularArray,
            WasmFunctionType.Create(
                CliValueKind.ManagedReference,
                CliValueKind.I4,
                CliValueKind.ManagedAddress,
                CliValueKind.I4,
                CliValueKind.I4,
                CliValueKind.I4,
                CliValueKind.I4))),
        new(RuntimeImportSymbol.ArrayRank, new WasmFunctionImport(
            RuntimeAbi.RuntimeModule,
            RuntimeAbi.RuntimeArrayRank,
            WasmFunctionType.Create(
                CliValueKind.I4,
                CliValueKind.ManagedReference))),
        new(RuntimeImportSymbol.ArrayGetLength, new WasmFunctionImport(
            RuntimeAbi.RuntimeModule,
            RuntimeAbi.RuntimeArrayGetLength,
            WasmFunctionType.Create(
                CliValueKind.I4,
                CliValueKind.ManagedReference,
                CliValueKind.I4))),
        new(RuntimeImportSymbol.ArrayCopy, new WasmFunctionImport(
            RuntimeAbi.RuntimeModule,
            RuntimeAbi.RuntimeArrayCopy,
            WasmFunctionType.Create(
                CliValueKind.I4,
                CliValueKind.ManagedReference,
                CliValueKind.I4,
                CliValueKind.ManagedReference,
                CliValueKind.I4,
                CliValueKind.I4))),
        new(RuntimeImportSymbol.ArrayClear, new WasmFunctionImport(
            RuntimeAbi.RuntimeModule,
            RuntimeAbi.RuntimeArrayClear,
            WasmFunctionType.Create(
                CliValueKind.Void,
                CliValueKind.ManagedReference,
                CliValueKind.I4,
                CliValueKind.I4))),
        new(RuntimeImportSymbol.ArrayClone, new WasmFunctionImport(
            RuntimeAbi.RuntimeModule,
            RuntimeAbi.RuntimeArrayClone,
            WasmFunctionType.Create(
                CliValueKind.ManagedReference,
                CliValueKind.ManagedReference))),
        new(RuntimeImportSymbol.ExceptionFrameSetEnvironment, new WasmFunctionImport(
            RuntimeAbi.RuntimeModule,
            RuntimeAbi.RuntimeExceptionFrameSetEnvironment,
            WasmFunctionType.Create(
                CliValueKind.Void,
                CliValueKind.I4,
                CliValueKind.ManagedAddress))),
        new(RuntimeImportSymbol.GetTypeObject, new WasmFunctionImport(
            RuntimeAbi.RuntimeModule,
            RuntimeAbi.RuntimeGetTypeObject,
            WasmFunctionType.Create(
                CliValueKind.ManagedReference,
                CliValueKind.I4,
                CliValueKind.I4))),
        new(RuntimeImportSymbol.HandleNew, new WasmFunctionImport(
            RuntimeAbi.RuntimeModule,
            RuntimeAbi.RuntimeHandleNew,
            WasmFunctionType.Create(CliValueKind.I4, CliValueKind.ManagedReference))),
        new(RuntimeImportSymbol.HandleGet, new WasmFunctionImport(
            RuntimeAbi.RuntimeModule,
            RuntimeAbi.RuntimeHandleGet,
            WasmFunctionType.Create(CliValueKind.ManagedReference, CliValueKind.I4))),
            new(RuntimeImportSymbol.HandleRelease, new WasmFunctionImport(
                RuntimeAbi.RuntimeModule,
                RuntimeAbi.RuntimeHandleRelease,
                WasmFunctionType.Create(CliValueKind.Void, CliValueKind.I4))),
            new(RuntimeImportSymbol.WeakHandleCreate, new WasmFunctionImport(
                RuntimeAbi.RuntimeModule,
                RuntimeAbi.RuntimeWeakHandleCreate,
                WasmFunctionType.Create(
                    CliValueKind.I4,
                    CliValueKind.ManagedReference,
                    CliValueKind.I4))),
            new(RuntimeImportSymbol.WeakHandleGet, new WasmFunctionImport(
                RuntimeAbi.RuntimeModule,
                RuntimeAbi.RuntimeWeakHandleGet,
                WasmFunctionType.Create(CliValueKind.ManagedReference, CliValueKind.I4))),
            new(RuntimeImportSymbol.WeakHandleSet, new WasmFunctionImport(
                RuntimeAbi.RuntimeModule,
                RuntimeAbi.RuntimeWeakHandleSet,
                WasmFunctionType.Create(
                    CliValueKind.Void,
                    CliValueKind.I4,
                    CliValueKind.ManagedReference))),
            new(RuntimeImportSymbol.WeakHandleRelease, new WasmFunctionImport(
                RuntimeAbi.RuntimeModule,
                RuntimeAbi.RuntimeWeakHandleRelease,
                WasmFunctionType.Create(CliValueKind.Void, CliValueKind.I4))),
            new(RuntimeImportSymbol.GcHandleCreate, new WasmFunctionImport(
                RuntimeAbi.RuntimeModule,
                RuntimeAbi.RuntimeGcHandleCreate,
                WasmFunctionType.Create(
                    CliValueKind.I4,
                    CliValueKind.ManagedReference,
                    CliValueKind.I4))),
            new(RuntimeImportSymbol.GcHandleGet, new WasmFunctionImport(
                RuntimeAbi.RuntimeModule,
                RuntimeAbi.RuntimeGcHandleGet,
                WasmFunctionType.Create(CliValueKind.ManagedReference, CliValueKind.I4))),
            new(RuntimeImportSymbol.GcHandleAddress, new WasmFunctionImport(
                RuntimeAbi.RuntimeModule,
                RuntimeAbi.RuntimeGcHandleAddress,
                WasmFunctionType.Create(CliValueKind.ManagedAddress, CliValueKind.I4))),
            new(RuntimeImportSymbol.GcHandleSet, new WasmFunctionImport(
                RuntimeAbi.RuntimeModule,
                RuntimeAbi.RuntimeGcHandleSet,
                WasmFunctionType.Create(
                    CliValueKind.Void,
                    CliValueKind.I4,
                    CliValueKind.ManagedReference))),
            new(RuntimeImportSymbol.GcHandleRelease, new WasmFunctionImport(
                RuntimeAbi.RuntimeModule,
                RuntimeAbi.RuntimeGcHandleRelease,
                WasmFunctionType.Create(CliValueKind.Void, CliValueKind.I4))),
            new(RuntimeImportSymbol.GcGetMetric, new WasmFunctionImport(
                RuntimeAbi.RuntimeModule,
                RuntimeAbi.RuntimeGcGetMetric,
                WasmFunctionType.Create(CliValueKind.I8, CliValueKind.I4))),
            new(RuntimeImportSymbol.GcMetricIsSupported, new WasmFunctionImport(
                RuntimeAbi.RuntimeModule,
                RuntimeAbi.RuntimeGcMetricIsSupported,
                WasmFunctionType.Create(CliValueKind.I4, CliValueKind.I4))),
            new(RuntimeImportSymbol.GcWaitForPendingFinalizers, new WasmFunctionImport(
                RuntimeAbi.RuntimeModule,
                RuntimeAbi.RuntimeGcWaitForPendingFinalizers,
                WasmFunctionType.Create(CliValueKind.Void))),
            new(RuntimeImportSymbol.ObjectIdentityHash, new WasmFunctionImport(
                RuntimeAbi.RuntimeModule,
                RuntimeAbi.RuntimeObjectIdentityHash,
                WasmFunctionType.Create(CliValueKind.I4, CliValueKind.ManagedReference))),
            new(RuntimeImportSymbol.NativeAlloc, new WasmFunctionImport(
            RuntimeAbi.RuntimeModule,
            RuntimeAbi.RuntimeNativeAlloc,
            WasmFunctionType.Create(CliValueKind.NativeInt, CliValueKind.NativeInt))),
        new(RuntimeImportSymbol.NativeRealloc, new WasmFunctionImport(
            RuntimeAbi.RuntimeModule,
            RuntimeAbi.RuntimeNativeRealloc,
            WasmFunctionType.Create(
                CliValueKind.NativeInt,
                CliValueKind.NativeInt,
                CliValueKind.NativeInt))),
        new(RuntimeImportSymbol.NativeFree, new WasmFunctionImport(
            RuntimeAbi.RuntimeModule,
            RuntimeAbi.RuntimeNativeFree,
            WasmFunctionType.Create(CliValueKind.Void, CliValueKind.NativeInt))),
        new(RuntimeImportSymbol.NativeAlignedAlloc, new WasmFunctionImport(
            RuntimeAbi.RuntimeModule,
            RuntimeAbi.RuntimeNativeAlignedAlloc,
            WasmFunctionType.Create(
                CliValueKind.NativeInt,
                CliValueKind.NativeInt,
                CliValueKind.NativeInt))),
        new(RuntimeImportSymbol.NativeAlignedRealloc, new WasmFunctionImport(
            RuntimeAbi.RuntimeModule,
            RuntimeAbi.RuntimeNativeAlignedRealloc,
            WasmFunctionType.Create(
                CliValueKind.NativeInt,
                CliValueKind.NativeInt,
                CliValueKind.NativeInt,
                CliValueKind.NativeInt))),
        new(RuntimeImportSymbol.NativeAlignedFree, new WasmFunctionImport(
            RuntimeAbi.RuntimeModule,
            RuntimeAbi.RuntimeNativeAlignedFree,
            WasmFunctionType.Create(CliValueKind.Void, CliValueKind.NativeInt))),
        new(RuntimeImportSymbol.ComponentReallocate, new WasmFunctionImport(
            RuntimeAbi.RuntimeModule,
            RuntimeAbi.RuntimeComponentReallocate,
            WasmFunctionType.Create(
                CliValueKind.ManagedAddress,
                CliValueKind.ManagedAddress,
                CliValueKind.ManagedAddress,
                CliValueKind.ManagedAddress,
                CliValueKind.ManagedAddress))),
        new(RuntimeImportSymbol.ComponentFree, new WasmFunctionImport(
            RuntimeAbi.RuntimeModule,
            RuntimeAbi.RuntimeComponentFree,
            WasmFunctionType.Create(CliValueKind.Void, CliValueKind.ManagedAddress))),
    ];
}
