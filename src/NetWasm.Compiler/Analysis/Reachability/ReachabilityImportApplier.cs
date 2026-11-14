using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Metadata;
using NetWasm.Compiler.Analysis.ManagedCallSites;
using NetWasm.Compiler.Analysis.Delegates;

namespace NetWasm.Compiler.Analysis.Reachability;

internal sealed class ReachabilityImportApplier(
    ITypeIdentityResolver typeIdentities) : IReachabilityImportApplier
{
    private readonly ITypeIdentityResolver _typeIdentities = typeIdentities ??
        throw new ArgumentNullException(nameof(typeIdentities));

    public void Apply(ReachabilitySession session, ReachabilityImportAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(analysis);
        session.State.EnumMetadataRequirements.UnionWith(analysis.EnumMetadataRequirements);
        foreach (var type in analysis.ConstructedTypes)
        {
            session.AddConstructedType(type);
        }
        foreach (var type in analysis.AllocatedTypes)
        {
            session.AddAllocatedType(type);
        }
        foreach (var type in analysis.Types)
        {
            session.State.Types.Add(type);
            session.State.TypeDependencyPaths.TryAdd(_typeIdentities.GetTypeIdentity(type).CanonicalName,
                session.CurrentDependencyPath);
        }
        foreach (var field in analysis.Fields)
        {
            session.VisitField(field);
        }
        foreach (var method in analysis.EnqueuedMethods)
        {
            session.EnqueueInstance(method);
        }
        foreach (var requirement in analysis.Exceptions)
        {
            session.AddImplicit(requirement.Kind, requirement.TypeName);
        }
        if (analysis.JavaScriptImport is not null)
        {
            session.State.JavaScriptImports.TryAdd(
                analysis.JavaScriptImport.Key,
                analysis.JavaScriptImport);
        }
        if (analysis.WitImport is not null)
        {
            session.State.WitImports.TryAdd(analysis.WitImport.Key, analysis.WitImport);
        }
        foreach (var callback in analysis.HostCallbacks)
        {
            session.State.HostCallbacks.Add(callback);
        }
        if (analysis.JavaScriptAsyncBinding is not null)
        {
            session.State.JavaScriptAsyncBindings.TryAdd(
                analysis.JavaScriptAsyncBinding.Method,
                analysis.JavaScriptAsyncBinding);
        }
        foreach (var (name, conversion) in analysis.Conversions)
        {
            session.State.JavaScriptConversions.TryAdd(name, conversion);
        }
    }
}
