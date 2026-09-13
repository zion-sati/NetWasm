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
    IValueLayoutProviderFactory valueProviders) : ICompilationRootMapBuilder
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

    public ReachableProgram Analyze(
        MetadataCompilationSnapshot metadata,
        CompilationLayouts layouts,
        ReachableProgram program)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(layouts);
        ArgumentNullException.ThrowIfNull(program);
        var types = _typeRepositories.Create(metadata);
        var fields = _fieldRepositories.Create(metadata);
        var methods = _methodRepositories.Create(metadata);
        var typeDefinitions = _typeDefinitions.Create(metadata);
        var valueResolver = _valueResolvers.Create(
            typeDefinitions,
            fields,
            layouts.Snapshot.Target,
            layouts.Snapshot.TypeLayouts.ValueLayoutState);
        var values = _valueProviders.Create(valueResolver);
        ArgumentNullException.ThrowIfNull(types);
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(methods);
        var analyzer = _analyzers.Create(
            types,
            fields,
            methods,
            values,
            program.DispatchCallSites);
        var updated = program with
        {
            RootMaps = program.Methods.ToImmutableDictionary(
                pair => pair.Key,
                pair => analyzer.Analyze(new RootMapAnalysisRequest(
                    pair.Value,
                    program.AllocatingMethods,
                    program.ConstructedAllocatingMethods))),
            ConstructedRootMaps = program.ConstructedMethods.ToImmutableDictionary(
                pair => pair.Key,
                pair => analyzer.Analyze(new RootMapAnalysisRequest(
                    pair.Value,
                    program.AllocatingMethods,
                    program.ConstructedAllocatingMethods)),
                StringComparer.Ordinal),
        };
        _invariants.Validate(types, fields, methods, updated);
        return updated;
    }
}
