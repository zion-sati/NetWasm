using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.Core;

using NetWasm.Compiler.Core.IntermediateRepresentation.Calls;
using NetWasm.Compiler.Core.IntermediateRepresentation.Delegates;
using NetWasm.Compiler.Core.IntermediateRepresentation.Members;
using NetWasm.Compiler.Wasm.Emission.Methods;

namespace NetWasm.Compiler.Wasm.Emission.Planning;

internal sealed record WasmModuleTarget(
    WasmEmissionRequest Request,
    WasmModulePlan Plan,
    ModuleDataPlan ModuleData,
    ImmutableDictionary<(EntityKey Method, int ParameterIndex), HostCallbackDeclaration>
        HostCallbacks)
{
    public InstructionModuleTarget Instructions { get; } = new(
        Plan.FunctionIndices,
        Plan.InteropImports,
        ModuleData,
        Request.DispatchCallSites,
        Request.TypeTestSites,
        Request.CallableMethods,
        Request.DelegateTypes,
        HostCallbacks,
        Request.JavaScriptAsyncBindings,
        Plan.StackTraceMethods,
        Plan.RuntimeImportSelection,
        Plan.Finalization,
        Request.ManagedCallSites,
        Request.ObjectArrayDelegateAdapters,
        Request.MemberExecution,
        Request.CollectManagedMethodMemoryMetrics,
        Request.EnableAddressTakenAggregatePromotion)
    {
        NativeImports = Plan.NativeImports,
        NativeCallbacks = Plan.NativeCallbacks,
        ManagedByRefEffects = Request.ManagedByRefEffects,
        EnableBranchValueFramePlacement = Request.EnableBranchValueFramePlacement,
        EnableAggregateReferenceLanes = Request.EnableAggregateReferenceLanes,
        EnableArrayMetadataCaching = Request.EnableArrayMetadataCaching,
        HoistArrayNullChecks = Request.HoistArrayNullChecks,
        LoopRotation = Request.LoopRotation,
        EliminateArrayStoreChecks = Request.EliminateArrayStoreChecks,
        EnableScalarCallPreservation = Request.EnableScalarCallPreservation,
        InlineSmallBlockOperations = Request.InlineSmallBlockOperations,
        RootMaps = Request.Methods.Values.Select(method =>
                (ManagedMethodBodyKey.Resolve(method), Request.RootMaps[method.Header.Method.Key]))
            .Concat(Request.ConstructedMethods.Select(pair =>
                (ManagedMethodBodyKey.Resolve(pair.Value), Request.ConstructedRootMaps[pair.Key])))
            .ToImmutableDictionary(item => item.Item1, item => item.Item2, StringComparer.Ordinal),
    };
}

internal sealed record InstructionModuleTarget(
    FunctionIndexMap FunctionIndices,
    InteropImportPlan InteropImports,
    ModuleDataPlan ModuleData,
    IReadOnlyDictionary<string, DispatchCallSiteModel> DispatchCallSites,
    IReadOnlyDictionary<string, TypeTestSiteModel> TypeTestSites,
    IReadOnlyDictionary<string, MethodInstanceModel> CallableMethods,
    ImmutableArray<CliTypeIdentity> DelegateTypes,
    ImmutableDictionary<(EntityKey Method, int ParameterIndex), HostCallbackDeclaration>
        HostCallbacks,
    IReadOnlyDictionary<EntityKey, JavaScriptAsyncMethodBinding> JavaScriptAsyncBindings,
    StackTraceMethodPlan StackTraceMethods,
    RuntimeImportSelection RuntimeImportSelection,
    FinalizationPlan Finalization,
    ImmutableDictionary<ManagedCallSiteKey, ManagedCallSite> ManagedCallSites,
    ImmutableDictionary<string, ObjectArrayDelegateAdapterPlan>
        ObjectArrayDelegateAdapters,
    MemberExecutionPlan MemberExecution,
    bool CollectManagedMethodMemoryMetrics = false,
    bool EnableAddressTakenAggregatePromotion = false)
{
    public bool EnableBranchValueFramePlacement { get; init; }
    public bool EnableAggregateReferenceLanes { get; init; }
    public bool EnableArrayMetadataCaching { get; init; }
    public bool HoistArrayNullChecks { get; init; }
    public LoopRotationPolicy LoopRotation { get; init; }
    public bool EliminateArrayStoreChecks { get; init; }
    public bool EnableScalarCallPreservation { get; init; }
    public bool InlineSmallBlockOperations { get; init; }
    public NativeImportPlan NativeImports { get; init; } = NativeImportPlan.Empty;
    public NativeCallbackPlan NativeCallbacks { get; init; } = NativeCallbackPlan.Empty;
    public IReadOnlyDictionary<string, MethodRootMap> RootMaps { get; init; } =
        ImmutableDictionary<string, MethodRootMap>.Empty;
    public ManagedByRefEffects ManagedByRefEffects { get; init; } =
        ManagedByRefEffects.Empty;
}

internal interface IWasmModuleTargetFactory
{
    WasmModuleTarget Create(WasmEmissionRequest request);
}

internal sealed class WasmModuleTargetFactory(
IWasmModulePlanner planner,
IModuleDataPlanner moduleDataPlanner,
IStructuredMethodEmissionPlanner methodEmissions,
IStaticInitializerFunctionPlanner staticInitializers,
IJSImportForwarderPlanner jsImportForwarders) : IWasmModuleTargetFactory
{
    public WasmModuleTarget Create(WasmEmissionRequest request)
    {
        var plannedMethods = methodEmissions.Plan(request);
        var plan = jsImportForwarders.Plan(request, planner.Build(request, plannedMethods));
        var moduleData = staticInitializers.Build(request, plan, moduleDataPlanner.Build(
            plannedMethods,
            request.StaticInitializers,
            request.ConstructedStaticInitializers,
            request.NativeCallbacks.Count != 0,
            plan.StackTraceMethods,
            reserveStateRegistrationGuard: request.HasReenterableHostEntries));
        return new(
            request,
            plan,
            moduleData,
            request.HostCallbacks.ToImmutableDictionary(
                callback => (callback.ImportMethod, callback.ParameterIndex)));
    }
}
