using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace NetWasm.Runtime.Pack.Materialization;

internal sealed class RuntimeLinkedImportValidator(IRuntimeNativeValidationProfileValidator profiles) : IRuntimeLinkedImportValidator
{
    private readonly IRuntimeNativeValidationProfileValidator _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));

    public void Validate(
        RuntimeNativeValidationProfile profile,
        RuntimeLinkedModule module,
        RuntimeNativeCallbackSupport? callbackSupport = null)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(module.MemoryLayout);
        _profiles.Validate(profile, module.MemoryLayout.Target);
        if (module.Imports.IsDefault || module.FunctionTypes.IsDefault)
            throw Invalid();
        var expected = profile.Imports.ToDictionary(import => (import.Module, import.Name));
        if (callbackSupport is not null)
        {
            foreach (var callback in callbackSupport.Callbacks)
            {
                var contract = new RuntimeNativeImportContract(
                    "netwasm.application.v1",
                    callback.ApplicationExportName,
                    [.. callback.Parameters.Select(ValueType)],
                    callback.ReturnType is { } result
                        ? ImmutableArray.Create(ValueType(result))
                        : [],
                    Required: callback.RuntimeGetterExportName is not null);
                if (!expected.TryAdd((contract.Module, contract.Name), contract))
                {
                    throw Invalid();
                }
            }
        }
        var seen = new HashSet<(string Module, string Name)>();
        foreach (var import in module.Imports)
        {
            if (import is null || import.Kind != 0 || !seen.Add((import.Module, import.Name)) ||
                !expected.TryGetValue((import.Module, import.Name), out var contract) || import.TypeIndex >= module.FunctionTypes.Length)
                throw Invalid();
            var signature = module.FunctionTypes[(int)import.TypeIndex];
            if (signature is null || signature.Parameters.IsDefault || signature.Results.IsDefault ||
                !signature.Parameters.SequenceEqual(contract.Parameters) || !signature.Results.SequenceEqual(contract.Results))
                throw Invalid();
        }
        if (expected.Values.Any(import => import.Required && !seen.Contains((import.Module, import.Name))))
            throw Invalid();
    }

    private static InvalidOperationException Invalid() =>
        new("The native-linked runtime imports do not match the supported runtime contract.");

    private static byte ValueType(RuntimeNativeValueType type) => type switch
    {
        RuntimeNativeValueType.I32 => 0x7f,
        RuntimeNativeValueType.I64 => 0x7e,
        RuntimeNativeValueType.F32 => 0x7d,
        RuntimeNativeValueType.F64 => 0x7c,
        _ => throw Invalid(),
    };
}
