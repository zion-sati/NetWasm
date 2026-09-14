using System.Collections.Immutable;
using NetWasm.Toolchain.Prerequisites;

namespace NetWasm.Toolchain.Tests;

public sealed class HostToolCompatibilityValidatorResolverTests
{
    [Fact]
    public void ResolveReturnsEveryRequiredKeyedStrategy()
    {
        var validators = HostToolIds.Required.ToImmutableDictionary(
            static toolId => toolId,
            static toolId => new RecordingValidator(toolId),
            StringComparer.Ordinal);
        var resolver = Assert.IsAssignableFrom<IHostToolCompatibilityValidatorResolver>(
            new HostToolCompatibilityValidatorResolver(
                HostToolIds.Required,
                [.. validators.Select(static pair => new HostToolCompatibilityValidatorRegistration(
                    pair.Key,
                    pair.Value))]));

        foreach (var pair in validators)
        {
            Assert.Same(pair.Value, resolver.Resolve(pair.Key));
        }
    }

    [Fact]
    public void ResolveAllowsAdditionalRegisteredStrategies()
    {
        var required = new RecordingValidator(HostToolIds.Node);
        var additional = new RecordingValidator("future-tool");
        var resolver = Assert.IsAssignableFrom<IHostToolCompatibilityValidatorResolver>(
            new HostToolCompatibilityValidatorResolver(
                [HostToolIds.Node],
                [
                    new(HostToolIds.Node, required),
                    new("future-tool", additional),
                ]));

        Assert.Same(additional, resolver.Resolve("future-tool"));
    }

    [Fact]
    public void ConstructorRejectsMissingOrInvalidRequiredKeys()
    {
        var registration = new HostToolCompatibilityValidatorRegistration(
            HostToolIds.Node,
            new RecordingValidator(HostToolIds.Node));

        Assert.Throws<ArgumentException>(() => new HostToolCompatibilityValidatorResolver(
            default,
            [registration]));
        Assert.Throws<ArgumentException>(() => new HostToolCompatibilityValidatorResolver(
            [],
            [registration]));
        Assert.Throws<ArgumentException>(() => new HostToolCompatibilityValidatorResolver(
            [" "],
            [registration]));
        Assert.Throws<ArgumentException>(() => new HostToolCompatibilityValidatorResolver(
            [HostToolIds.Node, HostToolIds.Node],
            [registration]));
        Assert.Throws<ArgumentException>(() => new HostToolCompatibilityValidatorResolver(
            [HostToolIds.Node, HostToolIds.WasmTools],
            [registration]));
    }

    [Fact]
    public void ConstructorRejectsInvalidOrDuplicateRegistrations()
    {
        var validator = new RecordingValidator(HostToolIds.Node);
        HostToolCompatibilityValidatorRegistration? nullRegistration = null;

        Assert.Throws<ArgumentException>(() => new HostToolCompatibilityValidatorResolver(
            [HostToolIds.Node],
            default));
        Assert.Throws<ArgumentException>(() => new HostToolCompatibilityValidatorResolver(
            [HostToolIds.Node],
            [nullRegistration!]));
        Assert.Throws<ArgumentException>(() => new HostToolCompatibilityValidatorResolver(
            [HostToolIds.Node],
            [new(" ", validator)]));
        Assert.Throws<ArgumentException>(() => new HostToolCompatibilityValidatorResolver(
            [HostToolIds.Node],
            [new(HostToolIds.Node, null!)]));
        Assert.Throws<ArgumentException>(() => new HostToolCompatibilityValidatorResolver(
            [HostToolIds.Node],
            [new(HostToolIds.Node, validator), new(HostToolIds.Node, validator)]));
    }

    [Fact]
    public void ResolveRejectsMissingKeys()
    {
        var resolver = Assert.IsAssignableFrom<IHostToolCompatibilityValidatorResolver>(
            new HostToolCompatibilityValidatorResolver(
                [HostToolIds.Node],
                [new(HostToolIds.Node, new RecordingValidator(HostToolIds.Node))]));

        Assert.Throws<ArgumentNullException>(() => resolver.Resolve(null!));
        Assert.Throws<ArgumentException>(() => resolver.Resolve(" "));
        var exception = Assert.Throws<UnsupportedHostToolException>(() =>
            resolver.Resolve(HostToolIds.WasmTools));
        Assert.Equal(HostToolIds.WasmTools, exception.ToolId);
    }

    private sealed class RecordingValidator(string toolId) : IHostToolCompatibilityValidator
    {
        public ValidatedHostToolCompatibility Validate(HostToolCompatibilityObservation observation)
            => new(toolId, observation.Version, observation.Capabilities);
    }
}
