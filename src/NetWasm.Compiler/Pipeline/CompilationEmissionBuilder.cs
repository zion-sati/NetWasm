using NetWasm.Compiler.Wasm.Units;
using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.ComponentModel.Inlining;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Emission;
using NetWasm.Compiler.Layout;
using NetWasm.Compiler.Metadata;
using NetWasm.Compiler.StackTraces;
using NetWasm.Compiler.Symbols;
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
    IStackTraceSourceLocationReader stackTraceSourceLocations,
    IMethodOutlineProviderFactory outlines,
    IUnitStoreCommitter unitCommitter,
    ITypeIdentityResolverFactory typeIdentities,
    IRuntimeTypeSymbolTableBuilder typeSymbols,
    IAssemblyIdentityFormatterFactory assemblyNames,
    IRuntimeTypeAssemblyResolverFactory assemblyResolvers) : ICompilationEmissionBuilder
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
    private readonly IMethodOutlineProviderFactory _outlines = outlines ??
        throw new ArgumentNullException(nameof(outlines));
    private readonly IUnitStoreCommitter _unitCommitter = unitCommitter ??
        throw new ArgumentNullException(nameof(unitCommitter));
    private readonly ITypeIdentityResolverFactory _typeIdentities = typeIdentities ??
        throw new ArgumentNullException(nameof(typeIdentities));
    private readonly IAssemblyIdentityFormatterFactory _assemblyNames = assemblyNames ??
        throw new ArgumentNullException(nameof(assemblyNames));
    private readonly IRuntimeTypeAssemblyResolverFactory _assemblyResolvers = assemblyResolvers ??
        throw new ArgumentNullException(nameof(assemblyResolvers));
    private readonly IRuntimeTypeSymbolTableBuilder _typeSymbols = typeSymbols ??
        throw new ArgumentNullException(nameof(typeSymbols));

    public CompilationEmission Emit(
        MetadataCompilationSnapshot metadata,
        CompilationPreparation preparation,
        CompilationAnalysis analysis,
        CompilationLayouts layouts,
        CompilerOptions options,
        CompiledUnitState? units = null)
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
        // Methods whose units will be reused are not structured; diagnostics need every
        // method's structure.
        var outlines = units is null || options.DiagnosticTracePath is not null
            ? null
            : _outlines.Create(units, analysis.Program.Methods, analysis.Program.RootMaps,
                analysis.Program.ConstructedRootMaps,
                preparation.OptimizationSettings.BranchPrefixDuplication,
                preparation.OptimizationSettings.DispatcherElimination);
        var lowering = _methodProgram.Build(
            analysis.Program.Methods,
            analysis.Program.ConstructedMethods,
            preparation.OptimizationSettings.BranchPrefixDuplication,
            preparation.OptimizationSettings.DispatcherElimination,
            outlines);
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
            JavaScriptConversions = analysis.Program.JavaScriptConversions,
            ManagedCallSites = analysis.Program.ManagedCallSites,
            DelegateBindings = analysis.Program.DelegateBindings,
            ManagedByRefEffects = analysis.Program.ManagedByRefEffects,
            ObjectArrayDelegateAdapters = analysis.Program.ObjectArrayDelegateAdapters,
            MemberExecution = analysis.Program.MemberExecution,
            NativeCallbacks = nativeCallbacks,
            AddressedNativeCallbacks = addressedNativeCallbacks,
            EntryPointArgumentFactory = preparation.EntryPointArgumentFactory,
            EntryPointExit = preparation.EntryPointExit,
            CollectManagedMethodMemoryMetrics = options.DiagnosticTracePath is not null,
            CompiledUnits = units,
            UseJavaScriptExportBoundary =
                options.UseJavaScriptExportBoundary || options.WitPath is null,
            StructuredDiagnostics = options.StructuredDiagnostics,
            EnableAddressTakenAggregatePromotion =
                preparation.OptimizationSettings.AddressTakenValuePromotion !=
                AddressTakenValuePromotionPolicy.Disabled,
            EnableAggregateReferenceLanes =
                preparation.OptimizationSettings.AddressTakenValuePromotion ==
                AddressTakenValuePromotionPolicy.References,
            EnableBranchValueFramePlacement =
                preparation.OptimizationSettings.BranchPrefixDuplication ==
                BranchPrefixDuplicationPolicy.Speed,
            EnableArrayMetadataCaching =
                preparation.OptimizationSettings.LoopInvariantFieldHoisting !=
                LoopInvariantFieldHoistingPolicy.Disabled,
            HoistArrayNullChecks =
                preparation.OptimizationSettings.LoopInvariantFieldHoisting ==
                LoopInvariantFieldHoistingPolicy.NullChecks,
            LoopRotation = preparation.OptimizationSettings.LoopRotation,
            EliminateArrayStoreChecks =
                preparation.OptimizationSettings.ArrayStoreCheckElimination ==
                ArrayStoreCheckEliminationPolicy.ExactElementTypes,
            EnableScalarCallPreservation =
                preparation.OptimizationSettings.ScalarCallPreservation ==
                ScalarCallPreservationPolicy.Conservative,
            InlineSmallBlockOperations =
                preparation.OptimizationSettings.SmallBlockOperations ==
                SmallBlockOperationPolicy.Inline,
            InlineArrayData =
                preparation.OptimizationSettings.InlineArrayData ==
                InlineArrayDataPolicy.Inline,
            // allocate_object's extra argument pays only where the runtime's
            // inline hints are kept, so the inlined allocation bumps a constant run.
            InlineObjectAllocation =
                preparation.OptimizationSettings.RuntimeInlineAnnotations ==
                WasmInlineAnnotationSelection.Preserve,
            InlineMulticastDispatch =
                preparation.OptimizationSettings.MulticastDispatch ==
                MulticastDispatchPolicy.Inline,
            SourceLocations = _stackTraceSourceLocations.Read(metadata, options),
        };
        var typeSymbols = _typeSymbols.Build(layouts.Snapshot, _typeIdentities.Create(metadata), typeDefinitions,
            layouts.Snapshot.StaticData.RuntimeAssemblies, _assemblyNames.Create(metadata.Assemblies),
            _assemblyResolvers.Create(typeDefinitions));
        request = request with
        {
            Sidecars = new WasmSidecarRequest(
                options.ProgramFileName,
                options.EmitTypeNames,
                typeSymbols.Digest,
                SidecarDescriptorSlot(analysis.Program, types, fields, staticFieldLayouts)),
        };
        var emission = emitter.Emit(request);
        _invariants.Validate(emission.Module, options.Target);
        if (units is { Directory: { } directory, Change: { } change })
            _unitCommitter.Commit(directory, change);
        if (units is { FunctionIndices: { } functions, Target: { } target })
        {
            units.Kept = new UnitKeptCompile(emission.Module, functions,
                UnitPlanCodec.Encode(target, layouts.Snapshot, ProgramFacts(analysis.Program)), []);
        }
        return new CompilationEmission(emission, lowering, typeSymbols);
    }

    // CoreLib's ProgramSidecars.s_descriptor, the word the runtime initializer fills with the sidecar
    // descriptor's address; null when nothing reachable reads it.
    private static int? SidecarDescriptorSlot(
        ReachableProgram program,
        ITypeRepository types,
        IFieldRepository fields,
        IStaticFieldLayoutProvider staticFields)
    {
        foreach (var key in program.Fields)
        {
            var field = fields.GetField(key);
            if (!field.IsStatic || field.Name != "s_descriptor")
                continue;
            var type = types.GetTypeDefinition(field.DeclaringType);
            if (type.Namespace == "System.Runtime.InteropServices" && type.Name == "ProgramSidecars")
                return staticFields.GetStaticFieldLayout(key).Address;
        }
        return null;
    }

    // What a later compile's early cutoff derives an edited method's own facts from.
    private static UnitProgramFacts ProgramFacts(ReachableProgram program) => new(
        program.AllocatedTypes,
        program.Methods.ToDictionary(pair => pair.Key, pair => pair.Value.Method.CanonicalName),
        [.. program.ConstructedMethods.Keys.Order(StringComparer.Ordinal)],
        program.ManagedByRefEffects.Methods
            .Where(pair => !pair.Value.WrittenArguments.IsEmpty || !pair.Value.EscapedArguments.IsEmpty ||
                !pair.Value.ReturnedArgumentAliases.IsEmpty)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
        program.ManagedByRefEffects.Callees);
}
