using NetWasm.Compiler.Wasm.Units;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.GarbageCollection;
using NetWasm.Compiler.Layout;
using NetWasm.Compiler.Metadata;
using NetWasm.Compiler.Validation;

namespace NetWasm.Compiler.Pipeline;

internal sealed class CompilationRootMapBuilder(
    IRootMapAnalyzerFactory analyzers,
    IRootMapInvariantValidator invariants,
    ITypeRepositoryFactory typeRepositories,
    IFieldRepositoryFactory fieldRepositories,
    IMethodRepositoryFactory methodRepositories,
    ITypeDefinitionResolverFactory typeDefinitions,
    IValueLayoutResolverFactory valueResolvers,
    IValueLayoutProviderFactory valueProviders,
    IManagedLayoutForkSourceFactory forks,
    IIndexedWorkExecutor work,
    CompilerParallelism parallelism,
    ITypeClassifierFactory typeClassifiers) : ICompilationRootMapBuilder
{
    private readonly IRootMapAnalyzerFactory _analyzers = analyzers ??
        throw new ArgumentNullException(nameof(analyzers));
    private readonly IRootMapInvariantValidator _invariants = invariants ??
        throw new ArgumentNullException(nameof(invariants));
    private readonly ITypeRepositoryFactory _typeRepositories = typeRepositories ??
        throw new ArgumentNullException(nameof(typeRepositories));
    private readonly IFieldRepositoryFactory _fieldRepositories = fieldRepositories ??
        throw new ArgumentNullException(nameof(fieldRepositories));
    private readonly IMethodRepositoryFactory _methodRepositories = methodRepositories ??
        throw new ArgumentNullException(nameof(methodRepositories));
    private readonly ITypeDefinitionResolverFactory _typeDefinitions = typeDefinitions ??
        throw new ArgumentNullException(nameof(typeDefinitions));
    private readonly IValueLayoutResolverFactory _valueResolvers = valueResolvers ??
        throw new ArgumentNullException(nameof(valueResolvers));
    private readonly IValueLayoutProviderFactory _valueProviders = valueProviders ??
        throw new ArgumentNullException(nameof(valueProviders));
    private readonly IManagedLayoutForkSourceFactory _forks = forks ??
        throw new ArgumentNullException(nameof(forks));
    private readonly IIndexedWorkExecutor _work = work ??
        throw new ArgumentNullException(nameof(work));
    private readonly CompilerParallelism _parallelism = parallelism ??
        throw new ArgumentNullException(nameof(parallelism));

    private readonly ITypeClassifierFactory _typeClassifiers = typeClassifiers ??
        throw new ArgumentNullException(nameof(typeClassifiers));

    public ReachableProgram Analyze(
        MetadataCompilationSnapshot metadata,
        CompilationLayouts layouts,
        ReachableProgram program,
        CompiledUnitState? units = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(layouts);
        ArgumentNullException.ThrowIfNull(program);
        var types = _typeRepositories.Create(metadata);
        var fields = _fieldRepositories.Create(metadata);
        var methods = _methodRepositories.Create(metadata);
        var typeDefinitions = _typeDefinitions.Create(metadata);
        var typeClassifier = _typeClassifiers.Create(metadata);
        ArgumentNullException.ThrowIfNull(types);
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(methods);
        var dispatchCallSites = Lookups.Of(program.DispatchCallSites);
        var allocatingMethods = Lookups.Of(program.AllocatingMethods);
        var allocatingConstructedMethods = Lookups.Of(program.ConstructedAllocatingMethods);
        // Compiled units are recorded and restored by root-map workers, so a compile that
        // keeps them uses one even without parallelism.
        if (_parallelism.WorkerCount == 1 && units?.Directory is null)
        {
            var valueResolver = _valueResolvers.Create(
                typeDefinitions,
                fields,
                layouts.Snapshot.Target,
                layouts.Snapshot.TypeLayouts.ValueLayoutState);
            var values = _valueProviders.Create(valueResolver);
            var analyzer = _analyzers.Create(
                types, fields, methods, values, dispatchCallSites, typeClassifier);
            var serial = program with
            {
                RootMaps = program.Methods.ToImmutableDictionary(
                    pair => pair.Key,
                    pair => analyzer.Analyze(new RootMapAnalysisRequest(
                        pair.Value,
                        allocatingMethods,
                        allocatingConstructedMethods))),
                ConstructedRootMaps = program.ConstructedMethods.ToImmutableDictionary(
                    pair => pair.Key,
                    pair => analyzer.Analyze(new RootMapAnalysisRequest(
                        pair.Value,
                        allocatingMethods,
                        allocatingConstructedMethods)),
                    StringComparer.Ordinal),
            };
            _invariants.Validate(types, fields, methods, serial, typeClassifier);
            return serial;
        }

        var ordinary = program.Methods.ToArray();
        var constructed = program.ConstructedMethods.ToArray();
        var bodies = ordinary.Select(pair => pair.Value)
            .Concat(constructed.Select(pair => pair.Value)).ToArray();
        var forkSet = _forks.Create(layouts.Snapshot, types,
            typeDefinitions, fields).Create(Math.Min(
                _parallelism.WorkerCount, Math.Max(1, bodies.Length)));
        // With compiled units each worker records the facts a root map is built from, and
        // takes a method's root map from its unit where those facts still hold.
        var workers = forkSet.Workers.Select(fork =>
        {
            var recorder = units?.Directory is null ? null : new UnitRootMapRecorder(units, fork.Values,
                dispatchCallSites, allocatingMethods, allocatingConstructedMethods, fields);
            return (Analyzer: _analyzers.Create(types, fields, methods, recorder?.Values ?? fork.Values,
                recorder?.DispatchCallSites ?? dispatchCallSites, typeClassifier), Units: recorder);
        }).ToArray();
        var results = _work.Execute(workers, bodies.Length, (worker, index) => worker.Units is { } recorder
            ? recorder.Map(bodies[index], () => worker.Analyzer.Analyze(new RootMapAnalysisRequest(
                bodies[index], recorder.AllocatingMethods, recorder.AllocatingConstructedMethods)))
            : worker.Analyzer.Analyze(new RootMapAnalysisRequest(
                bodies[index], allocatingMethods, allocatingConstructedMethods)));
        var ordinaryMaps = ImmutableDictionary.CreateBuilder<EntityKey,
            MethodRootMap>();
        for (var index = 0; index < ordinary.Length; index++)
            ordinaryMaps.Add(ordinary[index].Key, results[index]);
        var constructedMaps = ImmutableDictionary.CreateBuilder<string,
            MethodRootMap>(StringComparer.Ordinal);
        for (var index = 0; index < constructed.Length; index++)
            constructedMaps.Add(constructed[index].Key,
                results[ordinary.Length + index]);
        var updated = program with
        {
            RootMaps = ordinaryMaps.ToImmutable(),
            ConstructedRootMaps = constructedMaps.ToImmutable(),
        };
        _invariants.Validate(types, fields, methods, updated, typeClassifier,
            units is null || units.RootMapSources.IsEmpty
                ? null
                : units.RootMapSources.Keys.ToHashSet(StringComparer.Ordinal));
        forkSet.Publisher.Publish();
        return updated;
    }
}
