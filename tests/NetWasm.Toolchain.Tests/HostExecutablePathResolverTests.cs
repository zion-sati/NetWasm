using System.Collections.Immutable;
using NetWasm.Toolchain.Prerequisites;
using NetWasm.Toolchain.Resolution;

namespace NetWasm.Toolchain.Tests;

public sealed class HostExecutablePathResolverTests
{
    private const string OverrideName = "NETWASM_NODE_PATH";
    private static readonly string FirstDirectory = Path.Combine(Path.GetTempPath(), "netwasm-tools-first");
    private static readonly string SecondDirectory = Path.Combine(Path.GetTempPath(), "netwasm-tools-second");

    [Fact]
    public void ResolveUsesPathOrderAndReturnsTheCanonicalAbsoluteCandidate()
    {
        var path = string.Join(Path.PathSeparator, FirstDirectory, SecondDirectory);
        var environment = new RecordingEnvironment(("PATH", path));
        var expected = Path.GetFullPath(Path.Combine(SecondDirectory, "node"));
        var presence = new RecordingPresenceChecker(expected);
        var resolver = Assert.IsAssignableFrom<IHostExecutablePathResolver>(
            CreateResolver(environment, presence));

        var result = resolver.Resolve(Request());

        Assert.Equal(HostToolIds.Node, result.ToolId);
        Assert.Equal(expected, result.AbsolutePath);
        Assert.Equal(HostExecutableResolutionSource.Path, result.Source);
        Assert.Equal([OverrideName, "PATH"], environment.ReadNames);
        Assert.Equal(
            [Path.GetFullPath(Path.Combine(FirstDirectory, "node")), expected],
            presence.CheckedPaths);
    }

    [Fact]
    public void ResolvePreservesFirstPathMatchAndCollapsesEquivalentCandidates()
    {
        var duplicateDirectory = FirstDirectory.ToUpperInvariant();
        var path = string.Join(';', FirstDirectory, duplicateDirectory, SecondDirectory);
        var expected = Path.GetFullPath(Path.Combine(FirstDirectory, "node.exe"));
        var environment = new RecordingEnvironment(("PATH", path));
        var presence = new RecordingPresenceChecker(
            expected,
            Path.GetFullPath(Path.Combine(SecondDirectory, "node.exe")));
        var resolver = Assert.IsAssignableFrom<IHostExecutablePathResolver>(
            CreateResolver(
                environment,
                presence,
                new(';', ".exe", StringComparison.OrdinalIgnoreCase)));

        var result = resolver.Resolve(Request());

        Assert.Equal(expected, result.AbsolutePath);
        Assert.Equal([expected], presence.CheckedPaths);
    }

    [Fact]
    public void ResolveDoesNotAppendAnExistingPlatformSuffix()
    {
        var environment = new RecordingEnvironment(("PATH", FirstDirectory));
        var expected = Path.GetFullPath(Path.Combine(FirstDirectory, "node.EXE"));
        var presence = new RecordingPresenceChecker(expected);
        var resolver = Assert.IsAssignableFrom<IHostExecutablePathResolver>(
            CreateResolver(
                environment,
                presence,
                new(Path.PathSeparator, ".exe", StringComparison.OrdinalIgnoreCase)));

        var result = resolver.Resolve(Request("node.EXE"));

        Assert.Equal(expected, result.AbsolutePath);
    }

    [Fact]
    public void ResolveUsesAnExplicitOverrideWithoutReadingOrFallingBackToPath()
    {
        var configured = Path.Combine(FirstDirectory, "parent", "..", "node");
        var expected = Path.GetFullPath(Path.Combine(FirstDirectory, "node"));
        var environment = new RecordingEnvironment(
            (OverrideName, configured),
            ("PATH", SecondDirectory));
        var presence = new RecordingPresenceChecker(expected);
        var resolver = Assert.IsAssignableFrom<IHostExecutablePathResolver>(
            CreateResolver(environment, presence));

        var result = resolver.Resolve(Request());

        Assert.Equal(expected, result.AbsolutePath);
        Assert.Equal(HostExecutableResolutionSource.Override, result.Source);
        Assert.Equal([OverrideName], environment.ReadNames);
        Assert.Equal([expected], presence.CheckedPaths);
    }

    [Fact]
    public void ResolveUsesAnActivatedSdkExecutableBeforePath()
    {
        var expected = Path.GetFullPath(Path.Combine(FirstDirectory, "node"));
        var pathCandidate = Path.GetFullPath(Path.Combine(SecondDirectory, "node"));
        var environment = new RecordingEnvironment(
            ("EMSDK_NODE", expected),
            ("PATH", SecondDirectory));
        var presence = new RecordingPresenceChecker(expected, pathCandidate);
        var resolver = Assert.IsAssignableFrom<IHostExecutablePathResolver>(
            CreateResolver(environment, presence));

        var result = resolver.Resolve(Request(
            environmentFallbacks: [new("EMSDK_NODE")]));

        Assert.Equal(expected, result.AbsolutePath);
        Assert.Equal(HostExecutableResolutionSource.EnvironmentExecutable, result.Source);
        Assert.Equal([OverrideName, "EMSDK_NODE"], environment.ReadNames);
        Assert.Equal([expected], presence.CheckedPaths);
    }

    [Theory]
    [InlineData("relative/node")]
    [InlineData("missing")]
    public void ResolveRejectsInvalidActivatedSdkExecutable(string configured)
    {
        var environment = new RecordingEnvironment(
            ("EMSDK_NODE", configured),
            ("PATH", SecondDirectory));
        var resolver = Assert.IsAssignableFrom<IHostExecutablePathResolver>(
            CreateResolver(environment, new RecordingPresenceChecker()));

        var exception = Assert.Throws<HostExecutableResolutionException>(() =>
            resolver.Resolve(Request(environmentFallbacks: [new("EMSDK_NODE")])));

        Assert.Equal(HostExecutableResolutionFailure.InvalidFallback, exception.Failure);
        Assert.Equal([OverrideName, "EMSDK_NODE"], environment.ReadNames);
    }

    [Fact]
    public void ResolveRejectsMissingActivatedSdkExecutableBeforePath()
    {
        var configured = Path.GetFullPath(Path.Combine(FirstDirectory, "node"));
        var environment = new RecordingEnvironment(
            ("EMSDK_NODE", configured),
            ("PATH", SecondDirectory));
        var pathCandidate = Path.GetFullPath(Path.Combine(SecondDirectory, "node"));
        var presence = new RecordingPresenceChecker(pathCandidate);
        var resolver = Assert.IsAssignableFrom<IHostExecutablePathResolver>(
            CreateResolver(environment, presence));

        var exception = Assert.Throws<HostExecutableResolutionException>(() =>
            resolver.Resolve(Request(environmentFallbacks: [new("EMSDK_NODE")])));

        Assert.Equal(HostExecutableResolutionFailure.ExecutableNotFound, exception.Failure);
        Assert.Equal([OverrideName, "EMSDK_NODE"], environment.ReadNames);
        Assert.Equal([configured], presence.CheckedPaths);
    }

    [Fact]
    public void ResolveSkipsAnEmptyActivatedSdkExecutableAndReportsTheSelection()
    {
        var environment = new RecordingEnvironment(
            ("EMSDK_NODE", " "),
            ("PATH", null));
        var resolver = Assert.IsAssignableFrom<IHostExecutablePathResolver>(
            CreateResolver(environment, new RecordingPresenceChecker()));

        var exception = Assert.Throws<HostExecutableResolutionException>(() =>
            resolver.Resolve(Request(environmentFallbacks: [new("EMSDK_NODE")])));

        Assert.Equal(HostExecutableResolutionFailure.PathUnavailable, exception.Failure);
        Assert.Contains("EMSDK_NODE executable", exception.Message, StringComparison.Ordinal);
        Assert.Equal([OverrideName, "EMSDK_NODE", "PATH"], environment.ReadNames);
    }

    [Fact]
    public void ResolvePreservesActivatedSdkExecutableCanonicalizationFailure()
    {
        var cause = new ArgumentException("invalid activated SDK path");
        var configured = Path.GetFullPath(Path.Combine(FirstDirectory, "node"));
        var environment = new RecordingEnvironment(("EMSDK_NODE", configured));
        var resolver = Assert.IsAssignableFrom<IHostExecutablePathResolver>(
            CreateResolver(
                environment,
                new RecordingPresenceChecker(),
                canonicalizer: new ThrowingCanonicalizer(cause)));

        var exception = Assert.Throws<HostExecutableResolutionException>(() =>
            resolver.Resolve(Request(environmentFallbacks: [new("EMSDK_NODE")])));

        Assert.Equal(HostExecutableResolutionFailure.InvalidFallback, exception.Failure);
        Assert.Same(cause, exception.InnerException);
    }

    [Theory]
    [InlineData("")]
    [InlineData("relative/node")]
    public void ResolveRejectsInvalidOverridesWithoutReadingPath(string configured)
    {
        var environment = new RecordingEnvironment(
            (OverrideName, configured),
            ("PATH", FirstDirectory));
        var presence = new RecordingPresenceChecker();
        var resolver = Assert.IsAssignableFrom<IHostExecutablePathResolver>(
            CreateResolver(environment, presence));

        var exception = Assert.Throws<HostExecutableResolutionException>(() =>
            resolver.Resolve(Request()));

        Assert.Equal(HostToolIds.Node, exception.ToolId);
        Assert.Equal(HostExecutableResolutionFailure.InvalidOverride, exception.Failure);
        Assert.Equal([OverrideName], environment.ReadNames);
        Assert.Empty(presence.CheckedPaths);
    }

    [Fact]
    public void ResolveRejectsAMissingOverrideWithoutFallingBackToPath()
    {
        var configured = Path.Combine(FirstDirectory, "node");
        var environment = new RecordingEnvironment(
            (OverrideName, configured),
            ("PATH", SecondDirectory));
        var presence = new RecordingPresenceChecker(
            Path.GetFullPath(Path.Combine(SecondDirectory, "node")));
        var resolver = Assert.IsAssignableFrom<IHostExecutablePathResolver>(
            CreateResolver(environment, presence));

        var exception = Assert.Throws<HostExecutableResolutionException>(() =>
            resolver.Resolve(Request()));

        Assert.Equal(HostExecutableResolutionFailure.ExecutableNotFound, exception.Failure);
        Assert.Equal([OverrideName], environment.ReadNames);
        Assert.Single(presence.CheckedPaths);
    }

    [Fact]
    public void ResolveUsesPathBeforeAConfiguredInstallationRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "emsdk");
        var expected = Path.GetFullPath(Path.Combine(FirstDirectory, "wasm-ld"));
        var environment = new RecordingEnvironment(
            ("PATH", FirstDirectory),
            ("EMSDK", root));
        var presence = new RecordingPresenceChecker(expected);
        var resolver = Assert.IsAssignableFrom<IHostExecutablePathResolver>(
            CreateResolver(environment, presence));

        var result = resolver.Resolve(Request(
            executableName: "wasm-ld",
            toolId: HostToolIds.WasmLd,
            overrideName: "NETWASM_WASM_LD_PATH",
            fallbacks: [new("EMSDK", Path.Combine("upstream", "bin"))]));

        Assert.Equal(expected, result.AbsolutePath);
        Assert.Equal(HostExecutableResolutionSource.Path, result.Source);
        Assert.Equal(["NETWASM_WASM_LD_PATH", "PATH"], environment.ReadNames);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ResolveUsesTheConfiguredInstallationRootWhenPathCannotSupplyTheTool(
        string? path)
    {
        var root = Path.Combine(Path.GetTempPath(), "emsdk");
        var expected = Path.GetFullPath(
            Path.Combine(root, "upstream", "bin", "wasm-ld"));
        var environment = new RecordingEnvironment(
            ("PATH", path),
            ("EMSDK", root));
        var presence = new RecordingPresenceChecker(expected);
        var resolver = Assert.IsAssignableFrom<IHostExecutablePathResolver>(
            CreateResolver(environment, presence));

        var result = resolver.Resolve(Request(
            executableName: "wasm-ld",
            toolId: HostToolIds.WasmLd,
            overrideName: "NETWASM_WASM_LD_PATH",
            fallbacks: [new("EMSDK", Path.Combine("upstream", "bin"))]));

        Assert.Equal(expected, result.AbsolutePath);
        Assert.Equal(HostExecutableResolutionSource.EnvironmentRoot, result.Source);
        Assert.Equal(
            ["NETWASM_WASM_LD_PATH", "PATH", "EMSDK"],
            environment.ReadNames);
        Assert.Equal([expected], presence.CheckedPaths);
    }

    [Fact]
    public void ResolveRejectsAnInvalidConfiguredInstallationRoot()
    {
        var environment = new RecordingEnvironment(
            ("PATH", FirstDirectory),
            ("EMSDK", "relative"));
        var presence = new RecordingPresenceChecker();
        var resolver = Assert.IsAssignableFrom<IHostExecutablePathResolver>(
            CreateResolver(environment, presence));

        var exception = Assert.Throws<HostExecutableResolutionException>(() =>
            resolver.Resolve(Request(
                executableName: "wasm-ld",
                toolId: HostToolIds.WasmLd,
                overrideName: "NETWASM_WASM_LD_PATH",
                fallbacks: [new("EMSDK", Path.Combine("upstream", "bin"))])));

        Assert.Equal(HostExecutableResolutionFailure.InvalidFallback, exception.Failure);
        Assert.Equal(
            ["NETWASM_WASM_LD_PATH", "PATH", "EMSDK"],
            environment.ReadNames);
        Assert.Single(presence.CheckedPaths);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void ResolveRejectsAnUnavailablePath(string? path)
    {
        var environment = new RecordingEnvironment(("PATH", path));
        var presence = new RecordingPresenceChecker();
        var resolver = Assert.IsAssignableFrom<IHostExecutablePathResolver>(
            CreateResolver(environment, presence));

        var exception = Assert.Throws<HostExecutableResolutionException>(() =>
            resolver.Resolve(Request()));

        Assert.Equal(HostExecutableResolutionFailure.PathUnavailable, exception.Failure);
        Assert.Empty(presence.CheckedPaths);
    }

    [Fact]
    public void ResolveSkipsEmptyAndRelativePathEntriesAndUsesAbsoluteCandidates()
    {
        foreach (var path in new[]
                 {
                     string.Join(Path.PathSeparator, FirstDirectory, string.Empty, SecondDirectory),
                     string.Join(Path.PathSeparator, FirstDirectory, "relative", SecondDirectory),
                 })
        {
            var environment = new RecordingEnvironment(("PATH", path));
            var expected = Path.GetFullPath(Path.Combine(SecondDirectory, "node"));
            var presence = new RecordingPresenceChecker(expected);
            var resolver = Assert.IsAssignableFrom<IHostExecutablePathResolver>(
                CreateResolver(environment, presence));

            var result = resolver.Resolve(Request());

            Assert.Equal(expected, result.AbsolutePath);
            Assert.Equal(
                [Path.GetFullPath(Path.Combine(FirstDirectory, "node")), expected],
                presence.CheckedPaths);
        }
    }

    [Fact]
    public void ResolveReportsNoCandidateWhenPathContainsOnlyUnsafeEntries()
    {
        var path = string.Join(Path.PathSeparator, string.Empty, "relative");
        var environment = new RecordingEnvironment(("PATH", path));
        var presence = new RecordingPresenceChecker();
        var resolver = Assert.IsAssignableFrom<IHostExecutablePathResolver>(
            CreateResolver(environment, presence));

        var exception = Assert.Throws<HostExecutableResolutionException>(() =>
            resolver.Resolve(Request()));

        Assert.Equal(HostExecutableResolutionFailure.ExecutableNotFound, exception.Failure);
        Assert.Empty(presence.CheckedPaths);
    }

    [Fact]
    public void ResolvePreservesCanonicalizationFailuresAndReturnsNoCandidate()
    {
        var cause = new ArgumentException("invalid path");
        var environment = new RecordingEnvironment(("PATH", FirstDirectory));
        var presence = new RecordingPresenceChecker();
        var canonicalizer = new ThrowingCanonicalizer(cause);
        var resolver = Assert.IsAssignableFrom<IHostExecutablePathResolver>(
            CreateResolver(
                environment,
                presence,
                canonicalizer: canonicalizer));

        var exception = Assert.Throws<HostExecutableResolutionException>(() =>
            resolver.Resolve(Request()));

        Assert.Equal(HostExecutableResolutionFailure.InvalidPathEntry, exception.Failure);
        Assert.Same(cause, exception.InnerException);
        Assert.Empty(presence.CheckedPaths);
    }

    [Fact]
    public void ResolveReportsWhenNoPathCandidateExists()
    {
        var environment = new RecordingEnvironment(("PATH", FirstDirectory));
        var presence = new RecordingPresenceChecker();
        var resolver = Assert.IsAssignableFrom<IHostExecutablePathResolver>(
            CreateResolver(environment, presence));

        var exception = Assert.Throws<HostExecutableResolutionException>(() =>
            resolver.Resolve(Request()));

        Assert.Equal(HostExecutableResolutionFailure.ExecutableNotFound, exception.Failure);
        Assert.Single(presence.CheckedPaths);
    }

    [Fact]
    public void ResolveNamesTheEmsdkRootWhenTheLinkerCannotBeFound()
    {
        var environment = new RecordingEnvironment(("PATH", FirstDirectory));
        var resolver = Assert.IsAssignableFrom<IHostExecutablePathResolver>(
            CreateResolver(environment, new RecordingPresenceChecker()));

        var exception = Assert.Throws<HostExecutableResolutionException>(() =>
            resolver.Resolve(Request(
                executableName: "wasm-ld",
                toolId: HostToolIds.WasmLd,
                overrideName: "NETWASM_WASM_LD_PATH",
                fallbacks: [new("EMSDK", Path.Combine("upstream", "bin"))])));

        Assert.Equal(HostExecutableResolutionFailure.ExecutableNotFound, exception.Failure);
        Assert.Equal(
            "Cannot find 'wasm-ld' for host tool 'wasm-ld' on PATH or through the configured EMSDK installation root.",
            exception.Message);
    }

    [Fact]
    public void ConstructorRejectsInvalidDependenciesAndConventions()
    {
        var environment = new RecordingEnvironment();
        var presence = new RecordingPresenceChecker();
        var canonicalizer = new SystemHostPathCanonicalizer();
        var convention = Convention();

        Assert.Throws<ArgumentNullException>(() =>
            new HostExecutablePathResolver(null!, presence, canonicalizer, convention));
        Assert.Throws<ArgumentNullException>(() =>
            new HostExecutablePathResolver(environment, null!, canonicalizer, convention));
        Assert.Throws<ArgumentNullException>(() =>
            new HostExecutablePathResolver(environment, presence, null!, convention));
        Assert.Throws<ArgumentNullException>(() =>
            new HostExecutablePathResolver(environment, presence, canonicalizer, null!));
        Assert.Throws<ArgumentException>(() =>
            new HostExecutablePathResolver(environment, presence, canonicalizer,
                new('\0', string.Empty, StringComparison.Ordinal)));
        Assert.Throws<ArgumentException>(() =>
            new HostExecutablePathResolver(environment, presence, canonicalizer,
                new(Path.PathSeparator, null!, StringComparison.Ordinal)));
        Assert.Throws<ArgumentException>(() =>
            new HostExecutablePathResolver(environment, presence, canonicalizer,
                new(Path.PathSeparator, "exe", StringComparison.Ordinal)));
        Assert.Throws<ArgumentException>(() =>
            new HostExecutablePathResolver(environment, presence, canonicalizer,
                new(Path.PathSeparator, "./exe", StringComparison.Ordinal)));
        Assert.Throws<ArgumentException>(() =>
            new HostExecutablePathResolver(environment, presence, canonicalizer,
                new(Path.PathSeparator, ".\\exe", StringComparison.Ordinal)));
        Assert.Throws<ArgumentException>(() =>
            new HostExecutablePathResolver(environment, presence, canonicalizer,
                new(Path.PathSeparator, string.Empty, StringComparison.CurrentCulture)));
    }

    [Fact]
    public void ResolveRejectsInvalidRequestsBeforeReadingTheEnvironment()
    {
        var environment = new RecordingEnvironment();
        var presence = new RecordingPresenceChecker();
        var resolver = Assert.IsAssignableFrom<IHostExecutablePathResolver>(
            CreateResolver(environment, presence));

        Assert.Throws<ArgumentNullException>(() => resolver.Resolve(null!));
        Assert.Throws<ArgumentException>(() => resolver.Resolve(Request(toolId: " ")));
        Assert.Throws<ArgumentException>(() => resolver.Resolve(Request(executableName: " ")));
        Assert.Throws<ArgumentException>(() => resolver.Resolve(Request(executableName: ".")));
        Assert.Throws<ArgumentException>(() => resolver.Resolve(Request(executableName: "..")));
        Assert.Throws<ArgumentException>(() => resolver.Resolve(Request(executableName: "tools/node")));
        Assert.Throws<ArgumentException>(() => resolver.Resolve(Request(executableName: "tools\\node")));
        Assert.Throws<ArgumentException>(() => resolver.Resolve(Request(overrideName: " ")));
        Assert.Throws<ArgumentException>(() => resolver.Resolve(new(
            HostToolIds.Node,
            "node",
            OverrideName,
            [],
            default)));
        Assert.Throws<ArgumentException>(() => resolver.Resolve(new(
            HostToolIds.Node,
            "node",
            OverrideName,
            default,
            [])));
        Assert.Throws<ArgumentException>(() => resolver.Resolve(Request(
            environmentFallbacks: [new("")])));
        Assert.Throws<ArgumentNullException>(() => resolver.Resolve(Request(
            environmentFallbacks: [null!])));
        Assert.Throws<ArgumentException>(() => resolver.Resolve(Request(
            fallbacks: [new("EMSDK", "../bin")])));
        Assert.Throws<ArgumentException>(() => resolver.Resolve(Request(
            fallbacks: [new("", "bin")])));
        Assert.Empty(environment.ReadNames);
        Assert.Empty(presence.CheckedPaths);
    }

    private static HostExecutableResolutionRequest Request(
        string executableName = "node",
        string toolId = HostToolIds.Node,
        string overrideName = OverrideName,
        ImmutableArray<HostExecutableEnvironmentFallback> environmentFallbacks = default,
        ImmutableArray<HostExecutableRootFallback> fallbacks = default)
        => new(
            toolId,
            executableName,
            overrideName,
            environmentFallbacks.IsDefault ? [] : environmentFallbacks,
            fallbacks.IsDefault ? [] : fallbacks);

    private static HostExecutablePathConvention Convention()
        => new(Path.PathSeparator, string.Empty, StringComparison.Ordinal);

    private static HostExecutablePathResolver CreateResolver(
        RecordingEnvironment environment,
        RecordingPresenceChecker presence,
        HostExecutablePathConvention? convention = null,
        IHostPathCanonicalizer? canonicalizer = null)
        => new HostExecutablePathResolver(
            environment,
            presence,
            canonicalizer ?? new SystemHostPathCanonicalizer(),
            convention ?? Convention());

    private sealed class RecordingEnvironment(params (string Name, string? Value)[] variables)
        : IHostEnvironmentVariableReader
    {
        private readonly ImmutableDictionary<string, string?> _variables = variables
            .ToImmutableDictionary(static variable => variable.Name, static variable => variable.Value,
                StringComparer.Ordinal);

        public List<string> ReadNames { get; } = [];

        public string? Read(string name)
        {
            ReadNames.Add(name);
            return _variables.GetValueOrDefault(name);
        }
    }

    private sealed class RecordingPresenceChecker(params string[] existingPaths) : IFilePresenceChecker
    {
        private readonly ImmutableHashSet<string> _existingPaths = existingPaths
            .ToImmutableHashSet(StringComparer.Ordinal);

        public List<string> CheckedPaths { get; } = [];

        public bool Exists(string path)
        {
            CheckedPaths.Add(path);
            return _existingPaths.Contains(path);
        }
    }

    private sealed class ThrowingCanonicalizer(ArgumentException exception) : IHostPathCanonicalizer
    {
        public string Canonicalize(string path) => throw exception;
    }
}
