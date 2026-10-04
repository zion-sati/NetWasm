using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;
using NetWasm.Compiler.Wasm.Emission.Methods;

namespace NetWasm.Compiler.Wasm.Emission.Planning;

internal sealed record WasmModulePlan(
    RuntimeImportSelection RuntimeImportSelection,
    ImmutableArray<WasmFunctionImport> RuntimeImports,
    ImmutableArray<ManagedDefinitionEmission> OrderedMethods,
    ImmutableArray<ManagedMethodIdentity> OrderedConstructedMethods,
    StackTraceMethodPlan StackTraceMethods,
    ImmutableArray<MethodInstanceModel> DelegateInvokes,
    FunctionIndexMap FunctionIndices,
    InteropImportPlan InteropImports,
    OptionalFunctionIndex DelegateCountHelperIndex,
    OptionalFunctionIndex DelegateLeafHelperIndex,
    OptionalFunctionIndex DelegateEqualityHelperIndex,
    OptionalFunctionIndex DelegateRemoveHelperIndex)
{
    public int StaticInitializerFunctionBase { get; init; }
    public NativeImportPlan NativeImports { get; init; } = NativeImportPlan.Empty;
    public NativeCallbackPlan NativeCallbacks { get; init; } = NativeCallbackPlan.Empty;
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
    INativeCallbackPlanBuilder callbackPlans) : IWasmModulePlanner
{
    private readonly INativeImportPlanner _nativeImports = nativeImports ??
        throw new ArgumentNullException(nameof(nativeImports));
    private readonly INativeCallbackPlanBuilder _callbackPlans = callbackPlans ??
        throw new ArgumentNullException(nameof(callbackPlans));
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
        var runtimeImportSelection = new RuntimeImportSelection(
            request.ModuleProfile,
            request.EntryPointProfile == WasmEntryPointProfile.Process &&
            request.StructuredDiagnostics ||
            hasInteropTerminalBoundary,
            request.EmitStackTrace,
            request.RequestedExports.Values.Any(request.JavaScriptAsyncBindings.ContainsKey),
            RequiresEphemeronHandles(methodEmissions),
            request.StructuredDiagnostics || hasInteropTerminalBoundary);
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
        var delegateInvokeHelperIndices = delegateInvokes
            .Select((method, index) => (
                method.DeclaringType.CanonicalName,
                Index: new WasmFunctionIndex(importedFunctionCount + orderedMethods.Length +
                    orderedConstructedMethods.Length + index)))
            .ToImmutableDictionary(
                item => item.CanonicalName,
                item => item.Index,
                StringComparer.Ordinal);
        var helperBase = importedFunctionCount + orderedMethods.Length +
            orderedConstructedMethods.Length + delegateInvokes.Length;
        var hasDelegates = !request.DelegateTypes.IsEmpty;
        var plan = new WasmModulePlan(
            runtimeImportSelection,
            selectedRuntimeImports,
            orderedMethods,
            orderedConstructedMethods,
            stackTracePlan,
            delegateInvokes,
            new FunctionIndexMap(
                functionIndices,
                constructedFunctionIndices,
                delegateInvokeHelperIndices,
                importedMethodIndices),
            interopImports,
            Optional(hasDelegates, helperBase),
            Optional(hasDelegates, helperBase + 1),
            Optional(hasDelegates, helperBase + 2),
            Optional(hasDelegates, helperBase + 3))
        {
            StaticInitializerFunctionBase = helperBase + (hasDelegates ? 4 : 0),
            NativeImports = nativeImportPlan,
            NativeCallbacks = callbackPlan,
        };
        invariants.Validate(request, methodEmissions, plan);
        return plan;
    }

    private static OptionalFunctionIndex Optional(bool present, int index) =>
        present ? OptionalFunctionIndex.At(index) : OptionalFunctionIndex.Missing;

    private bool RequiresEphemeronHandles(
        ImmutableArray<StructuredMethodEmission> methodEmissions) =>
        methodEmissions
            .SelectMany(static emission => emission.Method.Header.Instructions)
            .Where(static instruction =>
                instruction.Operation is CilOperation.Call or CilOperation.CallVirtual)
            .Select(static instruction => instruction.Operand switch
            {
                CilOperand.Entity entity => entity.Key,
                CilOperand.MethodInstance method => method.Value.Definition.Key,
                _ => default,
            })
            .Any(method =>
                method != default &&
                intrinsics.TryGetIntrinsic(method, out var intrinsic) &&
                intrinsic is RuntimeIntrinsic.EphemeronHandleCreate or
                    RuntimeIntrinsic.EphemeronHandleGetKey or
                    RuntimeIntrinsic.EphemeronHandleGetValue or
                    RuntimeIntrinsic.EphemeronHandleRelease);

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
