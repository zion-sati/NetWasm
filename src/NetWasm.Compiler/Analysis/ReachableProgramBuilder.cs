using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler;
using NetWasm.Compiler.Analysis.Delegates;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Calls;
using NetWasm.Compiler.GarbageCollection;

namespace NetWasm.Compiler.Analysis;

internal sealed class ReachableProgramBuilder(
    IAllocationCapabilityAnalyzer allocationCapabilities,
    IDelegateBindingPlanner delegateBindings) :
    IReachableProgramBuilder
{
    public ReachableProgram Build(
        MethodDefinitionModel entryPoint,
        ImmutableArray<ProgramExport> exports,
        ReachabilityLedgerSnapshot state,
        IDelegateTypeRecognizer delegateTypes,
        ITypeTestPlanner typeTestPlanner)
    {
        var dispatchCallSites =
            state.DispatchDeclarations
                .Select(pair => new DispatchCallSiteModel(
                    pair.Value.Caller,
                    pair.Value.IlOffset,
                    pair.Value.Declaration,
                    state.DispatchTargets.TryGetValue(pair.Key, out var targets)
                        ? targets
                        : []))
                .ToImmutableDictionary(site => site.Key, StringComparer.Ordinal);
        var methods = state.Methods;
        var constructedMethods = state.ConstructedMethods;
        var typeTestSites = typeTestPlanner.Build(
            methods.Values.Concat(constructedMethods.Values),
            state.AllocatedTypes);

        var capabilities = allocationCapabilities.Analyze(
            new AllocationCapabilityAnalysisRequest(
                methods,
                constructedMethods,
                dispatchCallSites));
        var allocatingMethods = capabilities.Methods;
        var constructedAllocatingMethods =
            capabilities.ConstructedMethods;
        var rootMaps = methods.ToImmutableDictionary(
            pair => pair.Key,
            pair => EmptyRootMap(pair.Key));
        var constructedRootMaps =
            constructedMethods.ToImmutableDictionary(
                pair => pair.Key,
                pair => EmptyRootMap(pair.Value.Method.Definition.Key),
                StringComparer.Ordinal);
        var delegateInvokeMethods = state.ManagedCallSites.Values
            .Where(site =>
                site.Operation == ManagedCallOperation.Virtual &&
                site.Target.Definition.Name == "Invoke" &&
                delegateTypes.Recognize(site.Target.DeclaringType))
            .Select(site => site.Target)
            .Concat(state.HostCallbacks.Select(callback => callback.Invoke));
        var plannedDelegateBindings = delegateBindings.Plan(
            delegateInvokeMethods,
            state.CallableMethods.Values);

        return new ReachableProgram(
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
            state.Finalizers,
            state.ImplicitExceptions)
        {
            ModuleInitializers = [
                .. state.ModuleInitializers
                    .OrderBy(key => key.Assembly.Name, StringComparer.Ordinal)
                    .ThenBy(key => key.MetadataToken)
            ],
            ConstructedMethods = constructedMethods,
            MethodInstances = state.MethodInstances,
            ConstructedTypes = state.ConstructedTypes,
            RuntimeTypeDefinitions = state.RuntimeTypeDefinitions,
            ConstructedAllocatingMethods = constructedAllocatingMethods,
            ConstructedRootMaps = constructedRootMaps,
            ConstructedFields = state.ConstructedFields,
            ConstructedStaticInitializers = [
                ..state.ConstructedStaticInitializers
                    .Order(StringComparer.Ordinal)
            ],
            DispatchCallSites = dispatchCallSites,
            TypeTestSites = typeTestSites,
            CallableMethods = state.CallableMethods,
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
            ManagedCallSites = state.ManagedCallSites,
            DelegateBindings = plannedDelegateBindings,
            DelegateTypes = [.. state.AllocatedTypes
                .Where(delegateTypes.Recognize)
                .OrderBy(type => type.CanonicalName, StringComparer.Ordinal)],
        };
    }

    private static MethodRootMap EmptyRootMap(EntityKey method) => new(
        method,
        ImmutableDictionary<RootSource, int>.Empty,
        ImmutableDictionary<int, SafepointRootMap>.Empty);
}
