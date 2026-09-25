using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using System.Collections.Immutable;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class RuntimeImportCatalogTests
{
    [Fact]
    public void CatalogOwnsEveryRuntimeImportInStableAbiOrder()
    {
        var catalog = WasmRuntimeImports.CreateCatalog();
        var expectedNames = new[]
        {
            RuntimeAbi.RuntimeInitialize,
            RuntimeAbi.RuntimeAllocate,
            RuntimeAbi.RuntimeCollect,
            RuntimeAbi.RuntimeRegisterType,
            RuntimeAbi.RuntimeRegisterStaticRoot,
            RuntimeAbi.RuntimeRootFrameEnter,
            RuntimeAbi.RuntimeRootFrameLeave,
            RuntimeAbi.RuntimeValueFrameEnter,
            RuntimeAbi.RuntimeValueFrameLeave,
            RuntimeAbi.RuntimeBeginThrow,
            RuntimeAbi.RuntimeBeginRethrow,
            RuntimeAbi.RuntimeAllocateReferenceArray,
            RuntimeAbi.RuntimeAllocateString,
            RuntimeAbi.RuntimeSuppressFinalize,
            RuntimeAbi.RuntimeReRegisterForFinalize,
            RuntimeAbi.RuntimeReportUnobservedTaskException,
            RuntimeAbi.RuntimeIsAssignable,
            RuntimeAbi.RuntimeEndCatch,
            RuntimeAbi.RuntimeExceptionFrameEnter,
            RuntimeAbi.RuntimeExceptionFrameLeave,
            RuntimeAbi.RuntimeExceptionFrameTargetClause,
            RuntimeAbi.RuntimeFinalizerSafepoint,
            RuntimeAbi.RuntimeRegisterValueType,
            RuntimeAbi.RuntimeAllocateValueArray,
            RuntimeAbi.RuntimeAllocateRectangularArray,
            RuntimeAbi.RuntimeAllocateBoundedRectangularArray,
            RuntimeAbi.RuntimeArrayRank,
            RuntimeAbi.RuntimeArrayGetLength,
            RuntimeAbi.RuntimeArrayGetLowerBound,
            RuntimeAbi.RuntimeArrayGetValue,
            RuntimeAbi.RuntimeArrayCopy,
            RuntimeAbi.RuntimeArrayClear,
            RuntimeAbi.RuntimeArrayClone,
            RuntimeAbi.RuntimeExceptionFrameSetEnvironment,
            RuntimeAbi.RuntimeGetTypeObject,
            RuntimeAbi.RuntimeHandleNew,
            RuntimeAbi.RuntimeHandleGet,
            RuntimeAbi.RuntimeHandleRelease,
            RuntimeAbi.RuntimeWeakHandleCreate,
            RuntimeAbi.RuntimeWeakHandleGet,
            RuntimeAbi.RuntimeWeakHandleSet,
            RuntimeAbi.RuntimeWeakHandleRelease,
            RuntimeAbi.RuntimeGcHandleCreate,
            RuntimeAbi.RuntimeGcHandleGet,
            RuntimeAbi.RuntimeGcHandleAddress,
            RuntimeAbi.RuntimeGcHandleSet,
            RuntimeAbi.RuntimeGcHandleRelease,
            RuntimeAbi.RuntimeGcGetMetric,
            RuntimeAbi.RuntimeGcMetricIsSupported,
            RuntimeAbi.RuntimeGcWaitForPendingFinalizers,
            RuntimeAbi.RuntimeObjectIdentityHash,
            RuntimeAbi.RuntimeNativeAlloc,
            RuntimeAbi.RuntimeNativeRealloc,
            RuntimeAbi.RuntimeNativeFree,
            RuntimeAbi.RuntimeNativeAlignedAlloc,
            RuntimeAbi.RuntimeNativeAlignedRealloc,
            RuntimeAbi.RuntimeNativeAlignedFree,
            RuntimeAbi.RuntimeComponentReallocate,
            RuntimeAbi.RuntimeComponentFree,
            RuntimeAbi.RuntimeReportTerminalException,
            RuntimeAbi.RuntimeStackTraceFrameEnter,
            RuntimeAbi.RuntimeStackTraceFrameLeave,
            RuntimeAbi.RuntimeStackTraceInitialize,
        };

        Assert.Equal(expectedNames, catalog.Imports.Select(import => import.Name));
        foreach (var symbol in Enum.GetValues<RuntimeImportSymbol>())
        {
            Assert.Equal((int)symbol, catalog.Resolve(symbol));
        }
    }

    [Fact]
    public void CoreModulesOmitComponentAllocatorsAndDisabledStackTraceImports()
    {
        var catalog = WasmRuntimeImports.CreateCatalog();

        var imports = catalog.Resolve(WasmModuleProfile.CoreApplication);

        Assert.DoesNotContain(imports, import => import.Name == RuntimeAbi.RuntimeComponentReallocate);
        Assert.DoesNotContain(imports, import => import.Name == RuntimeAbi.RuntimeComponentFree);
        Assert.Contains(imports, import => import.Name == RuntimeAbi.RuntimeReportTerminalException);
        Assert.DoesNotContain(imports,
            import => import.Name == RuntimeAbi.RuntimeStackTraceFrameEnter);
        Assert.DoesNotContain(imports,
            import => import.Name == RuntimeAbi.RuntimeStackTraceFrameLeave);
        Assert.DoesNotContain(imports,
            import => import.Name == RuntimeAbi.RuntimeStackTraceInitialize);
    }

    [Fact]
    public void StackTraceSelectionIncludesEveryTraceImportWithProfileLocalIndices()
    {
        var catalog = WasmRuntimeImports.CreateCatalog();
        var selection = new RuntimeImportSelection(
            WasmModuleProfile.CoreApplication,
            IncludeTerminalExceptionReporter: true,
            IncludeStackTrace: true);

        var imports = catalog.Resolve(selection);

        Assert.Equal(
            RuntimeAbi.RuntimeStackTraceFrameEnter,
            imports[catalog.Resolve(
                RuntimeImportSymbol.StackTraceFrameEnter,
                selection)].Name);
        Assert.Equal(
            RuntimeAbi.RuntimeStackTraceFrameLeave,
            imports[catalog.Resolve(
                RuntimeImportSymbol.StackTraceFrameLeave,
                selection)].Name);
        Assert.Equal(
            RuntimeAbi.RuntimeStackTraceInitialize,
            imports[catalog.Resolve(
                RuntimeImportSymbol.StackTraceInitialize,
                selection)].Name);
    }

    [Fact]
    public void DuplicateAndMissingSymbolsAreRejected()
    {
        var import = new WasmFunctionImport(
            "test",
            "duplicate",
            WasmFunctionType.Create(CliValueKind.Void));
        var duplicate = Enum.GetValues<RuntimeImportSymbol>()
            .Select(symbol => new RuntimeImportBinding(symbol, import))
            .Append(new RuntimeImportBinding(RuntimeImportSymbol.Initialize, import));
        var missing = Enum.GetValues<RuntimeImportSymbol>()
            .Where(symbol => symbol != RuntimeImportSymbol.HandleRelease)
            .Select(symbol => new RuntimeImportBinding(symbol, import));

        var duplicateError = Assert.Throws<InvalidOperationException>(
            () => new RuntimeImportCatalog(duplicate));
        var missingError = Assert.Throws<InvalidOperationException>(
            () => new RuntimeImportCatalog(missing));

        Assert.Contains("registered more than once", duplicateError.Message);
        Assert.Contains(nameof(RuntimeImportSymbol.HandleRelease), missingError.Message);
    }
    [Fact]
    public void ImportProfilesUseProfileLocalIndices()
    {
        var catalog = WasmRuntimeImports.CreateCatalog();
        var coreImports = catalog.Resolve(WasmModuleProfile.CoreApplication);
        var componentImports = catalog.Resolve(WasmModuleProfile.ComponentCoreModule);
        var reporterIndex = catalog.Resolve(
            RuntimeImportSymbol.ManagedTerminalExceptionReport,
            WasmModuleProfile.CoreApplication);

        Assert.Equal(RuntimeAbi.RuntimeReportTerminalException, coreImports[reporterIndex].Name);
        Assert.DoesNotContain(componentImports, import => import.Name == RuntimeAbi.RuntimeReportTerminalException);
        Assert.Contains(componentImports, import => import.Name == RuntimeAbi.RuntimeComponentReallocate);
        Assert.Contains(componentImports, import => import.Name == RuntimeAbi.RuntimeComponentFree);
        Assert.Throws<InvalidOperationException>(() =>
            catalog.Resolve(
                RuntimeImportSymbol.ManagedTerminalExceptionReport,
                WasmModuleProfile.ComponentCoreModule));
    }

    [Fact]
    public void ComponentProfileCanIncludeTerminalReporterForHostCallbacks()
    {
        var catalog = WasmRuntimeImports.CreateCatalog();
        var selection = new RuntimeImportSelection(
            WasmModuleProfile.ComponentCoreModule,
            IncludeTerminalExceptionReporter: true);

        var imports = catalog.Resolve(selection);
        var reporterIndex = catalog.Resolve(
            RuntimeImportSymbol.ManagedTerminalExceptionReport,
            selection);

        Assert.Equal(RuntimeAbi.RuntimeReportTerminalException,
            imports[reporterIndex].Name);
        Assert.Contains(imports,
            import => import.Name == RuntimeAbi.RuntimeComponentReallocate);
        Assert.Contains(imports,
            import => import.Name == RuntimeAbi.RuntimeComponentFree);
    }

    [Fact]
    public void SelectionDefaultsForwardToTheProfileActions()
    {
        IRuntimeImportResolver resolver = new ProfileOnlyRuntimeImportResolver();
        var selection = new RuntimeImportSelection(
            WasmModuleProfile.ComponentCoreModule,
            IncludeTerminalExceptionReporter: true);

        Assert.Equal(17, resolver.Resolve(RuntimeImportSymbol.Allocate, selection));
        Assert.Empty(resolver.Resolve(selection));
    }

    private sealed class ProfileOnlyRuntimeImportResolver : IRuntimeImportResolver
    {
        public int Resolve(RuntimeImportSymbol symbol) => 17;

        public int Resolve(RuntimeImportSymbol symbol, WasmModuleProfile profile)
        {
            Assert.Equal(WasmModuleProfile.ComponentCoreModule, profile);
            return 17;
        }

        public ImmutableArray<WasmFunctionImport> Resolve(WasmModuleProfile profile)
        {
            Assert.Equal(WasmModuleProfile.ComponentCoreModule, profile);
            return [];
        }
    }
}
