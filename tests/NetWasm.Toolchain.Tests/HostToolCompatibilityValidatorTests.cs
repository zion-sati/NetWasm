using System.Collections.Immutable;
using NetWasm.Toolchain.Prerequisites;

namespace NetWasm.Toolchain.Tests;

public sealed class HostToolCompatibilityValidatorTests
{
    [Fact]
    public void ValidateReturnsTheExactAcceptedObservation()
    {
        var validator = Assert.IsAssignableFrom<IHostToolCompatibilityValidator>(
            new HostToolCompatibilityValidator(
                Requirement(requiredCapabilities: ["esm", "wasm-imports"])));
        var observation = Observation(
            new Version(24, 4, 1),
            ["wasm-imports", "extra", "esm"]);

        var result = validator.Validate(observation);

        Assert.Equal(observation.ToolId, result.ToolId);
        Assert.Same(observation.Version, result.Version);
        Assert.Equal(observation.Capabilities, result.Capabilities);
    }

    [Fact]
    public void ValidateAcceptsTheInclusiveMinimumAndAnOpenMaximum()
    {
        var validator = Assert.IsAssignableFrom<IHostToolCompatibilityValidator>(
            new HostToolCompatibilityValidator(
                new HostToolCompatibilityRequirement(
                    HostToolIds.Node,
                    new Version(24, 0, 0),
                    null,
                    [])));
        var observation = Observation(new Version(24, 0, 0), []);

        var result = validator.Validate(observation);

        Assert.Equal(new Version(24, 0, 0), result.Version);
        Assert.Empty(result.Capabilities);
    }

    [Fact]
    public void ConstructorRejectsInvalidRequirements()
    {
        Assert.Throws<ArgumentNullException>(() => new HostToolCompatibilityValidator(null!));
        Assert.Throws<ArgumentException>(() => new HostToolCompatibilityValidator(
            Requirement(toolId: " ")));
        Assert.Throws<ArgumentNullException>(() => new HostToolCompatibilityValidator(
            new HostToolCompatibilityRequirement(
                HostToolIds.Node,
                null!,
                new Version(27, 0, 0),
                ["esm"])));
        Assert.Throws<ArgumentException>(() => new HostToolCompatibilityValidator(
            Requirement(minimum: new(24, 0, 0), maximum: new(24, 0, 0))));
        Assert.Throws<ArgumentException>(() => new HostToolCompatibilityValidator(
            Requirement(minimum: new(25, 0, 0), maximum: new(24, 0, 0))));
        Assert.Throws<ArgumentException>(() => new HostToolCompatibilityValidator(
            new HostToolCompatibilityRequirement(
                HostToolIds.Node,
                new Version(24, 0, 0),
                new Version(27, 0, 0),
                default)));
        Assert.Throws<ArgumentException>(() => new HostToolCompatibilityValidator(
            Requirement(requiredCapabilities: [" "])));
        Assert.Throws<ArgumentException>(() => new HostToolCompatibilityValidator(
            Requirement(requiredCapabilities: ["esm", "esm"])));
    }

    [Fact]
    public void ValidateRejectsInvalidObservationsBeforeCompatibilityChecks()
    {
        var validator = Assert.IsAssignableFrom<IHostToolCompatibilityValidator>(
            new HostToolCompatibilityValidator(Requirement()));

        Assert.Throws<ArgumentNullException>(() => validator.Validate(null!));
        Assert.Throws<ArgumentException>(() => validator.Validate(Observation(toolId: " ")));
        Assert.Throws<ArgumentNullException>(() => validator.Validate(
            new HostToolCompatibilityObservation(HostToolIds.Node, null!, ["esm"])));
        Assert.Throws<ArgumentException>(() => validator.Validate(
            new HostToolCompatibilityObservation(
                HostToolIds.Node,
                new Version(26, 0, 0),
                default)));
        Assert.Throws<ArgumentException>(() => validator.Validate(Observation(capabilities: [" "])));
        Assert.Throws<ArgumentException>(() => validator.Validate(Observation(capabilities: ["esm", "esm"])));
    }

    [Fact]
    public void ValidateRejectsAnObservationForAnotherTool()
    {
        var validator = Assert.IsAssignableFrom<IHostToolCompatibilityValidator>(
            new HostToolCompatibilityValidator(Requirement()));

        var exception = Assert.Throws<HostToolCompatibilityException>(() =>
            validator.Validate(Observation(toolId: HostToolIds.WasmTools)));

        Assert.Equal(HostToolIds.Node, exception.ToolId);
        Assert.Equal(HostToolCompatibilityFailure.ToolIdentityMismatch, exception.Failure);
    }

    [Fact]
    public void ValidateRejectsAVersionBelowTheMinimum()
    {
        var validator = Assert.IsAssignableFrom<IHostToolCompatibilityValidator>(
            new HostToolCompatibilityValidator(Requirement()));

        var exception = Assert.Throws<HostToolCompatibilityException>(() =>
            validator.Validate(Observation(version: new(23, 9, 9))));

        Assert.Equal(HostToolCompatibilityFailure.VersionBelowMinimum, exception.Failure);
    }

    [Theory]
    [InlineData(27, 0, 0)]
    [InlineData(28, 0, 0)]
    public void ValidateRejectsAVersionAtOrAboveTheMaximum(int major, int minor, int build)
    {
        var validator = Assert.IsAssignableFrom<IHostToolCompatibilityValidator>(
            new HostToolCompatibilityValidator(Requirement()));

        var exception = Assert.Throws<HostToolCompatibilityException>(() =>
            validator.Validate(Observation(version: new(major, minor, build))));

        Assert.Equal(HostToolCompatibilityFailure.VersionAtOrAboveMaximum, exception.Failure);
    }

    [Fact]
    public void ValidateRejectsTheFirstMissingRequiredCapability()
    {
        var validator = Assert.IsAssignableFrom<IHostToolCompatibilityValidator>(
            new HostToolCompatibilityValidator(
                Requirement(requiredCapabilities: ["esm", "wasm-imports"])));

        var exception = Assert.Throws<HostToolCompatibilityException>(() =>
            validator.Validate(Observation(capabilities: ["wasm-imports"])));

        Assert.Equal(HostToolCompatibilityFailure.MissingCapability, exception.Failure);
    }

    private static HostToolCompatibilityRequirement Requirement(
        string toolId = HostToolIds.Node,
        Version? minimum = null,
        Version? maximum = null,
        ImmutableArray<string>? requiredCapabilities = null)
        => new(
            toolId,
            minimum ?? new Version(24, 0, 0),
            maximum ?? new Version(27, 0, 0),
            requiredCapabilities ?? ["esm"]);

    private static HostToolCompatibilityObservation Observation(
        Version? version = null,
        ImmutableArray<string>? capabilities = null,
        string toolId = HostToolIds.Node)
        => new(
            toolId,
            version ?? new Version(26, 0, 0),
            capabilities ?? ["esm"]);
}
