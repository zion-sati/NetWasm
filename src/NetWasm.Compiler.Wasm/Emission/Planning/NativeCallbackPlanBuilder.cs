using System;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.NativeInterop;

namespace NetWasm.Compiler.Wasm.Emission.Planning;

internal sealed class NativeCallbackPlanBuilder(
    INativeCallbackDeclarationValidator declarations,
    INativeAbiSignaturePlanner signatures) : INativeCallbackPlanBuilder
{
    public NativeCallbackPlan Build(
        IReadOnlyDictionary<string, MethodInstanceModel> callbacks,
        IReadOnlySet<string> addressedCallbacks,
        int firstGetterIndex)
    {
        ArgumentNullException.ThrowIfNull(callbacks);
        ArgumentNullException.ThrowIfNull(addressedCallbacks);
        ArgumentOutOfRangeException.ThrowIfNegative(firstGetterIndex);
        if (callbacks.Count == 0)
        {
            return NativeCallbackPlan.Empty;
        }

        var ordered = callbacks.Values
            .OrderBy(method => method.CanonicalName, StringComparer.Ordinal)
            .ToArray();
        if (ordered.Select(method => method.CanonicalName)
            .Distinct(StringComparer.Ordinal).Count() != ordered.Length)
        {
            throw new CompilerException(new(
                DiagnosticCode.CompilerInvariant,
                "Native callback identities must be unique."));
        }

        foreach (var method in ordered)
        {
            declarations.ValidateAddressTarget(method);
        }
        if (addressedCallbacks.Any(identity => !callbacks.ContainsKey(identity)) ||
            ordered.Any(method =>
                method.Definition.NativeCallback!.EntryPoint is null &&
                !addressedCallbacks.Contains(method.CanonicalName)))
        {
            throw new CompilerException(new(
                DiagnosticCode.CompilerInvariant,
                "Every native callback must be named, address-taken, or both."));
        }

        var getterOrdinal = 0;
        var planned = ordered.Select((method, ordinal) =>
        {
            var abi = signatures.Plan(
                method.Signature,
                NativeAbiSignatureKind.Callback,
                method.CanonicalName);
            var entryPoint = method.Definition.NativeCallback!.EntryPoint;
            var isAddressTaken = addressedCallbacks.Contains(method.CanonicalName);
            var currentGetterOrdinal = isAddressTaken ? getterOrdinal++ : -1;
            return new NativeCallbackMethodPlan(
                method,
                abi,
                entryPoint ?? $"__netwasm_native_callback_{ordinal}",
                entryPoint is null
                    ? $"__netwasm_native_callback_{ordinal}"
                    : $"__netwasm_named_callback_import_{ordinal}",
                entryPoint ?? $"__netwasm_application_callback_{ordinal}",
                isAddressTaken
                    ? $"__netwasm_callback_address_{currentGetterOrdinal}"
                    : null,
                isAddressTaken
                    ? new(firstGetterIndex + currentGetterOrdinal)
                    : null);
        }).ToArray();
        ValidateSymbolIdentities(planned);
        return new([.. planned]);
    }

    private static void ValidateSymbolIdentities(
        NativeCallbackMethodPlan[] callbacks)
    {
        var applicationExports = new HashSet<string>(StringComparer.Ordinal);
        var linkerSymbols = new HashSet<string>(StringComparer.Ordinal);
        foreach (var callback in callbacks)
        {
            if (!applicationExports.Add(callback.ThunkExportName) ||
                !linkerSymbols.Add(callback.RuntimeImportSymbol) ||
                callback.IsNamed && !linkerSymbols.Add(callback.NativeSymbol) ||
                callback.GetterName is { } getter && !linkerSymbols.Add(getter))
            {
                throw new CompilerException(new(
                    DiagnosticCode.NativeInterop,
                    "Native callback symbols must be unique."));
            }
        }
    }
}
