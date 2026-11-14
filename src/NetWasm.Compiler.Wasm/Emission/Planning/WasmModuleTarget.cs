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
    // The build ID that binds the module to its symbols sidecars.
    public string BuildId { get; init; } = "";

    public InstructionModuleTarget Instructions { get; } = new(
        Plan.FunctionIndices,
        Plan.InteropImports,
        ModuleData,
        Lookups.Of(Request.DispatchCallSites),
        Lookups.Of(Request.TypeTestSites),
        Lookups.Whole(Request.CallableMethods),
        Lookups.Whole<IReadOnlyList<CliTypeIdentity>>(Request.DelegateTypes),
        HostCallbacks,
        Request.JavaScriptAsyncBindings,
        Plan.StackTraceMethods,
        Plan.RuntimeImportSelection,
        Plan.Finalization,
        Lookups.Of(Request.ManagedCallSites),
        Request.ObjectArrayDelegateAdapters,
        Request.MemberExecution,
        Request.CollectManagedMethodMemoryMetrics,
        Request.EnableAddressTakenAggregatePromotion)
    {
        SharedObjectDispatch = Request.SharedObjectDispatch,
        NativeImports = Plan.NativeImports,
        NativeCallbacks = Plan.NativeCallbacks,
        ManagedByRefEffects = Lookups.Of(Request.ManagedByRefEffects.Methods),
        FinalizableTypeIds = Lookups.Of(Plan.Finalization.FinalizableTypeIds),
        StackTraceLookups = StackTraceMethodLookups.Of(Plan.StackTraceMethods),
        StaticInitializerGuards = Lookups.Of(ModuleData.StaticInitializerGuards),
        ExceptionMetadata = Lookups.Of(ModuleData.ExceptionMetadata),
        EnableBranchValueFramePlacement = Request.EnableBranchValueFramePlacement,
        EnableAggregateReferenceLanes = Request.EnableAggregateReferenceLanes,
        EnableArrayMetadataCaching = Request.EnableArrayMetadataCaching,
        HoistArrayNullChecks = Request.HoistArrayNullChecks,
        LoopRotation = Request.LoopRotation,
        EliminateArrayStoreChecks = Request.EliminateArrayStoreChecks,
        EnableScalarCallPreservation = Request.EnableScalarCallPreservation,
        InlineSmallBlockOperations = Request.InlineSmallBlockOperations,
        InlineArrayData = Request.InlineArrayData,
        InlineObjectAllocation = Request.InlineObjectAllocation,
        RootMaps = Lookups.Of(Request.Methods.Values.Select(method =>
                (ManagedMethodBodyKey.Resolve(method), Request.RootMaps[method.Header.Method.Key]))
            .Concat(Request.ConstructedMethods.Select(pair =>
                (ManagedMethodBodyKey.Resolve(pair.Value), Request.ConstructedRootMaps[pair.Key])))
            .ToImmutableDictionary(item => item.Item1, item => item.Item2, StringComparer.Ordinal)),
    };
}

internal sealed record InstructionModuleTarget(
    FunctionIndexMap FunctionIndices,
    InteropImportPlan InteropImports,
    ModuleDataPlan ModuleData,
    IEntryLookup<string, DispatchCallSiteModel> DispatchCallSites,
    IEntryLookup<string, TypeTestSiteModel> TypeTestSites,
    IWholeRead<IReadOnlyDictionary<string, MethodInstanceModel>> CallableMethods,
    IWholeRead<IReadOnlyList<CliTypeIdentity>> DelegateTypes,
    ImmutableDictionary<(EntityKey Method, int ParameterIndex), HostCallbackDeclaration>
        HostCallbacks,
    IReadOnlyDictionary<EntityKey, JavaScriptAsyncMethodBinding> JavaScriptAsyncBindings,
    StackTraceMethodPlan StackTraceMethods,
    RuntimeImportSelection RuntimeImportSelection,
    FinalizationPlan Finalization,
    IEntryLookup<ManagedCallSiteKey, ManagedCallSite> ManagedCallSites,
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
    public bool InlineArrayData { get; init; }
    public bool InlineObjectAllocation { get; init; }
    public ObjectAllocationPolicy ObjectAllocation => new(RuntimeImportSelection, InlineObjectAllocation);
    public NativeImportPlan NativeImports { get; init; } = NativeImportPlan.Empty;
    // The shared body each of object's virtual calls uses at its many-target sites, by declaration. A site's use
    // of it follows from its own target count (SharedDispatchPolicy), which a unit's answers record.
    public IReadOnlyDictionary<string, MethodInstanceModel> SharedObjectDispatch { get; init; } =
        ImmutableDictionary<string, MethodInstanceModel>.Empty;
    public NativeCallbackPlan NativeCallbacks { get; init; } = NativeCallbackPlan.Empty;
    public IEntryLookup<string, MethodRootMap> RootMaps { get; init; } = Lookups.NoEntries<string, MethodRootMap>();
    public IEntryLookup<string, ManagedByRefMethodEffects> ManagedByRefEffects { get; init; } =
        Lookups.NoEntries<string, ManagedByRefMethodEffects>();

    // What a method's emission looks up in the module's plans, one entry at a time; the
    // plans themselves serve the module's own emission.
    public IMembership<int> FinalizableTypeIds { get; init; } = Lookups.NoItems<int>();
    public StackTraceMethodLookups StackTraceLookups { get; init; } = StackTraceMethodLookups.None;
    public IEntryLookup<string, StaticInitializerGuard> StaticInitializerGuards { get; init; } =
        Lookups.NoEntries<string, StaticInitializerGuard>();
    public IEntryLookup<StructuredExceptionGroupKey, ExceptionGroupMetadata> ExceptionMetadata { get; init; } =
        Lookups.NoEntries<StructuredExceptionGroupKey, ExceptionGroupMetadata>();
}

internal interface IWasmModuleTargetFactory
{
    WasmModuleTarget Create(WasmEmissionRequest request);
}

internal sealed class WasmModuleTargetFactory(
IWasmModulePlanner planner,
IModuleDataPlanner moduleDataPlanner,
ISymbolBuildIdCalculator buildIds,
IStructuredMethodEmissionPlanner methodEmissions,
IStaticInitializerFunctionPlanner staticInitializers,
IJSImportForwarderPlanner jsImportForwarders) : IWasmModuleTargetFactory
{
    public WasmModuleTarget Create(WasmEmissionRequest request)
    {
        var plannedMethods = methodEmissions.Plan(request);
        var plan = jsImportForwarders.Plan(request, planner.Build(request, plannedMethods));
        var buildId = buildIds.Calculate(
            request.Sidecars?.TypeSymbolsDigest ?? [],
            plan.StackTraceMethods.Symbols);
        var moduleData = staticInitializers.Build(request, plan, moduleDataPlanner.Build(
            plannedMethods,
            request.StaticInitializers,
            request.ConstructedStaticInitializers,
            request.NativeCallbacks.Count != 0,
            request.Sidecars is { DescriptorSlotAddress: int slot } sidecars
                ? new SidecarDescriptor(
                    slot,
                    (sidecars.LoadTypeSymbols ? SidecarDescriptor.TypeSymbolsFlag : 0) |
                    (plan.StackTraceMethods.Symbols.IsEmpty ? 0 : SidecarDescriptor.MethodSymbolsFlag),
                    buildId,
                    sidecars.ProgramFileName)
                : null,
            reserveStateRegistrationGuard: request.HasReenterableHostEntries));
        return new(
            request,
            plan,
            moduleData,
            request.HostCallbacks.ToImmutableDictionary(
                callback => (callback.ImportMethod, callback.ParameterIndex)))
        {
            BuildId = buildId,
        };
    }
}
