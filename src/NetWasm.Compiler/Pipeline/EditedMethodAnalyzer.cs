using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Interop;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Pipeline;

internal interface IEditedMethodAnalyzer
{
    // Each method definition's own instance analysed alone, as whole-program analysis
    // analyses it when no analysis crosses methods (O0's defaults), with its sites resolved
    // against the types the program allocates. Generic methods and methods of generic types
    // have no instance of their own and are left unanalysed.
    EditedMethods Analyze(
        MetadataCompilationSnapshot metadata,
        CompilationPreparation preparation,
        IReadOnlyCollection<EntityKey> definitions,
        IReadOnlyCollection<CliTypeIdentity> allocatedTypes);
}

internal sealed record EditedMethods(
    ImmutableArray<ReachableMethodAnalysis> Methods,
    AnalysedSites Sites,
    ImmutableArray<EntityKey> Unanalysed);

internal sealed class EditedMethodAnalyzer(
    IStandaloneMethodAnalyzerFactory analyzers,
    IRuntimeIntrinsicRegistryFactory intrinsics,
    ITypeRepositoryFactory typeRepositories,
    IFieldRepositoryFactory fieldRepositories,
    IMethodRepositoryFactory methodRepositories,
    ITypeFinderFactory typeFinders,
    ITypeDefinitionResolverFactory typeDefinitions,
    ITypeIdentityResolverFactory identities,
    IBaseTypeIdentityResolverFactory baseTypeIdentities,
    IImplementedInterfaceResolverFactory implementedInterfaces,
    IMethodBodyReaderFactory bodies,
    IMethodInstanceResolverFactory methodInstances,
    IMethodImplementationResolverFactory methodImplementations,
    ISymbolFormatterFactory symbols) : IEditedMethodAnalyzer
{
    public EditedMethods Analyze(
        MetadataCompilationSnapshot metadata,
        CompilationPreparation preparation,
        IReadOnlyCollection<EntityKey> definitions,
        IReadOnlyCollection<CliTypeIdentity> allocatedTypes)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(preparation);
        ArgumentNullException.ThrowIfNull(definitions);
        var methods = methodRepositories.Create(metadata);
        var instances = methodInstances.Create(metadata);
        var formatter = symbols.Create(metadata);
        var analyzer = analyzers.Create(
            metadata,
            typeRepositories.Create(metadata),
            fieldRepositories.Create(metadata),
            methods,
            typeFinders.Create(metadata),
            typeDefinitions.Create(metadata),
            identities.Create(metadata),
            baseTypeIdentities.Create(metadata),
            implementedInterfaces.Create(metadata),
            bodies.Create(metadata),
            instances,
            methodImplementations.Create(metadata),
            formatter,
            intrinsics.Create(formatter, metadata.Methods),
            preparation.OptimizationSettings,
            PlatformWorldCapabilities.From(preparation.ComponentContract));
        var analyses = ImmutableArray.CreateBuilder<ReachableMethodAnalysis>(definitions.Count);
        var unanalysed = ImmutableArray.CreateBuilder<EntityKey>();
        foreach (var key in definitions)
        {
            var definition = methods.GetMethod(key);
            var instance = instances.ResolveMethodInstance(key.Assembly, key.MetadataToken, definition.Name, 0);
            if (instance.IsConstructed || instance.DeclaringType.ContainsGenericParameters ||
                !instance.MethodArguments.IsEmpty || definition.GenericArity != 0)
            {
                unanalysed.Add(key);
                continue;
            }
            analyses.Add(analyzer.Analyzer.Analyze(new ReachableMethodRequest(instance)));
        }
        var analysed = analyses.ToImmutable();
        return new(analysed, analyzer.Sites.Plan(analysed, allocatedTypes), unanalysed.ToImmutable());
    }
}
