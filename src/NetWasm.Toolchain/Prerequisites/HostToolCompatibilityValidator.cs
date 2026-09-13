using System;
using System.Collections.Immutable;

namespace NetWasm.Toolchain.Prerequisites;

public sealed class HostToolCompatibilityValidator : IHostToolCompatibilityValidator
{
    private readonly HostToolCompatibilityRequirement _requirement;
    private readonly ImmutableHashSet<string> _requiredCapabilities;

    public HostToolCompatibilityValidator(HostToolCompatibilityRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        ArgumentException.ThrowIfNullOrWhiteSpace(requirement.ToolId);
        ArgumentNullException.ThrowIfNull(requirement.MinimumVersionInclusive);
        if (requirement.MaximumVersionExclusive is not null &&
            requirement.MaximumVersionExclusive <= requirement.MinimumVersionInclusive)
        {
            throw new ArgumentException(
                "The exclusive maximum version must be greater than the minimum version.",
                nameof(requirement));
        }

        _requiredCapabilities = ValidateCapabilities(
            requirement.RequiredCapabilities,
            nameof(requirement));
        _requirement = requirement;
    }

    public ValidatedHostToolCompatibility Validate(HostToolCompatibilityObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentException.ThrowIfNullOrWhiteSpace(observation.ToolId);
        ArgumentNullException.ThrowIfNull(observation.Version);
        var observedCapabilities = ValidateCapabilities(
            observation.Capabilities,
            nameof(observation));

        if (!string.Equals(observation.ToolId, _requirement.ToolId, StringComparison.Ordinal))
        {
            throw new HostToolCompatibilityException(
                _requirement.ToolId,
                HostToolCompatibilityFailure.ToolIdentityMismatch,
                $"The compatibility observation is for '{observation.ToolId}', not '{_requirement.ToolId}'.");
        }

        if (observation.Version < _requirement.MinimumVersionInclusive)
        {
            throw new HostToolCompatibilityException(
                _requirement.ToolId,
                HostToolCompatibilityFailure.VersionBelowMinimum,
                $"Host tool '{_requirement.ToolId}' must be at least version {_requirement.MinimumVersionInclusive}.");
        }

        if (_requirement.MaximumVersionExclusive is not null &&
            observation.Version >= _requirement.MaximumVersionExclusive)
        {
            throw new HostToolCompatibilityException(
                _requirement.ToolId,
                HostToolCompatibilityFailure.VersionAtOrAboveMaximum,
                $"Host tool '{_requirement.ToolId}' must be older than version {_requirement.MaximumVersionExclusive}.");
        }

        foreach (var capability in _requiredCapabilities)
        {
            if (!observedCapabilities.Contains(capability))
            {
                throw new HostToolCompatibilityException(
                    _requirement.ToolId,
                    HostToolCompatibilityFailure.MissingCapability,
                    $"Host tool '{_requirement.ToolId}' does not provide required capability '{capability}'.");
            }
        }

        return new(observation.ToolId, observation.Version, observation.Capabilities);
    }

    private static ImmutableHashSet<string> ValidateCapabilities(
        ImmutableArray<string> capabilities,
        string parameterName)
    {
        if (capabilities.IsDefault)
        {
            throw new ArgumentException("Tool capabilities must be explicit.", parameterName);
        }

        var validated = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        foreach (var capability in capabilities)
        {
            if (string.IsNullOrWhiteSpace(capability) || !validated.Add(capability))
            {
                throw new ArgumentException(
                    "Tool capabilities must be nonempty and unique.",
                    parameterName);
            }
        }

        return validated.ToImmutable();
    }
}
