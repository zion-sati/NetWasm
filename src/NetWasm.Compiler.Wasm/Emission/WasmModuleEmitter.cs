using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Instructions.Calls;
using NetWasm.Compiler.Wasm.Emission.Instructions.Delegates;
using NetWasm.Compiler.Wasm.Emission.Instructions.Interop;
using NetWasm.Compiler.Wasm.Emission.Instructions.Memory;
using NetWasm.Compiler.Wasm.Emission.Instructions.Objects;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission.Results;
using NetWasm.Compiler.Wasm.ModuleEncoding;

namespace NetWasm.Compiler.Wasm.Emission;

internal sealed class WasmModuleEmitter(
    IMethodRepository methods,
    ISymbolFormatter symbols,
    ITargetLayout layouts,
    IManagedDefinitionSetAppender managedDefinitions,
    IConstructedMethodSetAppender constructedMethods,
    IDelegateFunctionAppender delegateFunctions,
    IHostCallbackSetAppender hostCallbacks,
    IAsyncJSImportSetAppender asyncJSImportFunctions,
    IFilterFunctionSetAppender filterFunctions,
    IRuntimeFunctionAppender runtimeFunctions,
    IRequestedExportSetAppender requestedExports,
    IComponentFunctionAppender componentFunctions,
    IFunctionIndexResolverFactory functionIndexResolvers,
    IModuleExportCollector moduleExports,
    IModuleImportCollector moduleImports,
    IEntryPointValidator entryPoints,
    IManagedBoundaryPlanValidator boundaryPlans,
    IWasmModuleEmissionResultBuilder results,
    IStaticInitializerFunctionAppender staticInitializers) : IWasmModuleEmitter
{
    private readonly IMethodRepository _methods =
        methods ?? throw new ArgumentNullException(nameof(methods));
    private readonly ISymbolFormatter _symbols =
        symbols ?? throw new ArgumentNullException(nameof(symbols));
    private readonly ITargetLayout _layouts =
        layouts ?? throw new ArgumentNullException(nameof(layouts));
    private readonly IFilterFunctionSetAppender _filterFunctions = filterFunctions;
    private readonly IManagedDefinitionSetAppender _managedDefinitions =
        managedDefinitions;
    private readonly IConstructedMethodSetAppender _constructedMethods =
        constructedMethods;
    private readonly IDelegateFunctionAppender _delegateFunctions = delegateFunctions;
    private readonly IHostCallbackSetAppender _hostCallbacks = hostCallbacks;
    private readonly IAsyncJSImportSetAppender _asyncJSImportFunctions =
        asyncJSImportFunctions;
    private readonly IRuntimeFunctionAppender _runtimeFunctions = runtimeFunctions;
    private readonly IRequestedExportSetAppender _requestedExports = requestedExports;
    private readonly IComponentFunctionAppender _componentFunctions = componentFunctions;
    private readonly IFunctionIndexResolverFactory _functionIndexResolvers =
        functionIndexResolvers;
    private readonly IModuleExportCollector _moduleExports = moduleExports;
    private readonly IModuleImportCollector _moduleImports = moduleImports;
    private readonly IEntryPointValidator _entryPoints = entryPoints;
    private readonly IManagedBoundaryPlanValidator _boundaryPlans = boundaryPlans;
    private readonly IWasmModuleEmissionResultBuilder _results = results;

    public WasmModuleEmissionResult Emit(WasmModuleTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var request = target.Request;
        var entryPoint = request.EntryPoint;
        var methods = request.Methods;
        var rootMaps = request.RootMaps;
        var requestedExports = request.RequestedExports;
        var constructedMethods =
            request.ConstructedMethods;
        var methodInstances =
            request.MethodInstances;
        var constructedRootMaps =
            request.ConstructedRootMaps;
        _entryPoints.Validate(entryPoint);
        var plan = target.Plan;
        var functionIndices = _functionIndexResolvers.Create(
            _methods,
            _symbols,
            plan.FunctionIndices);
        var moduleData = target.ModuleData;
        var orderedMethods = plan.OrderedMethods;
        var orderedConstructedMethods = plan.OrderedConstructedMethods;
        var delegateInvokes = plan.DelegateInvokes;
        var delegateInvokeTarget = new DelegateInvokeTarget(
            plan.FunctionIndices,
            request.DelegateBindings);
        var imports = _moduleImports.Collect(new(
            request,
            plan.RuntimeImports,
            plan.InteropImports.Imports,
            _layouts.Target.Target,
            target.HostCallbacks));
        var functions = new List<WasmFunctionDefinition>(
            orderedMethods.Length + orderedConstructedMethods.Length + 1);
        var boundaryEntries = new List<ManagedBoundaryPlanEntry>();
        var managedMethodEmissions = new List<ManagedMethodEmissionRecord>(
            orderedMethods.Length + orderedConstructedMethods.Length);
        var filterEnvironments = new Dictionary<string, FilterEnvironmentLayout>(
            StringComparer.Ordinal);
        _managedDefinitions.Append(functions, filterEnvironments,
            managedMethodEmissions, orderedMethods, methods, rootMaps,
            target.Instructions, functionIndices);
        _constructedMethods.Append(functions, filterEnvironments,
            managedMethodEmissions, orderedConstructedMethods, methodInstances,
            constructedMethods, constructedRootMaps, target.Instructions,
            functionIndices);
        _delegateFunctions.Append(functions, delegateInvokes, request.DelegateTypes,
            plan.DelegateCountHelperIndex, plan.DelegateLeafHelperIndex,
            plan.DelegateEqualityHelperIndex, delegateInvokeTarget, functionIndices);
        staticInitializers.Append(functions, moduleData.StaticInitializerFunctions);

        var initialization = new RuntimeInitializationPlan(
            moduleData.StaticDataEnd,
            plan.StackTraceMethods,
            plan.RuntimeImportSelection)
        {
            ModuleInitializers = [
                .. request.ModuleInitializers.Select(initializer =>
                {
                    var guard = moduleData.StaticInitializerGuards[
                        StaticInitializerGuard.KeyFor(initializer)];
                    return new ModuleInitializerCall(
                        guard.Address,
                        guard.FunctionIndex.Value);
                })
            ],
        };

        var callbackIndices = ImmutableDictionary.CreateBuilder<string, int>(StringComparer.Ordinal);
        _hostCallbacks.Append(functions, imports.Length, callbackIndices,
            request.HostCallbacks, plan.FunctionIndices, initialization,
            plan.InteropImports, plan.RuntimeImportSelection, boundaryEntries);

        var asyncImportIndices = ImmutableDictionary.CreateBuilder<string, int>(
            StringComparer.Ordinal);
        _asyncJSImportFunctions.Append(functions, imports.Length, asyncImportIndices,
            request, functionIndices, boundaryEntries);

        var filterIndices = ImmutableDictionary.CreateBuilder<int, int>();
        _filterFunctions.Append(functions, imports.Length, filterIndices,
            moduleData.FilterFunclets, filterEnvironments, target.Instructions,
            functionIndices, managedMethodEmissions);
        var plannedFilterIndices = filterIndices.ToImmutable();
        var asyncExportHelperIndices = ImmutableDictionary.CreateBuilder<string, int>(
            StringComparer.Ordinal);
        request.JavaScriptAsyncBindings.TryGetValue(
            entryPoint.Key,
            out var entryPointAsyncBinding);
        var runtimeFunctions = _runtimeFunctions.Append(
            functions,
            imports.Length,
            moduleData.FilterFunclets,
            plannedFilterIndices,
            entryPoint,
            initialization,
            request.EntryPointProfile,
            entryPointAsyncBinding,
            asyncExportHelperIndices,
            functionIndices,
            boundaryEntries,
            request.EntryPointArgumentFactory);
        var requestedExportIndices = ImmutableDictionary.CreateBuilder<string, int>(
            StringComparer.Ordinal);
        _requestedExports.Append(functions, imports.Length, requestedExportIndices,
            asyncExportHelperIndices, requestedExports,
            request.JavaScriptAsyncBindings, initialization,
            runtimeFunctions.HasFinalizers, request.ModuleProfile, functionIndices,
            boundaryEntries);
        var exports = _moduleExports.Collect(
            request.EntryPointProfile,
            runtimeFunctions.EntryPointIndex,
            runtimeFunctions.FilterDispatcherIndex,
            runtimeFunctions.FinalizerDispatcherIndex,
            requestedExportIndices,
            callbackIndices,
            asyncImportIndices,
            asyncExportHelperIndices).ToList();

        var runtimeFeatures = _componentFunctions.Append(
            functions,
            exports,
            boundaryEntries,
            request,
            _layouts.Target.Target,
            initialization,
            imports.Length,
            requestedExportIndices,
            functionIndices);

        _boundaryPlans.Validate(functions, exports, imports.Length, boundaryEntries);

        return _results.Build(new(
            new WasmModuleBuildRequest(
                imports,
                RuntimeAbi.RuntimeModule,
                "memory",
                functions,
                exports,
                moduleData.DataSegments,
                true,
                _layouts.Target.Target,
                !request.EmitStackTrace),
            moduleData.StaticDataEnd,
            managedMethodEmissions,
            plan.StackTraceMethods.Symbols,
            runtimeFeatures));
    }

}
