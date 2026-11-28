// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Diagnostics;
using NetWasm.Compiler.Metadata;
using NetWasm.Compiler.Analysis.ManagedCallSites;
using NetWasm.Compiler.Analysis.Delegates;

namespace NetWasm.Compiler.Analysis.Reachability;

internal sealed class RuntimeReachabilityExpander(
    ISharedObjectDispatchResolver sharedObjectDispatch,
    IObjectDispatchSharing objectDispatchSharing,
    IDelegateTypeRecognizer delegateTypes,
    IRuntimeIntrinsicTypeRootPlanner runtimeIntrinsicTypeRoots,
    ISharedArrayTypeOfResolver sharedArrayTypeOf,
    ITypeDefinitionResolver typeDefinitions,
    ITypeIdentityResolver typeIdentities,
    IObjectArrayDelegateAdapterPlanner objectArrayDelegateAdapters,
    IAnalysisStepRecorder steps) : IRuntimeReachabilityExpander
{
    private readonly ISharedObjectDispatchResolver _sharedObjectDispatch = sharedObjectDispatch ??
        throw new ArgumentNullException(nameof(sharedObjectDispatch));
    private readonly IObjectDispatchSharing _objectDispatchSharing = objectDispatchSharing ??
        throw new ArgumentNullException(nameof(objectDispatchSharing));
    private readonly IDelegateTypeRecognizer _delegateTypes = delegateTypes ??
        throw new ArgumentNullException(nameof(delegateTypes));
    private readonly IRuntimeIntrinsicTypeRootPlanner _runtimeIntrinsicTypeRoots = runtimeIntrinsicTypeRoots ??
        throw new ArgumentNullException(nameof(runtimeIntrinsicTypeRoots));
    private readonly ISharedArrayTypeOfResolver _sharedArrayTypeOf = sharedArrayTypeOf ??
        throw new ArgumentNullException(nameof(sharedArrayTypeOf));
    private readonly ITypeDefinitionResolver _typeDefinitions = typeDefinitions ??
        throw new ArgumentNullException(nameof(typeDefinitions));
    private readonly ITypeIdentityResolver _typeIdentities = typeIdentities ??
        throw new ArgumentNullException(nameof(typeIdentities));
    private readonly IObjectArrayDelegateAdapterPlanner _objectArrayDelegateAdapters = objectArrayDelegateAdapters ??
        throw new ArgumentNullException(nameof(objectArrayDelegateAdapters));
    private readonly IAnalysisStepRecorder _steps = steps ?? throw new ArgumentNullException(nameof(steps));

    public void Expand(ReachabilitySession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _steps.Record("shared object dispatch", () => AddSharedObjectDispatch(session));
        if (session.State.AllocatedTypes.Any(_delegateTypes.Recognize))
        {
            session.AddImplicit(ManagedExceptionKind.Argument, "System.ArgumentException");
            session.AddImplicit(ManagedExceptionKind.OutOfMemory, "System.OutOfMemoryException");
        }
        var intrinsicRoots = _steps.Record("intrinsic type root plan", () => _runtimeIntrinsicTypeRoots.Plan(
            session.State.MethodInstances.Values,
            session.State.Types.Select(_typeIdentities.GetTypeIdentity).Concat(session.State.ConstructedTypes),
            [.. session.TypeHandleSeeds],
            [.. session.State.AllocatedTypes]));
        _steps.Record("intrinsic type roots", () => AddIntrinsicRoots(session, intrinsicRoots));
        _steps.Record("object array delegate adapters", () => AddObjectArrayDelegateAdapters(session));
    }

    private void AddIntrinsicRoots(ReachabilitySession session, RuntimeIntrinsicTypeRootPlan intrinsicRoots)
    {
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
        if (intrinsicRoots.StructMarshallingConstructors)
        {
            foreach (var type in intrinsicRoots.StructMarshallingTypes)
            {
                session.AddStructureConstructor(type);
            }
        }
        foreach (var type in intrinsicRoots.AllocatedTypes)
        {
            session.AddAllocatedType(type);
        }
        // GetType of an object of a shared array type answers its element's array type, which CoreLib finds.
        var shared = intrinsicRoots.AllocatedTypes.Where(SharedArrayShapes.IsShared).ToArray();
        if (shared.Length != 0 && (session.State.SharedArrays is null || shared.Length != session.State.SharedArrays.Types.Length))
        {
            var typeOf = session.State.SharedArrays?.TypeOf ?? _sharedArrayTypeOf.Resolve();
            session.State.SharedArrays = new([.. shared.OrderBy(type => type.CanonicalName, StringComparer.Ordinal)], typeOf);
            session.VisitGenerated(CilOperation.Call, typeOf);
        }
    }

    private void AddObjectArrayDelegateAdapters(ReachabilitySession session)
    {
        foreach (var pair in _objectArrayDelegateAdapters.Plan(
                     session.State.MethodInstances.Values))
        {
            if (!session.State.ObjectArrayDelegateAdapters.TryAdd(pair.Key, pair.Value))
            {
                continue;
            }
            var plan = pair.Value;
            session.EnqueueGenerated(plan.Target);
            if (plan.IsSupported)
            {
                session.AddAllocatedType(plan.DelegateType);
                session.AddConstructedType(plan.DelegateType);
                session.AddCallable(plan.Target);
            }
        }
    }

    // A virtual call on object with as many targets as the preset shares at calls CoreLib's shared body for it,
    // which is reached here.
    private void AddSharedObjectDispatch(ReachabilitySession session)
    {
        var demanded = session.State.DispatchDeclarations
            .Where(pair => pair.Value.Operation == CilOperation.CallVirtual &&
                pair.Value.ReceiverBound is null &&
                !session.State.SharedObjectDispatch.ContainsKey(pair.Value.Declaration.CanonicalName) &&
                // An unbounded site reaches every target of its slot use.
                session.State.DispatchTargets.TryGetValue(
                    new DispatchSlot(pair.Value.Operation, pair.Value.Declaration.CanonicalName),
                    out var siteTargets) &&
                _objectDispatchSharing.Shares(siteTargets.Count))
            .Select(pair => pair.Value.Declaration)
            .DistinctBy(declaration => declaration.CanonicalName)
            .ToArray();
        foreach (var declaration in demanded)
        {
            if (_sharedObjectDispatch.Resolve(declaration) is { } helper)
            {
                session.State.SharedObjectDispatch.Add(declaration.CanonicalName, helper);
                session.VisitGenerated(CilOperation.Call, helper);
            }
        }
    }
}
