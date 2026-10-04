using System.Collections.Immutable;
using NetWasm.Runtime.Pack.Materialization;
using NetWasm.Runtime.Pack.Tests.TestSupport;

namespace NetWasm.Runtime.Pack.Tests.Materialization;

public sealed class RuntimeNativeModuleValidatorTests
{
    [Fact]
    public void UsesTheInjectedProfileBuilderAndPinnedToolContractWithoutReinterpretingFailures()
    {
        var fixture = new Fixture();
        var validator = new RuntimeNativeModuleValidator(fixture, fixture);
        var request = Request();
        validator.Validate(request);

        Assert.Equal((request.Profile, request.Target, request.Path), Assert.Single(fixture.Builds));
        Assert.Equal(new[] { "--disable-warning=ExperimentalWarning", request.CommandPath, request.ModulePath,
            "validate", request.Path, "--features=mvp" }, Assert.Single(fixture.Commands).Arguments);
        Assert.Equal(request.NodePath, fixture.Commands[0].ExecutablePath);
        Assert.Equal(request.LogPath, fixture.Commands[0].LogPath);
        var failure = new InvalidOperationException("tool failure");
        fixture.ToolFailure = failure;
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => validator.Validate(request)));
    }

    [Fact]
    public void RejectsMissingDependenciesAndRequestPathsBeforeInvokingCollaborators()
    {
        var fixture = new Fixture();
        Assert.Throws<ArgumentNullException>(() => new RuntimeNativeModuleValidator(null!, fixture));
        Assert.Throws<ArgumentNullException>(() => new RuntimeNativeModuleValidator(fixture, null!));
        var validator = new RuntimeNativeModuleValidator(fixture, fixture);
        Assert.Throws<ArgumentNullException>(() => validator.Validate(null!));
        var request = Request();
        foreach (var invalid in new[]
        {
            request with { NodePath = " " }, request with { CommandPath = "" },
            request with { ModulePath = null! }, request with { LogPath = " " },
        })
            Assert.ThrowsAny<ArgumentException>(() => validator.Validate(invalid));
        Assert.Empty(fixture.Builds);
        Assert.Empty(fixture.Commands);
        fixture.BuildFailure = new InvalidOperationException("invalid profile");
        Assert.Same(fixture.BuildFailure, Assert.Throws<InvalidOperationException>(() => validator.Validate(request)));
        Assert.Empty(fixture.Commands);
    }

    private static RuntimeNativeModuleValidationRequest Request() => new(RuntimePackTestData.NativeProfile(), "wasm32",
        "/owned/module.wasm", "/tools/node", "/tools/command.mjs", "/tools/module.wasm", "/owned/log.txt");

    private sealed class Fixture : IRuntimeNativeValidationArgumentBuilder, ICommandInvoker
    {
        public List<(RuntimeNativeValidationProfile Profile, string Target, string Path)> Builds { get; } = [];
        public List<RuntimeCommand> Commands { get; } = [];
        public Exception? BuildFailure { get; set; }
        public Exception? ToolFailure { get; set; }
        public ImmutableArray<string> Build(RuntimeNativeValidationProfile profile, string target, string path)
        {
            Builds.Add((profile, target, path));
            if (BuildFailure is not null) throw BuildFailure;
            return ["validate", path, "--features=mvp"];
        }
        public void Invoke(RuntimeCommand command)
        {
            Commands.Add(command);
            if (ToolFailure is not null) throw ToolFailure;
        }
    }
}
