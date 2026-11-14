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
    IManagedByRefEffectAnalyzer byRefEffects,
    IDelegateBindingPlanner delegateBindings) :
    IReachableProgramBuilder
{
    public ReachableProgram Build(
        MethodDefinitionModel? entryPoint,
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
                        : [])
                {
                    SharesObjectDispatch = SharesObjectDispatch(pair.Value, targets, state),
                })
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
        var managedByRefEffects = byRefEffects.Analyze(
            new ManagedByRefEffectAnalysisRequest(
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
        // Every delegate type whose Invoke is called, directly or not, or whose address a delegate
        // binds, has an invoke helper.
        var delegateInvokeMethods = state.ManagedCallSites.Values
            .Where(site =>
                site.Target.Definition.Name == "Invoke" &&
                delegateTypes.Recognize(site.Target.DeclaringType))
            .Select(site => site.Target)
            .Concat(state.HostCallbacks.Select(callback => callback.Invoke))
            .Concat(state.MemberExecution.DelegateInvocation?.Methods.Values ?? []);
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
            ConstructedStaticInitializers = [
                ..state.ConstructedStaticInitializers
                    .Order(StringComparer.Ordinal)
            ],
            DispatchCallSites = dispatchCallSites,
            SharedObjectDispatch = state.SharedObjectDispatch,
            TypeTestSites = typeTestSites,
            CallableMethods = state.CallableMethods,
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
            ManagedCallSites = state.ManagedCallSites,
            DelegateBindings = plannedDelegateBindings,
            ManagedByRefEffects = managedByRefEffects,
            DelegateTypes = [.. state.AllocatedTypes
                .Where(delegateTypes.Recognize)
                .OrderBy(type => type.CanonicalName, StringComparer.Ordinal)],
            AllocatedTypes = [.. state.AllocatedTypes.OrderBy(type => type.CanonicalName, StringComparer.Ordinal)],
        };
    }

    private static MethodRootMap EmptyRootMap(EntityKey method) => new(
        method,
        ImmutableDictionary<RootSource, int>.Empty,
        ImmutableDictionary<int, SafepointRootMap>.Empty);

    // An unbounded virtual call on object with many targets uses the shared body the analysis reached for it; the
    // shared body's own call dispatches.
    private static bool SharesObjectDispatch(
        DispatchDeclaration declaration,
        ImmutableArray<DispatchTargetModel> targets,
        ReachabilityLedgerSnapshot state) =>
        declaration.Operation == CilOperation.CallVirtual &&
        declaration.ReceiverBound is null &&
        !targets.IsDefault && targets.Length >= SharedDispatchPolicy.MinimumTargets &&
        state.SharedObjectDispatch.TryGetValue(declaration.Declaration.CanonicalName, out var shared) &&
        declaration.Caller != shared.CanonicalName;
}
