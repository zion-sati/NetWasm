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

internal sealed class RuntimeReachabilityExpander(
    ISharedObjectDispatchResolver sharedObjectDispatch,
    IDelegateTypeRecognizer delegateTypes,
    IRuntimeIntrinsicTypeRootPlanner runtimeIntrinsicTypeRoots,
    ITypeDefinitionResolver typeDefinitions,
    ITypeIdentityResolver typeIdentities,
    IObjectArrayDelegateAdapterPlanner objectArrayDelegateAdapters) : IRuntimeReachabilityExpander
{
    private readonly ISharedObjectDispatchResolver _sharedObjectDispatch = sharedObjectDispatch ??
        throw new ArgumentNullException(nameof(sharedObjectDispatch));
    private readonly IDelegateTypeRecognizer _delegateTypes = delegateTypes ??
        throw new ArgumentNullException(nameof(delegateTypes));
    private readonly IRuntimeIntrinsicTypeRootPlanner _runtimeIntrinsicTypeRoots = runtimeIntrinsicTypeRoots ??
        throw new ArgumentNullException(nameof(runtimeIntrinsicTypeRoots));
    private readonly ITypeDefinitionResolver _typeDefinitions = typeDefinitions ??
        throw new ArgumentNullException(nameof(typeDefinitions));
    private readonly ITypeIdentityResolver _typeIdentities = typeIdentities ??
        throw new ArgumentNullException(nameof(typeIdentities));
    private readonly IObjectArrayDelegateAdapterPlanner _objectArrayDelegateAdapters = objectArrayDelegateAdapters ??
        throw new ArgumentNullException(nameof(objectArrayDelegateAdapters));

    public void Expand(ReachabilitySession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        AddSharedObjectDispatch(session);
        if (session.State.AllocatedTypes.Any(_delegateTypes.Recognize))
        {
            session.AddImplicit(ManagedExceptionKind.Argument, "System.ArgumentException");
            session.AddImplicit(ManagedExceptionKind.OutOfMemory, "System.OutOfMemoryException");
        }
        var intrinsicRoots = _runtimeIntrinsicTypeRoots.Plan(
            session.State.MethodInstances.Values,
            session.State.Types.Select(_typeIdentities.GetTypeIdentity).Concat(session.State.ConstructedTypes),
            [.. session.ArrayElementSeeds],
            [.. session.TypeHandleSeeds],
            [.. session.State.AllocatedTypes]);
        foreach (var type in intrinsicRoots.RuntimeTypes)
        {
            session.AddRuntimeType(type);
            session.AddConstructedType(type);
        }
        foreach (var type in intrinsicRoots.InitializedTypes)
        {
            session.EnsureTypeInitialized(_typeDefinitions.ResolveTypeIdentity(type).Key, type);
        }
        // GetUninitializedObject allocates these without a constructor; delegates are
        // rejected before allocation.
        foreach (var type in intrinsicRoots.UninitializedObjectTypes.Where(type => !_delegateTypes.Recognize(type)))
        {
            session.AddRuntimeType(type);
            session.AddConstructedType(type);
            session.AddAllocatedType(type);
            session.State.UninitializedObjectTypes.Add(type);
        }
        session.State.StructMarshallingTypes.UnionWith(intrinsicRoots.StructMarshallingTypes);
        session.State.StructMarshallingFieldNames |= intrinsicRoots.StructMarshallingFieldNames;
        foreach (var type in intrinsicRoots.AllocatedTypes)
        {
            session.AddAllocatedType(type);
        }
        foreach (var pair in _objectArrayDelegateAdapters.Plan(
                     session.State.MethodInstances.Values))
        {
            if (!session.State.ObjectArrayDelegateAdapters.TryAdd(pair.Key, pair.Value))
            {
                continue;
            }
            var plan = pair.Value;
            session.EnqueueInstance(plan.Target);
            if (plan.IsSupported)
            {
                session.AddAllocatedType(plan.DelegateType);
                session.AddConstructedType(plan.DelegateType);
                session.AddCallable(plan.Target);
            }
        }
    }

    // A virtual call on object with many targets calls CoreLib's shared body for it, which is reached here.
    private void AddSharedObjectDispatch(ReachabilitySession session)
    {
        var demanded = session.State.DispatchDeclarations
            .Where(pair => pair.Value.Operation == CilOperation.CallVirtual &&
                pair.Value.ReceiverBound is null &&
                !session.State.SharedObjectDispatch.ContainsKey(pair.Value.Declaration.CanonicalName) &&
                session.State.DispatchTargets.TryGetValue(pair.Key, out var siteTargets) &&
                siteTargets.Count >= SharedDispatchPolicy.MinimumTargets)
            .Select(pair => pair.Value.Declaration)
            .DistinctBy(declaration => declaration.CanonicalName)
            .ToArray();
        foreach (var declaration in demanded)
        {
            if (_sharedObjectDispatch.Resolve(declaration) is { } helper)
            {
                session.State.SharedObjectDispatch.Add(declaration.CanonicalName, helper);
                session.VisitMethod(CilOperation.Call, helper);
            }
        }
    }
}
