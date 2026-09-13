using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;

using NetWasm.Compiler.Core.IntermediateRepresentation.Calls;
using NetWasm.Compiler.Core.IntermediateRepresentation.Delegates;

namespace NetWasm.Compiler.Wasm.Emission;

public enum WasmModuleProfile
{
    CoreApplication,
    ComponentCoreModule
}

public enum WasmEntryPointProfile
{
    Internal,
    Process
}

public sealed record WasmEmissionRequest(
    MethodDefinitionModel EntryPoint,
    IReadOnlyDictionary<EntityKey, StructuredMethod> Methods,
    IReadOnlyDictionary<EntityKey, MethodRootMap> RootMaps,
    IReadOnlyList<EntityKey> StaticInitializers,
    IReadOnlyDictionary<string, EntityKey> RequestedExports,
    IReadOnlyDictionary<string, StructuredMethod> ConstructedMethods,
    IReadOnlyDictionary<string, MethodInstanceModel> MethodInstances,
    IReadOnlyDictionary<string, MethodRootMap> ConstructedRootMaps,
    IReadOnlyList<string> ConstructedStaticInitializers,
    IReadOnlyDictionary<string, DispatchCallSiteModel> DispatchCallSites,
    IReadOnlyDictionary<string, TypeTestSiteModel> TypeTestSites,
    IReadOnlyDictionary<string, MethodInstanceModel> CallableMethods,
    ImmutableArray<CliTypeIdentity> DelegateTypes,
    ImmutableArray<MethodDefinitionModel> JSImportMethods,
    ImmutableArray<MethodDefinitionModel> WitImportMethods,
    ImmutableArray<HostCallbackDeclaration> HostCallbacks,
    ComponentBoundaryContract ComponentContract,
    IReadOnlyDictionary<EntityKey, JavaScriptAsyncMethodBinding> JavaScriptAsyncBindings,
    bool EmitStackTrace = false,
    WasmModuleProfile ModuleProfile = WasmModuleProfile.CoreApplication,
    WasmEntryPointProfile EntryPointProfile = WasmEntryPointProfile.Process)
{
    public ImmutableArray<EntityKey> ModuleInitializers { get; init; } = [];

    public ImmutableDictionary<ManagedCallSiteKey, ManagedCallSite> ManagedCallSites { get; init; } =
        ImmutableDictionary<ManagedCallSiteKey, ManagedCallSite>.Empty;

    public ImmutableArray<ManagedDelegateBinding> DelegateBindings { get; init; } = [];

    public EntityKey? EntryPointArgumentFactory { get; init; }

    public static WasmEmissionRequest Create(
        MethodDefinitionModel entryPoint,
        IReadOnlyDictionary<EntityKey, StructuredMethod> methods,
        IReadOnlyDictionary<EntityKey, MethodRootMap> rootMaps,
        IReadOnlyList<EntityKey> staticInitializers,
        IReadOnlyDictionary<string, EntityKey> requestedExports,
        IReadOnlyDictionary<string, StructuredMethod>? constructedMethods = null,
        IReadOnlyDictionary<string, MethodInstanceModel>? methodInstances = null,
        IReadOnlyDictionary<string, MethodRootMap>? constructedRootMaps = null,
        IReadOnlyList<string>? constructedStaticInitializers = null,
        IReadOnlyDictionary<string, DispatchCallSiteModel>? dispatchCallSites = null,
        IReadOnlyDictionary<string, TypeTestSiteModel>? typeTestSites = null,
        IReadOnlyDictionary<string, MethodInstanceModel>? callableMethods = null,
        ImmutableArray<CliTypeIdentity> delegateTypes = default,
        ImmutableArray<MethodDefinitionModel> jsImportMethods = default,
        ImmutableArray<MethodDefinitionModel> witImportMethods = default,
        ImmutableArray<HostCallbackDeclaration> hostCallbacks = default,
        ComponentBoundaryContract? componentContract = null,
        IReadOnlyDictionary<EntityKey, JavaScriptAsyncMethodBinding>? javaScriptAsyncBindings = null,
        bool emitStackTrace = false,
        WasmModuleProfile moduleProfile = WasmModuleProfile.CoreApplication,
        WasmEntryPointProfile entryPointProfile = WasmEntryPointProfile.Process)
    {
        ArgumentNullException.ThrowIfNull(entryPoint);
        ArgumentNullException.ThrowIfNull(methods);
        ArgumentNullException.ThrowIfNull(rootMaps);
        ArgumentNullException.ThrowIfNull(staticInitializers);
        ArgumentNullException.ThrowIfNull(requestedExports);
        var resolvedComponentContract = componentContract ?? ComponentBoundaryContract.Empty;
        if (moduleProfile == WasmModuleProfile.CoreApplication && !resolvedComponentContract.IsEmpty)
        {
            throw new ArgumentException("Core application modules cannot contain component adapter contracts.", nameof(componentContract));
        }

        if (moduleProfile == WasmModuleProfile.ComponentCoreModule && resolvedComponentContract.IsEmpty)
        {
            throw new ArgumentException("Component core modules require a component adapter contract.", nameof(componentContract));
        }

        return new WasmEmissionRequest(
            entryPoint,
            methods,
            rootMaps,
            staticInitializers,
            requestedExports,
            constructedMethods ?? ImmutableDictionary<string, StructuredMethod>.Empty,
            methodInstances ?? ImmutableDictionary<string, MethodInstanceModel>.Empty,
            constructedRootMaps ?? ImmutableDictionary<string, MethodRootMap>.Empty,
            constructedStaticInitializers ?? [],
            dispatchCallSites ?? ImmutableDictionary<string, DispatchCallSiteModel>.Empty,
            typeTestSites ?? ImmutableDictionary<string, TypeTestSiteModel>.Empty,
            callableMethods ?? ImmutableDictionary<string, MethodInstanceModel>.Empty,
            delegateTypes.IsDefault ? [] : delegateTypes,
            jsImportMethods.IsDefault ? [] : jsImportMethods,
            witImportMethods.IsDefault ? [] : witImportMethods,
            hostCallbacks.IsDefault ? [] : hostCallbacks,
            resolvedComponentContract,
            javaScriptAsyncBindings ??
                ImmutableDictionary<EntityKey, JavaScriptAsyncMethodBinding>.Empty,
            emitStackTrace,
            moduleProfile,
            entryPointProfile);
    }
}
