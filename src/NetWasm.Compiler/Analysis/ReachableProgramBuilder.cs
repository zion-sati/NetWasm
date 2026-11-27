// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler;
using NetWasm.Compiler.Analysis.Delegates;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Calls;
using NetWasm.Compiler.GarbageCollection;
using NetWasm.Compiler.Diagnostics;

namespace NetWasm.Compiler.Analysis;

internal sealed class ReachableProgramBuilder(
    IAllocationCapabilityAnalyzer allocationCapabilities,
    IManagedByRefEffectAnalyzer byRefEffects,
    IDelegateBindingPlanner delegateBindings,
    IObjectDispatchSharing objectDispatchSharing,
    IDispatchSiteTargetSelector siteTargets,
    IAnalysisStepRecorder steps) :
    IReachableProgramBuilder
{
    private readonly IAllocationCapabilityAnalyzer _allocationCapabilities = allocationCapabilities ??
        throw new ArgumentNullException(nameof(allocationCapabilities));
    private readonly IManagedByRefEffectAnalyzer _byRefEffects = byRefEffects ??
        throw new ArgumentNullException(nameof(byRefEffects));
    private readonly IDelegateBindingPlanner _delegateBindings = delegateBindings ??
        throw new ArgumentNullException(nameof(delegateBindings));
    private readonly IObjectDispatchSharing _objectDispatchSharing = objectDispatchSharing ??
        throw new ArgumentNullException(nameof(objectDispatchSharing));
    private readonly IDispatchSiteTargetSelector _siteTargets = siteTargets ??
        throw new ArgumentNullException(nameof(siteTargets));
    private readonly IAnalysisStepRecorder _steps = steps ?? throw new ArgumentNullException(nameof(steps));

    public ReachableProgram Build(
        MethodDefinitionModel? entryPoint,
        ImmutableArray<ProgramExport> exports,
        ReachabilityLedgerSnapshot state,
        IDelegateTypeRecognizer delegateTypes,
        ITypeTestPlanner typeTestPlanner)
    {
        var dispatchCallSites = _steps.Record("dispatch call sites", () => DispatchCallSites(state));
        var methods = state.Methods;
        var constructedMethods = state.ConstructedMethods;
        var typeTestSites = _steps.Record("type test sites", () => typeTestPlanner.Build(
            methods.Values.Concat(constructedMethods.Values),
            state.AllocatedTypes));

        var capabilities = _steps.Record("allocation capabilities", () => _allocationCapabilities.Analyze(
            new AllocationCapabilityAnalysisRequest(
                methods,
                constructedMethods,
                dispatchCallSites)));
        var managedByRefEffects = _steps.Record("by-ref effects", () => _byRefEffects.Analyze(
            new ManagedByRefEffectAnalysisRequest(
                methods,
                constructedMethods,
                dispatchCallSites)));
        var allocatingMethods = capabilities.Methods;
        var constructedAllocatingMethods =
            capabilities.ConstructedMethods;
        var (rootMaps, constructedRootMaps) = _steps.Record("root maps", () => (
            methods.ToImmutableDictionary(
                pair => pair.Key,
                pair => EmptyRootMap(pair.Key)),
            constructedMethods.ToImmutableDictionary(
                pair => pair.Key,
                pair => EmptyRootMap(pair.Value.Method.Definition.Key),
                StringComparer.Ordinal)));
        // Every delegate type whose Invoke is called, directly or not, or whose address a delegate
        // binds, has an invoke helper.
        var delegateInvokeMethods = state.ManagedCallSites.Values
            .Where(site =>
                site.Target.Definition.Name == "Invoke" &&
                delegateTypes.Recognize(site.Target.DeclaringType))
            .Select(site => site.Target)
            .Concat(state.HostCallbacks.Select(callback => callback.Invoke))
            .Concat(state.MemberExecution.DelegateInvocation?.Methods.Values ?? []);
        var plannedDelegateBindings = _steps.Record("delegate bindings", () => _delegateBindings.Plan(
            delegateInvokeMethods,
            state.CallableMethods.Values));

        return _steps.Record("program model", () => new ReachableProgram(
            entryPoint,
            methods,
            state.Types,
            state.Fields,
            [
                ..state.StaticInitializers
                    .OrderBy(key => key.Assembly.Name, StringComparer.Ordinal)
                    .ThenBy(key => key.MetadataToken)
            ],
            state.Strings,
            exports,
            allocatingMethods,
            rootMaps,
            // A finalizer can be discovered first through another path (a dispatch slot or a base call) as an equal but
            // separately built model. The program's finalizer is the instance it compiles.
            state.Finalizers.ToImmutableDictionary(
                pair => pair.Key,
                pair => state.MethodInstances.TryGetValue(pair.Value.CanonicalName, out var instance)
                    ? instance
                    : pair.Value),
            state.ImplicitExceptions)
        {
            TypeDependencyPaths = state.TypeDependencyPaths,
            EnumMetadataRequirements = state.EnumMetadataRequirements,
            UninitializedObjectTypes = [.. state.UninitializedObjectTypes
                .OrderBy(type => type.CanonicalName, StringComparer.Ordinal)],
            StructMarshallingTypes = [.. state.StructMarshallingTypes
                .OrderBy(type => type.CanonicalName, StringComparer.Ordinal)],
            StructMarshallingFieldNames = state.StructMarshallingFieldNames,
            StructMarshallingConstructors = state.StructMarshallingConstructors,
            MethodContributions = state.Contributions,
            ModuleInitializers = [
                .. state.ModuleInitializers
                    .OrderBy(key => key.Assembly.Name, StringComparer.Ordinal)
                    .ThenBy(key => key.MetadataToken)
            ],
            ConstructedMethods = constructedMethods,
            MethodInstances = state.MethodInstances,
            ConstructedTypes = state.ConstructedTypes,
            RuntimeTypeDefinitions = state.RuntimeTypeDefinitions,
            RuntimeTypes = state.RuntimeTypes,
            ConstructedAllocatingMethods = constructedAllocatingMethods,
            ConstructedRootMaps = constructedRootMaps,
            ConstructedFields = state.ConstructedFields,
            MethodDescriptors = state.MethodDescriptors,
            FieldDescriptors = state.FieldDescriptors,
            PropertyDescriptors = state.PropertyDescriptors,
            ObjectArrayDelegateAdapters = state.ObjectArrayDelegateAdapters,
            MemberExecution = state.MemberExecution,
            NamedMemberDescriptors = state.NamedMemberDescriptors,
            RequiresTypeFacts = state.RequiresTypeFacts,
            RequiresGenericArguments = state.RequiresGenericArguments,
            RequiresDeclaringTypes = state.RequiresDeclaringTypes,
            RequiresGenericDefinitions = state.RequiresGenericDefinitions,
            RequiresAssemblies = state.RequiresAssemblies,
            RequiresElementTypes = state.RequiresElementTypes,
            RequiresTypeAttributes = state.RequiresTypeAttributes,
            RequiresInterfaces = state.RequiresInterfaces,
            RequiresManifestResources = state.RequiresManifestResources,
            RequiresAssemblyNames = state.RequiresAssemblyNames,
            RequiresAssemblyEnumeration = state.RequiresAssemblyEnumeration,
            ConstructedStaticInitializers = [
                ..state.ConstructedStaticInitializers
                    .Order(StringComparer.Ordinal)
            ],
            DispatchCallSites = dispatchCallSites,
            SharedObjectDispatch = state.SharedObjectDispatch,
            SharedArrays = state.SharedArrays,
            TypeTestSites = typeTestSites,
            CallableMethods = state.CallableMethods,
            GeneratedCallees = state.GeneratedCallees,
            NativeCallbacks = state.NativeCallbacks,
            JSImportMethods = [.. state.JavaScriptImports.Values
                .OrderBy(method => method.JSImport!.ModuleName ?? RuntimeAbi.HostModule,
                    StringComparer.Ordinal)
                .ThenBy(method => method.JSImport!.FunctionName, StringComparer.Ordinal)],
            WitImportMethods = [.. state.WitImports.Values
                .OrderBy(method => method.WitImport!.InterfaceName, StringComparer.Ordinal)
                .ThenBy(method => method.WitImport!.FunctionName, StringComparer.Ordinal)],
            HostCallbacks = [.. state.HostCallbacks
                .OrderBy(callback => callback.ExportName, StringComparer.Ordinal)],
            JavaScriptAsyncBindings = state.JavaScriptAsyncBindings,
            JavaScriptConversions = state.JavaScriptConversions,
            ProducesJavaScriptErrors = state.ProducesJavaScriptErrors,
            ManagedCallSites = state.ManagedCallSites,
            DelegateBindings = plannedDelegateBindings,
            ManagedByRefEffects = managedByRefEffects,
            DelegateTypes = [.. state.AllocatedTypes
                .Where(delegateTypes.Recognize)
                .OrderBy(type => type.CanonicalName, StringComparer.Ordinal)],
            AllocatedTypes = [.. state.AllocatedTypes.OrderBy(type => type.CanonicalName, StringComparer.Ordinal)],
        });
    }

    // Each site with its slot use's targets that its bound admits: one array for every site of the same slot use and
    // bound.
    private ImmutableDictionary<string, DispatchCallSiteModel> DispatchCallSites(ReachabilityLedgerSnapshot state)
    {
        var selected =
            new Dictionary<(DispatchSlot Slot, CliTypeIdentity? Bound), ImmutableArray<DispatchTargetModel>>();
        var sites = ImmutableDictionary.CreateBuilder<string, DispatchCallSiteModel>(StringComparer.Ordinal);
        foreach (var (key, declaration) in state.DispatchDeclarations)
        {
            var slot = new DispatchSlot(declaration.Operation, declaration.Declaration.CanonicalName);
            if (!selected.TryGetValue((slot, declaration.ReceiverBound), out var targets))
            {
                selected.Add((slot, declaration.ReceiverBound), targets = _siteTargets.Select(
                    declaration.ReceiverBound,
                    state.DispatchTargets.GetValueOrDefault(slot, [])));
            }
            // A call dispatched by receiver flow has the targets of the receivers its flow admitted, not every target its
            // slot use has for other calls; with no bound among them, they are exactly what reached it.
            var admission = state.ReceiverFlowAdmissions.GetValueOrDefault(key);
            if (admission is not null)
                targets = [.. targets.Where(target => admission.Receivers.Contains(target.ReceiverType))];
            sites.Add(key, new DispatchCallSiteModel(declaration.Caller, declaration.IlOffset, declaration.Declaration,
                targets)
            {
                SharesObjectDispatch = SharesObjectDispatch(declaration, targets, state),
                PreciseTargets = admission is { Bounded: false },
            });
        }
        return sites.ToImmutable();
    }

    private static MethodRootMap EmptyRootMap(EntityKey method) => new(
        method,
        ImmutableDictionary<RootSource, int>.Empty,
        ImmutableDictionary<int, SafepointRootMap>.Empty);

    // An unbounded virtual call on object with as many targets as the preset shares at uses the shared body the
    // analysis reached for it; the shared body's own call dispatches.
    private bool SharesObjectDispatch(
        DispatchDeclaration declaration,
        ImmutableArray<DispatchTargetModel> targets,
        ReachabilityLedgerSnapshot state) =>
        declaration.Operation == CilOperation.CallVirtual &&
        declaration.ReceiverBound is null &&
        !targets.IsDefault && _objectDispatchSharing.Shares(targets.Length) &&
        state.SharedObjectDispatch.TryGetValue(declaration.Declaration.CanonicalName, out var shared) &&
        declaration.Caller != shared.CanonicalName;
}
