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

internal sealed class ReachableMethodApplier(
    ITypeIdentityResolver typeIdentities) : IReachableMethodApplier
{
    private readonly ITypeIdentityResolver _typeIdentities = typeIdentities ??
        throw new ArgumentNullException(nameof(typeIdentities));

    public void Apply(ReachabilitySession session, ReachableMethodAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(analysis);
        foreach (var requirement in analysis.Exceptions)
        {
            session.AddImplicit(requirement.Kind, requirement.TypeName);
        }
        foreach (var catchType in analysis.CatchTypes)
        {
            session.State.Types.Add(catchType);
        }
        foreach (var region in analysis.Body.Body.ExceptionRegions)
        {
            if (region.CatchTypeIdentity is not { } identity)
                continue;
            if (identity.ContainsGenericParameters)
                throw new CompilerException(new CompilerDiagnostic(
                    DiagnosticCode.UnsupportedMetadata, "An executable catch type must be closed."));
            session.AddRuntimeType(identity);
            session.AddConstructedType(identity);
        }
        session.State.Contributions[analysis.Method.CanonicalName] = new(analysis.Method.Definition.Key, analysis.Contribution);
        if (analysis.Method.IsConstructed)
        {
            session.State.ConstructedMethods.Add(analysis.Method.CanonicalName, analysis.Body);
        }
        else
        {
            session.State.Methods.Add(analysis.Method.Definition.Key, analysis.Body);
        }
        ApplyInstructions(session, analysis.Instructions);
        session.State.RequiredTypes.UnionWith(analysis.RequiredTypes);
    }

    private void ApplyInstructions(ReachabilitySession session, ReachabilityInstructionAnalysis analysis)
    {
        session.ArrayElementSeeds.UnionWith(analysis.RuntimeTypes);
        session.TypeHandleSeeds.UnionWith(analysis.RuntimeTypes);
        session.ArrayElementSeeds.UnionWith(analysis.ConstructedTypes);
        session.ArrayElementSeeds.UnionWith(analysis.AllocatedTypes);
        session.ArrayElementSeeds.UnionWith(analysis.Types.Select(_typeIdentities.GetTypeIdentity));
        session.State.RequiresTypeFacts |= analysis.RequiresTypeFacts;
        session.State.RequiresGenericArguments |= analysis.RequiresGenericArguments;
        session.State.RequiresDeclaringTypes |= analysis.RequiresDeclaringTypes;
        session.State.RequiresGenericDefinitions |= analysis.RequiresGenericDefinitions;
        session.State.RequiresAssemblies |= analysis.RequiresAssemblies;
        session.State.RequiresElementTypes |= analysis.RequiresElementTypes;
        session.State.RequiresTypeAttributes |= analysis.RequiresTypeAttributes;
        session.State.RequiresInterfaces |= analysis.RequiresInterfaces;
        session.State.RequiresMemberNames |= analysis.RequiresMemberNames;
        foreach (var type in analysis.RuntimeTypes)
        {
            session.AddRuntimeType(type);
        }
        foreach (var type in analysis.ConstructedTypes)
        {
            session.AddConstructedType(type, retainElementModifierIdentity: true);
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
        foreach (var literal in analysis.Strings)
        {
            session.State.Strings.Add(literal);
        }
        foreach (var method in analysis.Methods)
        {
            session.VisitMethod(method.Operation, method.Method);
        }
        foreach (var entity in analysis.Entities)
        {
            session.VisitEntity(entity.Operation, entity.Entity);
        }
        foreach (var field in analysis.Fields)
        {
            session.VisitField(field);
        }
        foreach (var method in analysis.MethodDescriptors)
        {
            session.AddMethodDescriptor(method);
        }
        foreach (var field in analysis.FieldDescriptors)
        {
            session.AddFieldDescriptor(field);
        }
        foreach (var dispatch in analysis.Dispatches)
        {
            session.AddDispatch(dispatch.Key, dispatch.Declaration);
        }
        foreach (var callable in analysis.CallableMethods)
        {
            session.AddCallable(callable);
        }
        foreach (var callback in analysis.NativeCallbacks)
        {
            session.State.NativeCallbacks.TryAdd(callback.CanonicalName, callback);
        }
    }
}
