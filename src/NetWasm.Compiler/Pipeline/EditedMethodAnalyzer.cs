// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Interop;
using NetWasm.Compiler.Metadata;

using NetWasm.Compiler.ComponentModel.Optimization;
namespace NetWasm.Compiler.Pipeline;

internal interface IEditedMethodAnalyzer
{
    // Each method definition's own instance analyzed alone, as whole-program analysis
    // analyses it when no analysis crosses methods (O0's defaults), with its sites resolved
    // against the types the program allocates. Generic methods and methods of generic types
    // have no instance of their own and are left unanalyzed.
    EditedMethods Analyze(
        MetadataCompilationSnapshot metadata,
        CompilationPreparation preparation,
        IReadOnlyCollection<EntityKey> definitions,
        IReadOnlyCollection<CliTypeIdentity> allocatedTypes);
}

internal sealed record EditedMethods(
    ImmutableArray<ReachableMethodAnalysis> Methods,
    AnalyzedSites Sites,
    ImmutableArray<EntityKey> Unanalyzed);

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
            bodies.Create(metadata,
                preparation.OptimizationSettings.RuntimeAsyncSuspension == RuntimeAsyncSuspensionPolicy.WholeProgram),
            instances,
            methodImplementations.Create(metadata),
            formatter,
            intrinsics.Create(formatter, metadata.Methods),
            preparation.OptimizationSettings,
            preparation.World,
            preparation.InliningExcludedMethods);
        var analyses = ImmutableArray.CreateBuilder<ReachableMethodAnalysis>(definitions.Count);
        var unanalyzed = ImmutableArray.CreateBuilder<EntityKey>();
        foreach (var key in definitions)
        {
            var definition = methods.GetMethod(key);
            var instance = instances.ResolveMethodInstance(key.Assembly, key.MetadataToken, definition.Name, 0);
            if (instance.IsConstructed || instance.DeclaringType.ContainsGenericParameters ||
                !instance.MethodArguments.IsEmpty || definition.GenericArity != 0)
            {
                unanalyzed.Add(key);
                continue;
            }
            analyses.Add(analyzer.Analyzer.Analyze(new ReachableMethodRequest(instance)));
        }
        var analyzed = analyses.ToImmutable();
        return new(analyzed, analyzer.Sites.Plan(analyzed, allocatedTypes), unanalyzed.ToImmutable());
    }
}
