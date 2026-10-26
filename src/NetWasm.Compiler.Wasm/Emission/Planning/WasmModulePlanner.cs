using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;
using NetWasm.Compiler.Wasm.Emission.Methods;

namespace NetWasm.Compiler.Wasm.Emission.Planning;

internal sealed record WasmModulePlan(
    RuntimeImportSelection RuntimeImportSelection,
    FinalizationPlan Finalization,
    ImmutableArray<WasmFunctionImport> RuntimeImports,
    ImmutableArray<ManagedDefinitionEmission> OrderedMethods,
    ImmutableArray<ManagedMethodIdentity> OrderedConstructedMethods,
    StackTraceMethodPlan StackTraceMethods,
    ImmutableArray<MethodInstanceModel> DelegateInvokes,
    FunctionIndexMap FunctionIndices,
    InteropImportPlan InteropImports)
{
    public int StaticInitializerFunctionBase { get; init; }
    public NativeImportPlan NativeImports { get; init; } = NativeImportPlan.Empty;
    public NativeCallbackPlan NativeCallbacks { get; init; } = NativeCallbackPlan.Empty;
    public ImmutableArray<MethodInstanceModel> JSImportForwarders { get; init; } = [];
}

internal sealed class WasmModulePlanner(
    ITypeRepository types,
    IMethodRepository methods,
    ITypeClassifier typeClassifier,
    IRuntimeIntrinsicRegistry intrinsics,
    IInteropImportPlanner interopImportPlanner,
    IRuntimeImportResolver runtimeImports,
    IStackTraceMethodPlanBuilder stackTraceMethods,
    IWasmModulePlanInvariantValidator invariants,
    INativeImportPlanner nativeImports,
    INativeCallbackPlanBuilder callbackPlans,
    IFinalizationPlanBuilder finalizationPlans) : IWasmModulePlanner
{
    private readonly INativeImportPlanner _nativeImports = nativeImports ??
        throw new ArgumentNullException(nameof(nativeImports));
    private readonly INativeCallbackPlanBuilder _callbackPlans = callbackPlans ??
        throw new ArgumentNullException(nameof(callbackPlans));
    private readonly IFinalizationPlanBuilder _finalizationPlans = finalizationPlans ??
        throw new ArgumentNullException(nameof(finalizationPlans));
    public WasmModulePlan Build(
        WasmEmissionRequest request,
        ImmutableArray<StructuredMethodEmission> methodEmissions)
    {
        var hasInteropTerminalBoundary =
            !request.HostCallbacks.IsEmpty ||
            request.NativeCallbacks.Count != 0 ||
            (request.UseJavaScriptExportBoundary ||
             request.ModuleProfile == WasmModuleProfile.CoreApplication) &&
             request.RequestedExports.Count != 0;
        var finalization = _finalizationPlans.Build(methodEmissions);
        var runtimeImportSelection = new RuntimeImportSelection(
            request.ModuleProfile,
            request.EntryPointProfile == WasmEntryPointProfile.Process &&
            request.StructuredDiagnostics ||
            hasInteropTerminalBoundary,
            request.EmitStackTrace,
            request.RequestedExports.Values.Any(request.JavaScriptAsyncBindings.ContainsKey),
            RequiresEphemeronHandles(methodEmissions),
            request.StructuredDiagnostics || hasInteropTerminalBoundary,
            finalization.RequiresRuntime,
            RequiresWeakHandles(methodEmissions),
            RequiresRandomSeed(methodEmissions));
        var selectedRuntimeImports = runtimeImports.Resolve(runtimeImportSelection);
        var runtimeImportCount = selectedRuntimeImports.Length;
        var interopImports = interopImportPlanner.Build(request, runtimeImportCount);
        var helperImportCount = interopImports.Imports.Length;
        var witImportIdentities = request.WitImportMethods
            .Select(method => method.WitImport!.Identity)
            .Distinct()
            .ToImmutableArray();
        var nativeImportPlan = _nativeImports.Plan(request.MethodInstances.Values);
        var callbackGetterBase = runtimeImportCount + helperImportCount +
            request.JSImportMethods.Length + witImportIdentities.Length +
            nativeImportPlan.Methods.Length;
        var callbackPlan = request.NativeCallbacks.Count == 0
            ? NativeCallbackPlan.Empty
            : _callbackPlans.Build(
                request.NativeCallbacks,
                request.AddressedNativeCallbacks,
                callbackGetterBase);
        var importedFunctionCount = runtimeImportCount + helperImportCount +
            request.JSImportMethods.Length + witImportIdentities.Length +
            nativeImportPlan.Methods.Length + callbackPlan.AddressedMethods.Length;
        var firstManagedImportIndex = runtimeImportCount + helperImportCount;
        var witImportIndices = witImportIdentities
            .Select((identity, index) => (Identity: identity, Index: new WasmFunctionIndex(
                firstManagedImportIndex + request.JSImportMethods.Length + index)))
            .ToImmutableDictionary(item => item.Identity, item => item.Index);
        var importedMethodIndices = request.JSImportMethods
            .Select((method, index) => (method.Key, Index: new WasmFunctionIndex(
                firstManagedImportIndex + index)))
            .Concat(request.WitImportMethods.Select(method => (
                method.Key,
                Index: witImportIndices[method.WitImport!.Identity])))
            .Concat(nativeImportPlan.Methods.Select((import, index) => (
                import.Method.Definition.Key,
                Index: new WasmFunctionIndex(firstManagedImportIndex + request.JSImportMethods.Length +
                    witImportIdentities.Length + index))))
            .ToImmutableDictionary(item => item.Key, item => item.Index);
        var constructedIdentities = request.ConstructedMethods.Keys
            .ToHashSet(StringComparer.Ordinal);
        var orderedMethods = methodEmissions
            .Where(emission => !constructedIdentities.Contains(emission.Identity.CanonicalName))
            .Select(emission => new ManagedDefinitionEmission(
                emission.Identity,
                emission.Method.Header.Method.Key))
            .OrderBy(
                emission => emission.Identity.CanonicalName,
                StringComparer.Ordinal)
            .ToImmutableArray();
        var orderedConstructedMethods = methodEmissions
            .Where(emission => constructedIdentities.Contains(emission.Identity.CanonicalName))
            .Select(emission => emission.Identity)
            .OrderBy(
                identity => identity.CanonicalName,
                StringComparer.Ordinal)
            .ToImmutableArray();
        var orderedMethodKeys = orderedMethods
            .Select(method => method.MethodKey)
            .ToImmutableArray();
        if (orderedMethodKeys.Distinct().Count() != orderedMethodKeys.Length ||
            orderedMethods
                .Select(method => method.Identity.CanonicalName)
                .Distinct(StringComparer.Ordinal)
                .Count() != orderedMethods.Length)
        {
            throw new CompilerException(new CompilerDiagnostic(
                DiagnosticCode.CompilerInvariant,
                "DUPLICATE_MANAGED_DEFINITION: managed definition keys and identities must be unique."));
        }

        var orderedConstructedMethodKeys = orderedConstructedMethods
            .Select(identity => identity.CanonicalName)
            .ToImmutableArray();
        var stackTraceConstructedMethods = request.EmitStackTrace
            ? orderedConstructedMethodKeys
                .Select(name => new StackTraceConstructedMethod(
                    name,
                    request.MethodInstances[name].Definition.Key))
                .ToImmutableArray()
            : [];
        var stackTracePlan = stackTraceMethods.Build(
            request.EmitStackTrace,
            orderedMethodKeys,
            stackTraceConstructedMethods,
            request.SourceLocations);
        var functionIndices = orderedMethodKeys
            .Select((key, index) => (
                key,
                Index: new WasmFunctionIndex(importedFunctionCount + index)))
            .ToImmutableDictionary(item => item.key, item => item.Index);
        var constructedFunctionIndices =
            orderedConstructedMethodKeys
                .Select((key, index) => (
                    key,
                    Index: new WasmFunctionIndex(
                        importedFunctionCount + orderedMethods.Length + index)))
                .ToImmutableDictionary(
                    item => item.key,
                    item => item.Index,
                    StringComparer.Ordinal);
        var delegateInvokes = request.Methods.Values
            .Concat(request.ConstructedMethods.Values)
            .SelectMany(method => method.Header.Instructions)
            .Where(instruction => instruction.Operation == CilOperation.CallVirtual)
            .Select(GetMethodInstance)
            .Where(method =>
                method.Definition.Name == "Invoke" &&
                typeClassifier.IsDelegateType(method.Definition.DeclaringType))
            .Concat(request.HostCallbacks.Select(callback => callback.Invoke))
            .Concat(request.MemberExecution.DelegateInvocation?.Methods.Values ?? [])
            .DistinctBy(method => method.DeclaringType.CanonicalName)
            .OrderBy(method => method.DeclaringType.CanonicalName, StringComparer.Ordinal)
            .ToImmutableArray();
        // Every implicit and explicit throw calls the shared throw function, which follows
        // the managed methods. ExceptionDispatchInfo's rethrow has its own when reachable.
        var managedThrowFunction = importedFunctionCount + orderedMethods.Length +
            orderedConstructedMethods.Length;
        var hasManagedRethrowFunction = RequiresManagedRethrowFunction(methodEmissions);
        var throwFunctionCount = hasManagedRethrowFunction ? 2 : 1;
        var delegateInvokeHelperIndices = delegateInvokes
            .Select((method, index) => (
                method.DeclaringType.CanonicalName,
                Index: new WasmFunctionIndex(managedThrowFunction + throwFunctionCount + index)))
            .ToImmutableDictionary(
                item => item.CanonicalName,
                item => item.Index,
                StringComparer.Ordinal);
        var helperBase = managedThrowFunction + throwFunctionCount + delegateInvokes.Length;
        if (!request.JavaScriptConversions.IsEmpty)
        {
            interopImports = interopImports with
            {
                Conversions = request.JavaScriptConversions.ToImmutableDictionary(
                    conversion => conversion.Key,
                    conversion => functionIndices[conversion.Value.Definition.Key].Value,
                    StringComparer.Ordinal),
            };
        }
        var plan = new WasmModulePlan(
            runtimeImportSelection,
            finalization,
            selectedRuntimeImports,
            orderedMethods,
            orderedConstructedMethods,
            stackTracePlan,
            delegateInvokes,
            new FunctionIndexMap(
                functionIndices,
                constructedFunctionIndices,
                delegateInvokeHelperIndices,
                importedMethodIndices)
            {
                ManagedThrowFunctionIndex = OptionalFunctionIndex.At(managedThrowFunction),
                ManagedRethrowFunctionIndex = hasManagedRethrowFunction
                    ? OptionalFunctionIndex.At(managedThrowFunction + 1)
                    : OptionalFunctionIndex.Missing,
            },
            interopImports)
        {
            StaticInitializerFunctionBase = helperBase,
            NativeImports = nativeImportPlan,
            NativeCallbacks = callbackPlan,
        };
        invariants.Validate(request, methodEmissions, plan);
        return plan;
    }

    private bool RequiresManagedRethrowFunction(
        ImmutableArray<StructuredMethodEmission> methodEmissions) =>
        CalledIntrinsics(methodEmissions)
            .Any(static intrinsic => intrinsic == RuntimeIntrinsic.ExceptionDispatchPreserved);

    private bool RequiresEphemeronHandles(
        ImmutableArray<StructuredMethodEmission> methodEmissions) =>
        CalledIntrinsics(methodEmissions)
            .Any(static intrinsic => intrinsic is RuntimeIntrinsic.EphemeronHandleCreate or
                RuntimeIntrinsic.EphemeronHandleGetKey or
                RuntimeIntrinsic.EphemeronHandleGetValue or
                RuntimeIntrinsic.EphemeronHandleSetTargetToNull or
                RuntimeIntrinsic.EphemeronHandleSetValue or
                RuntimeIntrinsic.EphemeronHandleRelease);

    private bool RequiresWeakHandles(
        ImmutableArray<StructuredMethodEmission> methodEmissions) =>
        CalledIntrinsics(methodEmissions)
            .Any(static intrinsic => intrinsic is RuntimeIntrinsic.WeakHandleCreate or
                RuntimeIntrinsic.WeakHandleGet or
                RuntimeIntrinsic.WeakHandleSet or
                RuntimeIntrinsic.WeakHandleRelease);

    private bool RequiresRandomSeed(
        ImmutableArray<StructuredMethodEmission> methodEmissions) =>
        CalledIntrinsics(methodEmissions)
            .Any(static intrinsic => intrinsic == RuntimeIntrinsic.RandomSeed);

    private IEnumerable<RuntimeIntrinsic> CalledIntrinsics(
        ImmutableArray<StructuredMethodEmission> methodEmissions)
    {
        foreach (var instruction in methodEmissions
            .SelectMany(static emission => emission.Method.Header.Instructions)
            .Where(static instruction =>
                instruction.Operation is CilOperation.Call or CilOperation.CallVirtual))
        {
            var method = instruction.Operand switch
            {
                CilOperand.Entity entity => entity.Key,
                CilOperand.MethodInstance instance => instance.Value.Definition.Key,
                _ => default,
            };
            if (method != default && intrinsics.TryGetIntrinsic(method, out var intrinsic))
            {
                yield return intrinsic;
            }
        }
    }

    private MethodInstanceModel GetMethodInstance(CilInstruction instruction) =>
        instruction.Operand switch
        {
            CilOperand.MethodInstance method => method.Value,
            CilOperand.Entity method => new(
                methods.GetMethod(method.Key),
                GetTypeIdentity(methods.GetMethod(method.Key).DeclaringType),
                [],
                methods.GetMethod(method.Key).Signature),
            _ => throw new InvalidOperationException(
                $"instruction {instruction.Operation} has no method operand"),
        };

    private CliTypeIdentity GetTypeIdentity(EntityKey key)
    {
        var type = types.GetTypeDefinition(key);
        return CliTypeIdentity.Named(
            key.Assembly,
            type.Namespace,
            type.Name,
            type.IsValueType);
    }
}
