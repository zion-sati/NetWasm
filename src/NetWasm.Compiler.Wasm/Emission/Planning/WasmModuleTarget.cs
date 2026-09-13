using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

using NetWasm.Compiler.Core.IntermediateRepresentation.Calls;

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
        Plan.DelegateRemoveHelperIndex,
        Plan.DelegateEqualityHelperIndex,
        Request.ManagedCallSites);
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
    OptionalFunctionIndex DelegateRemoveHelperIndex,
    OptionalFunctionIndex DelegateEqualityHelperIndex,
    ImmutableDictionary<ManagedCallSiteKey, ManagedCallSite> ManagedCallSites);

internal interface IWasmModuleTargetFactory
{
    WasmModuleTarget Create(WasmEmissionRequest request);
}

internal sealed class WasmModuleTargetFactory(
IWasmModulePlanner planner,
IModuleDataPlanner moduleDataPlanner,
IStructuredMethodEmissionPlanner methodEmissions) : IWasmModuleTargetFactory
{
    public WasmModuleTarget Create(WasmEmissionRequest request)
    {
        var plannedMethods = methodEmissions.Plan(request);
        var plan = planner.Build(request, plannedMethods);
        var moduleData = moduleDataPlanner.Build(
            plannedMethods,
            request.StaticInitializers,
            request.ConstructedStaticInitializers);
        return new(
            request,
            plan,
            moduleData,
            request.HostCallbacks.ToImmutableDictionary(
                callback => (callback.ImportMethod, callback.ParameterIndex)));
    }
}
