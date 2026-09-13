using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission;

public interface IManagedModuleEmitter
{
    WasmModuleEmissionResult Emit(WasmEmissionRequest request);
}

public sealed class ManagedWasmEmitter(
    ITypeRepository types,
    ITypeDefinitionResolver typeDefinitions,
    IFieldRepository fields,
    IMethodRepository methods,
    ISymbolFormatter symbols,
    ITypeClassifier typeClassifier,
    IRuntimeIntrinsicRegistry intrinsics,
    ITargetLayout targetLayout,
    IValueLayoutProvider valueLayouts,
    ITypeLayoutProvider typeLayouts,
    IInstanceFieldLayoutProvider instanceFields,
    IStaticFieldLayoutProvider staticFields,
    IStaticDataLayout staticData,
    IRuntimeObjectLayout runtimeObjects,
    IManagedExceptionObjectProvider exceptionObjects,
    ITypeDescriptorSource typeDescriptors,
    IWasmModuleEmitterFactory modules) : IManagedModuleEmitter
{
    private readonly ITypeRepository _types =
        types ?? throw new ArgumentNullException(nameof(types));
    private readonly ITypeDefinitionResolver _typeDefinitions =
        typeDefinitions ?? throw new ArgumentNullException(nameof(typeDefinitions));
    private readonly IFieldRepository _fields =
        fields ?? throw new ArgumentNullException(nameof(fields));
    private readonly IMethodRepository _methods =
        methods ?? throw new ArgumentNullException(nameof(methods));
    private readonly ISymbolFormatter _symbols =
        symbols ?? throw new ArgumentNullException(nameof(symbols));
    private readonly ITypeClassifier _typeClassifier =
        typeClassifier ?? throw new ArgumentNullException(nameof(typeClassifier));
    private readonly IRuntimeIntrinsicRegistry _intrinsics =
        intrinsics ?? throw new ArgumentNullException(nameof(intrinsics));
    private readonly ITargetLayout _targetLayout = targetLayout ??
        throw new ArgumentNullException(nameof(targetLayout));
    private readonly IValueLayoutProvider _valueLayouts = valueLayouts ??
        throw new ArgumentNullException(nameof(valueLayouts));
    private readonly ITypeLayoutProvider _typeLayouts = typeLayouts ??
        throw new ArgumentNullException(nameof(typeLayouts));
    private readonly IInstanceFieldLayoutProvider _instanceFields = instanceFields ??
        throw new ArgumentNullException(nameof(instanceFields));
    private readonly IStaticFieldLayoutProvider _staticFields = staticFields ??
        throw new ArgumentNullException(nameof(staticFields));
    private readonly IStaticDataLayout _staticData = staticData ??
        throw new ArgumentNullException(nameof(staticData));
    private readonly IRuntimeObjectLayout _runtimeObjects = runtimeObjects ??
        throw new ArgumentNullException(nameof(runtimeObjects));
    private readonly IManagedExceptionObjectProvider _exceptionObjects = exceptionObjects ??
        throw new ArgumentNullException(nameof(exceptionObjects));
    private readonly ITypeDescriptorSource _typeDescriptors = typeDescriptors ??
        throw new ArgumentNullException(nameof(typeDescriptors));
    private readonly IWasmModuleEmitterFactory _modules =
        modules ?? throw new ArgumentNullException(nameof(modules));

    public byte[] Emit(
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
        ImmutableArray<HostCallbackDeclaration> hostCallbacks = default) =>
        Emit(WasmEmissionRequest.Create(
            entryPoint,
            methods,
            rootMaps,
            staticInitializers,
            requestedExports,
            constructedMethods,
            methodInstances,
            constructedRootMaps,
            constructedStaticInitializers,
            dispatchCallSites,
            typeTestSites,
            callableMethods,
            delegateTypes,
            jsImportMethods,
            witImportMethods,
            hostCallbacks)).Module;

    public WasmModuleEmissionResult Emit(WasmEmissionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _modules.Emit(
            _types,
            _typeDefinitions,
            _fields,
            _methods,
            _symbols,
            _typeClassifier,
            _intrinsics,
            _targetLayout,
            _valueLayouts,
            _typeLayouts,
            _instanceFields,
            _staticFields,
            _staticData,
            _runtimeObjects,
            _exceptionObjects,
            _typeDescriptors,
            request);
    }
}
