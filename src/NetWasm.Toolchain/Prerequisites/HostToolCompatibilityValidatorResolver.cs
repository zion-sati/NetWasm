using System;
using System.Collections.Immutable;

namespace NetWasm.Toolchain.Prerequisites;

public sealed record HostToolCompatibilityValidatorRegistration(
    string ToolId,
    IHostToolCompatibilityValidator Validator);

public sealed class HostToolCompatibilityValidatorResolver : IHostToolCompatibilityValidatorResolver
{
    private readonly ImmutableDictionary<string, IHostToolCompatibilityValidator> _validators;

    public HostToolCompatibilityValidatorResolver(
        ImmutableArray<string> requiredToolIds,
        ImmutableArray<HostToolCompatibilityValidatorRegistration> registrations)
    {
        var required = ValidateRequiredToolIds(requiredToolIds);
        _validators = BuildValidatorMap(registrations);
        foreach (var toolId in required)
        {
            if (!_validators.ContainsKey(toolId))
            {
                throw new ArgumentException(
                    $"The host tool compatibility validator for '{toolId}' is missing.",
                    nameof(registrations));
            }
        }
    }

    public IHostToolCompatibilityValidator Resolve(string toolId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolId);
        if (!_validators.TryGetValue(toolId, out var validator))
        {
            throw new UnsupportedHostToolException(toolId);
        }

        return validator;
    }

    private static ImmutableHashSet<string> ValidateRequiredToolIds(
        ImmutableArray<string> requiredToolIds)
    {
        if (requiredToolIds.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                "At least one required host tool must be explicit.",
                nameof(requiredToolIds));
        }

        var required = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        foreach (var toolId in requiredToolIds)
        {
            if (string.IsNullOrWhiteSpace(toolId) || !required.Add(toolId))
            {
                throw new ArgumentException(
                    "Required host tool identifiers must be nonempty and unique.",
                    nameof(requiredToolIds));
            }
        }

        return required.ToImmutable();
    }

    private static ImmutableDictionary<string, IHostToolCompatibilityValidator> BuildValidatorMap(
        ImmutableArray<HostToolCompatibilityValidatorRegistration> registrations)
    {
        if (registrations.IsDefault)
        {
            throw new ArgumentException(
                "Host tool compatibility validator registrations must be explicit.",
                nameof(registrations));
        }

        var validators = ImmutableDictionary.CreateBuilder<string, IHostToolCompatibilityValidator>(
            StringComparer.Ordinal);
        foreach (var registration in registrations)
        {
            if (registration is null ||
                string.IsNullOrWhiteSpace(registration.ToolId) ||
                registration.Validator is null)
            {
                throw new ArgumentException(
                    "Every host tool compatibility validator registration must be complete.",
                    nameof(registrations));
            }

            if (validators.ContainsKey(registration.ToolId))
            {
                throw new ArgumentException(
                    $"The host tool compatibility validator key '{registration.ToolId}' is duplicated.",
                    nameof(registrations));
            }

            validators.Add(registration.ToolId, registration.Validator);
        }

        return validators.ToImmutable();
    }
}
