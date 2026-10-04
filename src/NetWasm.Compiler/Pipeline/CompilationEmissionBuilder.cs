using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Emission;
using NetWasm.Compiler.Layout;
using NetWasm.Compiler.Metadata;
using NetWasm.Compiler.StackTraces;
using NetWasm.Compiler.Validation;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Pipeline;

internal sealed class CompilationEmissionBuilder(
    IManagedWasmEmitterFactory emitters,
    IWasmMethodProgramBuilder methodProgram,
    IStructuredProgramInvariantValidator structuredInvariants,
    IWasmEmissionInvariantValidator invariants,
    ITypeRepositoryFactory typeRepositories,
    IFieldRepositoryFactory fieldRepositories,
    IMethodRepositoryFactory methodRepositories,
    ISymbolFormatterFactory symbols,
    ITypeClassifierFactory classifiers,
    IRuntimeIntrinsicRegistryFactory intrinsics,
    ITypeDefinitionResolverFactory typeDefinitions,
    IValueLayoutResolverFactory valueResolvers,
    IValueLayoutProviderFactory valueProviders,
    ITypeLayoutProviderFactory typeProviders,
    IInstanceFieldLayoutProviderFactory instanceFields,
    IStaticFieldLayoutProviderFactory staticFields,
    IStaticDataLayoutProviderFactory staticData,
    IMemberDescriptorLayoutProviderFactory memberDescriptors,
    IManagedExceptionObjectProviderFactory exceptionObjects,
    IStackTraceSourceLocationReader stackTraceSourceLocations) : ICompilationEmissionBuilder
{
    private readonly IManagedWasmEmitterFactory _emitters = emitters ??
        throw new ArgumentNullException(nameof(emitters));
    private readonly IWasmMethodProgramBuilder _methodProgram = methodProgram ??
        throw new ArgumentNullException(nameof(methodProgram));
    private readonly IStructuredProgramInvariantValidator _structuredInvariants =
        structuredInvariants ?? throw new ArgumentNullException(nameof(structuredInvariants));
    private readonly IWasmEmissionInvariantValidator _invariants = invariants ??
        throw new ArgumentNullException(nameof(invariants));
    private readonly ITypeRepositoryFactory _typeRepositories = typeRepositories ??
        throw new ArgumentNullException(nameof(typeRepositories));
    private readonly IFieldRepositoryFactory _fieldRepositories = fieldRepositories ??
        throw new ArgumentNullException(nameof(fieldRepositories));
    private readonly IMethodRepositoryFactory _methodRepositories = methodRepositories ??
        throw new ArgumentNullException(nameof(methodRepositories));
    private readonly ISymbolFormatterFactory _symbols = symbols ??
        throw new ArgumentNullException(nameof(symbols));
    private readonly ITypeClassifierFactory _classifiers = classifiers ??
        throw new ArgumentNullException(nameof(classifiers));
    private readonly IRuntimeIntrinsicRegistryFactory _intrinsics = intrinsics ??
        throw new ArgumentNullException(nameof(intrinsics));
    private readonly ITypeDefinitionResolverFactory _typeDefinitions = typeDefinitions ??
        throw new ArgumentNullException(nameof(typeDefinitions));
    private readonly IValueLayoutResolverFactory _valueResolvers = valueResolvers ??
        throw new ArgumentNullException(nameof(valueResolvers));
    private readonly IValueLayoutProviderFactory _valueProviders = valueProviders ??
        throw new ArgumentNullException(nameof(valueProviders));
    private readonly ITypeLayoutProviderFactory _typeProviders = typeProviders ??
        throw new ArgumentNullException(nameof(typeProviders));
    private readonly IInstanceFieldLayoutProviderFactory _instanceFields = instanceFields ??
        throw new ArgumentNullException(nameof(instanceFields));
    private readonly IStaticFieldLayoutProviderFactory _staticFields = staticFields ??
        throw new ArgumentNullException(nameof(staticFields));
    private readonly IStaticDataLayoutProviderFactory _staticData = staticData ??
        throw new ArgumentNullException(nameof(staticData));
    private readonly IMemberDescriptorLayoutProviderFactory _memberDescriptors = memberDescriptors ??
        throw new ArgumentNullException(nameof(memberDescriptors));
    private readonly IManagedExceptionObjectProviderFactory _exceptionObjects =
        exceptionObjects ?? throw new ArgumentNullException(nameof(exceptionObjects));
    private readonly IStackTraceSourceLocationReader _stackTraceSourceLocations =
        stackTraceSourceLocations ??
        throw new ArgumentNullException(nameof(stackTraceSourceLocations));

    public CompilationEmission Emit(
        MetadataCompilationSnapshot metadata,
        CompilationPreparation preparation,
        CompilationAnalysis analysis,
        CompilationLayouts layouts,
        CompilerOptions options)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(preparation);
        ArgumentNullException.ThrowIfNull(analysis);
        ArgumentNullException.ThrowIfNull(layouts);
        ArgumentNullException.ThrowIfNull(options);

        var types = _typeRepositories.Create(metadata);
        var fields = _fieldRepositories.Create(metadata);
        var methods = _methodRepositories.Create(metadata);
        var symbols = _symbols.Create(metadata);
        var typeClassifier = _classifiers.Create(metadata);
        var typeDefinitions = _typeDefinitions.Create(metadata);
        var valueResolver = _valueResolvers.Create(
            typeDefinitions,
            fields,
            layouts.Snapshot.Target,
            layouts.Snapshot.TypeLayouts.ValueLayoutState);
        var values = _valueProviders.Create(valueResolver);
        var typeLayouts = _typeProviders.Create(typeDefinitions, layouts.Snapshot);
        var instanceFieldLayouts = _instanceFields.Create(layouts.Snapshot);
        var staticFieldLayouts = _staticFields.Create(layouts.Snapshot);
        var staticDataLayout = _staticData.Create(layouts.Snapshot);
        var memberDescriptorLayout = _memberDescriptors.Create(layouts.Snapshot);
        var exceptionObjectLayouts = _exceptionObjects.Create(layouts.Snapshot);
        var runtimeIntrinsics = _intrinsics.Create(symbols, metadata.Methods);
        var lowering = _methodProgram.Build(
            analysis.Program.Methods,
            analysis.Program.ConstructedMethods);
        _structuredInvariants.Validate(symbols, lowering);

        var emitter = _emitters.Create(
            types,
            typeDefinitions,
            fields,
            methods,
            symbols,
            typeClassifier,
            runtimeIntrinsics,
            layouts.Snapshot,
            values,
            typeLayouts,
            instanceFieldLayouts,
            staticFieldLayouts,
            staticDataLayout,
            memberDescriptorLayout,
            layouts.Snapshot,
            exceptionObjectLayouts,
            layouts.Snapshot);
        var request = WasmEmissionRequest.Create(
            preparation.Entry.EntryPoint,
            lowering.Methods,
            analysis.Program.RootMaps,
            analysis.Program.StaticInitializers,
            preparation.Exports.ToImmutableDictionary(
                export => export.Name,
                export => export.Method),
            lowering.ConstructedMethods,
            analysis.Program.MethodInstances,
            analysis.Program.ConstructedRootMaps,
            analysis.Program.ConstructedStaticInitializers,
            analysis.Program.DispatchCallSites,
            analysis.Program.TypeTestSites,
            analysis.Program.CallableMethods,
            analysis.Program.DelegateTypes,
            analysis.Program.JSImportMethods,
            analysis.Program.WitImportMethods,
            analysis.Program.HostCallbacks,
            preparation.ComponentContract,
            analysis.Program.JavaScriptAsyncBindings,
            options.EmitStackTrace,
            options.WitPath is null
                ? WasmModuleProfile.CoreApplication
                : WasmModuleProfile.ComponentCoreModule,
            options.EntryPointKind == CompilerEntryPointKind.Library
                ? WasmEntryPointProfile.None
                : options.WitPath is null ||
            options.EntryPointKind == CompilerEntryPointKind.ManagedExecutable
                ? WasmEntryPointProfile.Process
                : WasmEntryPointProfile.Internal);
        var addressedNativeCallbacks = analysis.Program.NativeCallbacks.Keys
            .ToImmutableHashSet(StringComparer.Ordinal);
        var nativeCallbacks = analysis.Program.NativeCallbacks.Values
            .Concat(analysis.Program.MethodInstances.Values.Where(method =>
                method.Definition.NativeCallback?.EntryPoint is not null))
            .DistinctBy(method => method.CanonicalName)
            .ToImmutableDictionary(
                method => method.CanonicalName,
                StringComparer.Ordinal);
        request = request with
        {
            ModuleInitializers = analysis.Program.ModuleInitializers,
            ManagedCallSites = analysis.Program.ManagedCallSites,
            DelegateBindings = analysis.Program.DelegateBindings,
            ObjectArrayDelegateAdapters = analysis.Program.ObjectArrayDelegateAdapters,
            MemberExecution = analysis.Program.MemberExecution,
            NativeCallbacks = nativeCallbacks,
            AddressedNativeCallbacks = addressedNativeCallbacks,
            EntryPointArgumentFactory = preparation.EntryPointArgumentFactory,
            CollectManagedMethodMemoryMetrics = options.DiagnosticTracePath is not null,
            UseJavaScriptExportBoundary =
                options.UseJavaScriptExportBoundary || options.WitPath is null,
            StructuredDiagnostics = options.StructuredDiagnostics,
            SourceLocations = _stackTraceSourceLocations.Read(metadata, options),
        };
        var emission = emitter.Emit(request);
        _invariants.Validate(emission.Module, options.Target);
        return new CompilationEmission(emission, lowering);
    }
}
