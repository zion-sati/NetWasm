using System;
using System.Linq;
using System.Runtime.CompilerServices;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Units;

/// <summary>
/// The module target's whole-program plans that emission reads without a key: the
/// compile's emission policy, the runtime imports it selected, interop, native imports and
/// callbacks, delegate adapters, member execution and the optional runtime helpers. Every unit depends on their fingerprint, so a
/// unit is reused only while all of them are unchanged; an ordinary body edit changes none.
/// Per-key reads (layouts, sites, guards, stack-trace ids) are recorded where they happen.
/// </summary>
internal static class UnitTargetFingerprint
{
    // One fingerprint per compile: every method's view shares the target's function map.
    private static readonly ConditionalWeakTable<FunctionIndexMap, StrongBox<UnitFingerprint>> Cache = new();

    public static UnitFingerprint Of(InstructionModuleTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return Cache.GetValue(target.FunctionIndices, _ => new(Compute(target))).Value;
    }

    private static UnitFingerprint Compute(InstructionModuleTarget target)
    {
        var answer = new UnitFingerprintBuilder();
        // Emission policy.
        answer.Add(target.EnableBranchValueFramePlacement);
        answer.Add(target.EnableAggregateReferenceLanes);
        answer.Add(target.EnableArrayMetadataCaching);
        answer.Add(target.HoistArrayNullChecks);
        answer.Add(target.LoopRotation.ToString());
        answer.Add(target.EliminateArrayStoreChecks);
        answer.Add(target.EnableScalarCallPreservation);
        answer.Add(target.InlineSmallBlockOperations);
        answer.Add(target.InlineArrayData);
        answer.Add(target.InlineObjectAllocation);
        answer.Add(target.CollectManagedMethodMemoryMetrics);
        answer.Add(target.EnableAddressTakenAggregatePromotion);
        // Runtime imports and helpers.
        var runtime = target.RuntimeImportSelection;
        answer.Add(runtime.ModuleProfile.ToString());
        answer.Add(runtime.IncludeTerminalExceptionReporter);
        answer.Add(runtime.IncludeStackTrace);
        answer.Add(runtime.IncludeExceptionCapture);
        answer.Add(runtime.IncludeEphemeronHandles);
        answer.Add(runtime.IncludeTerminalExceptionRaise);
        answer.Add(runtime.IncludeFinalization);
        answer.Add(runtime.IncludeWeakHandles);
        answer.Add(runtime.IncludeRandomSeed);
        answer.Add(runtime.IncludeArraySetValue);
        answer.Add(runtime.IncludeObjectClone);
        answer.Add(runtime.IncludeUnhandledExceptionHint);
        answer.Add(target.FunctionIndices.ManagedThrowFunctionIndex.IsPresent);
        answer.Add(target.FunctionIndices.ManagedRethrowFunctionIndex.IsPresent);
        // Interop.
        var interop = target.InteropImports;
        answer.Add(interop.Imports.Length);
        foreach (var import in interop.Imports) Add(ref answer, import);
        answer.Add(interop.StringLength.IsPresent);
        answer.Add(interop.CopyStringUtf16.IsPresent);
        answer.Add(interop.ReleaseHandle.IsPresent);
        answer.Add(interop.ReleaseSubscription.IsPresent);
        answer.Add(interop.ByteLength.IsPresent);
        answer.Add(interop.CopyBytes.IsPresent);
        answer.Add(interop.UseJavaScriptExportBoundary);
        answer.Add(interop.Conversions.Count);
        foreach (var (name, value) in interop.Conversions.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            answer.Add(name);
            answer.Add(value);
        }
        answer.Add(target.HostCallbacks.Count);
        foreach (var (key, callback) in target.HostCallbacks
                     .OrderBy(pair => UnitQueryKeys.Of(pair.Key.Method), StringComparer.Ordinal)
                     .ThenBy(pair => pair.Key.ParameterIndex))
        {
            answer.Add(UnitQueryKeys.Of(key.Method));
            answer.Add(key.ParameterIndex);
            answer.Add(callback.Invoke.CanonicalName);
            answer.Add(callback.ExportName);
        }
        answer.Add(target.JavaScriptAsyncBindings.Count);
        foreach (var (key, binding) in target.JavaScriptAsyncBindings
                     .OrderBy(pair => UnitQueryKeys.Of(pair.Key), StringComparer.Ordinal))
        {
            answer.Add(UnitQueryKeys.Of(key));
            answer.Add(binding.Return.ToString());
            answer.Add(binding.TaskType);
            answer.Add(binding.SetResult.CanonicalName);
            answer.Add(binding.SetException.CanonicalName);
            answer.Add(binding.SetCanceled.CanonicalName);
            answer.Add(binding.ValueTaskTaskField);
            answer.Add(binding.ValueTaskAsTask);
            answer.Add(binding.StatusField);
            answer.Add(binding.ResultField);
            answer.Add(binding.GetVoidResult);
            answer.Add(binding.CommandEntry?.Start);
            answer.Add(binding.CommandEntry?.Callback);
            answer.Add(binding.CommandEntry?.ExitCode);
        }
        answer.Add(target.NativeImports.Methods.Length);
        foreach (var import in target.NativeImports.Methods)
        {
            answer.Add(import.Method.CanonicalName);
            Add(ref answer, import.Import);
            // The ABI plan is a record whose generated text lists its members.
            answer.Add(import.Abi.ToString());
        }
        answer.Add(target.NativeCallbacks.Methods.Length);
        foreach (var callback in target.NativeCallbacks.Methods)
        {
            answer.Add(callback.Method.CanonicalName);
            answer.Add(callback.Abi.ToString());
            answer.Add(callback.NativeSymbol);
            answer.Add(callback.RuntimeImportSymbol);
            answer.Add(callback.ThunkExportName);
            answer.Add(callback.GetterName);
            answer.Add(callback.GetterIndex is not null);
        }
        // Finalization, delegate adapters and member execution (finalizable types and
        // delegate types are recorded where they are read).
        answer.Add(target.Finalization.UsesExplicitFinalizationApi);
        answer.Add(target.ObjectArrayDelegateAdapters.Count);
        foreach (var (key, adapter) in target.ObjectArrayDelegateAdapters.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            answer.Add(key);
            answer.Add(adapter.Factory.CanonicalName);
            answer.Add(adapter.DelegateType);
            answer.Add(adapter.Invoke);
            answer.Add(adapter.Target.CanonicalName);
            answer.Add(adapter.IsSupported);
        }
        var members = target.MemberExecution;
        answer.Add(members.Methods.Count);
        foreach (var (key, plan) in members.Methods.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            answer.Add(key);
            answer.Add(plan.Descriptor.CanonicalName);
            answer.Add(plan.RequiresDispatch);
            answer.Add(plan.Targets.Length);
            foreach (var dispatch in plan.Targets)
            {
                answer.Add(dispatch.ReceiverType);
                answer.Add(dispatch.Method.CanonicalName);
            }
        }
        answer.Add(members.Fields.Count);
        foreach (var (key, field) in members.Fields.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            answer.Add(key);
            answer.Add(field.CanonicalName);
        }
        answer.Add(members.UnsupportedTarget);
        answer.Add(members.MethodInvokers.Count);
        foreach (var invoker in members.MethodInvokers.Select(UnitQueryKeys.Of).Order(StringComparer.Ordinal))
            answer.Add(invoker);
        if (members.DelegateInvocation is { } invocation)
        {
            answer.Add(invocation.Methods.Count);
            foreach (var (key, method) in invocation.Methods.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                answer.Add(key);
                answer.Add(method.CanonicalName);
            }
            answer.Add(invocation.Invokers.Count);
            foreach (var invoker in invocation.Invokers.Select(UnitQueryKeys.Of).Order(StringComparer.Ordinal))
                answer.Add(invoker);
            answer.Add(invocation.UnsupportedTarget.CanonicalName);
            answer.Add(invocation.ArgumentTarget.CanonicalName);
            answer.Add(invocation.ParameterCountTarget.CanonicalName);
            answer.Add(invocation.InvocationTarget.CanonicalName);
        }
        else
        {
            answer.Add(-1);
        }
        return answer.Finish();
    }

    private static void Add(ref UnitFingerprintBuilder answer, WasmFunctionImport import)
    {
        answer.Add(import.Module);
        answer.Add(import.Name);
        answer.Add(import.Type.Parameters.Length);
        foreach (var parameter in import.Type.Parameters) answer.Add((int)parameter);
        answer.Add((int)import.Type.Result);
    }
}
