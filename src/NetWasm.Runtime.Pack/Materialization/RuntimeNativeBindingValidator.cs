using System;
using System.Collections.Generic;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Linq;

namespace NetWasm.Runtime.Pack.Materialization;

internal sealed class RuntimeNativeBindingValidator : IRuntimeNativeBindingValidator
{
    private static readonly FrozenDictionary<RuntimeNativeValueType, byte> PhysicalValueTypes =
        new Dictionary<RuntimeNativeValueType, byte>
        {
            [RuntimeNativeValueType.I32] = 0x7f,
            [RuntimeNativeValueType.I64] = 0x7e,
            [RuntimeNativeValueType.F32] = 0x7d,
            [RuntimeNativeValueType.F64] = 0x7c,
        }.ToFrozenDictionary();

    public ImmutableArray<RuntimeValidatedNativeBinding> Validate(RuntimeNativeCachedBindingValidationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Module);
        if (request.Bindings.IsDefault || request.Evidence.IsDefault || request.Bindings.Length != request.Evidence.Length)
            throw Invalid("The cached native provider evidence is incomplete.");
        ValidateBindings(request.Bindings, request.Module);
        // Stored provenance is integrity-bound by the cache envelope. Match it to current
        // provider bytes before re-proving defined exports and their physical signatures.
        for (var index = 0; index < request.Bindings.Length; index++)
        {
            var binding = request.Bindings[index];
            var evidence = request.Evidence[index];
            if (binding is null || binding.Import is null || binding.Provider is null || evidence is null ||
                evidence.EntryPoint != binding.Import.EntryPoint || evidence.ProviderPath != binding.Provider.Path ||
                evidence.ProviderSha256 != binding.Provider.Sha256 || InvalidIdentity(evidence.ArchiveMemberName))
                throw Invalid("The cached native provider evidence does not match current bindings.");
            ValidateDefinedExport(binding.Import, request.Module);
        }
        return request.Evidence;
    }

    public ImmutableArray<RuntimeValidatedNativeBinding> Validate(RuntimeNativeBindingValidationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Module);
        if (request.PermittedGeneratedInputIdentities.IsDefault || request.Events.IsDefault)
            throw Invalid("The native binding validation request is incomplete.");
        var bindings = ValidateBindings(request.Bindings, request.Module);
        var generated = request.PermittedGeneratedInputIdentities.ToHashSet(StringComparer.Ordinal);
        if (generated.Count != request.PermittedGeneratedInputIdentities.Length || generated.Any(InvalidIdentity))
            throw Invalid("The native generated-input identity contract is invalid.");
        if (request.Events.Any(item => item is null || !bindings.ContainsKey(item.EntryPoint) ||
                InvalidIdentity(item.InputIdentity) || string.IsNullOrEmpty(item.EntryPoint) || !Enum.IsDefined(item.Kind)))
            throw Invalid("The native linker symbol trace is inconsistent with the selected bindings.");

        var results = ImmutableArray.CreateBuilder<RuntimeValidatedNativeBinding>(request.Bindings.Length);
        foreach (var binding in request.Bindings)
        {
            var member = ValidateProvenance(binding, generated, request.Events);
            ValidateDefinedExport(binding.Import, request.Module);
            results.Add(new(binding.Import.EntryPoint, binding.Provider.Path,
                binding.Provider.Sha256, member));
        }
        return results.ToImmutable();
    }

    private static Dictionary<string, RuntimeNativeBinding> ValidateBindings(
        ImmutableArray<RuntimeNativeBinding> inputs, RuntimeLinkedModule module)
    {
        if (inputs.IsDefault || module.FunctionTypes.IsDefault || module.ImportedFunctionTypeIndices.IsDefault ||
            module.DefinedFunctionTypeIndices.IsDefault || module.Exports.IsDefault)
            throw Invalid("The native binding validation request is incomplete.");
        var bindings = new Dictionary<string, RuntimeNativeBinding>(StringComparer.Ordinal);
        foreach (var binding in inputs)
        {
            if (binding is null || binding.Import is null || binding.Provider is null ||
                InvalidIdentity(binding.Import.EntryPoint) || InvalidIdentity(binding.Provider.Path) ||
                binding.Import.Parameters.IsDefault ||
                binding.Import.Parameters.Any(type => !PhysicalValueTypes.ContainsKey(type)) ||
                binding.Import.ReturnType is { } result && !PhysicalValueTypes.ContainsKey(result) ||
                binding.Provider.Sha256 is not { Length: 64 } || !binding.Provider.Sha256.All(Uri.IsHexDigit) ||
                !bindings.TryAdd(binding.Import.EntryPoint, binding))
                throw Invalid("The native binding validation request contains duplicate or missing bindings.");
        }
        return bindings;
    }

    private static string ValidateProvenance(
        RuntimeNativeBinding binding,
        HashSet<string> generated,
        ImmutableArray<RuntimeLinkerSymbolEvent> events)
    {
        string? member = null;
        var generatedDefinition = false;
        foreach (var item in events.Where(item => item.EntryPoint == binding.Import.EntryPoint))
        {
            if (item.Kind is RuntimeLinkerSymbolEventKind.Reference or RuntimeLinkerSymbolEventKind.LazyDefinition)
                continue;
            if (TryArchiveMember(item.InputIdentity, binding.Provider.Path, out var candidate))
            {
                if (member is not null || generatedDefinition)
                    throw Invalid("The selected native symbol has competing original definitions.");
                member = candidate;
                continue;
            }
            if (generated.Contains(item.InputIdentity))
            {
                if (member is null || generatedDefinition)
                    throw Invalid("The native LTO definition does not follow one selected original definition.");
                generatedDefinition = true;
                continue;
            }
            throw Invalid("The selected native symbol resolved from an unexpected provider.");
        }
        return member ?? throw Invalid("The selected native symbol has no accepted archive definition.");
    }

    private static void ValidateDefinedExport(RuntimeNativeImport import, RuntimeLinkedModule module)
    {
        var matches = module.Exports.Where(export => export.Name == import.EntryPoint).ToArray();
        if (matches.Length != 1 || matches[0].Kind != 0 || matches[0].Index < module.ImportedFunctionTypeIndices.Length)
            throw Invalid("The selected native symbol is not a uniquely defined function export.");
        var definedIndex = matches[0].Index - checked((uint)module.ImportedFunctionTypeIndices.Length);
        if (definedIndex >= module.DefinedFunctionTypeIndices.Length)
            throw Invalid("The selected native function export index is invalid.");
        var typeIndex = module.DefinedFunctionTypeIndices[(int)definedIndex];
        if (typeIndex >= module.FunctionTypes.Length)
            throw Invalid("The selected native function type index is invalid.");
        var type = module.FunctionTypes[(int)typeIndex];
        var expectedParameters = import.Parameters.Select(ValueType).ToImmutableArray();
        var expectedResults = import.ReturnType is { } result
            ? ImmutableArray.Create(ValueType(result))
            : ImmutableArray<byte>.Empty;
        if (!type.Parameters.SequenceEqual(expectedParameters) || !type.Results.SequenceEqual(expectedResults))
            throw Invalid("The selected native function export has a different physical signature.");
    }

    private static byte ValueType(RuntimeNativeValueType value) => PhysicalValueTypes[value];

    private static bool TryArchiveMember(string identity, string archivePath, out string member)
    {
        var prefix = archivePath + "(";
        if (identity.StartsWith(prefix, StringComparison.Ordinal) && identity.EndsWith(')') &&
            identity.Length > prefix.Length + 1)
        {
            member = identity[prefix.Length..^1];
            return !InvalidIdentity(member);
        }
        member = string.Empty;
        return false;
    }

    private static bool InvalidIdentity(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.IndexOfAny(['\0', '\r', '\n']) >= 0;

    private static InvalidOperationException Invalid(string message) => new(message);
}
