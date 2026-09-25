using System.Collections.Immutable;
using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Correctness;

[Collection(CorrectnessTestGroup.Name)]
public sealed class StjSourceGenerationAcceptanceTests
{
    [Fact]
    public void GeneratedNullableMetadataResolvesAliasedGenericCallsExactly()
    {
        using var services = CorrectnessTestAssets.CreateServices();
        var environment = services.GetRequiredService<CompilerCorrectnessEnvironment>();
        var processes = services.GetRequiredService<IQualifiedProcessRunner>();
        using var built = SourceGenerationFixtureBuild.Create(
            environment,
            processes,
            CilProfile.Debug);
        var fixture = built.Compilation.Fixture;
        var references = fixture.NetWasmReferencePaths.Insert(
            0,
            Path.Combine(
                environment.RepositoryRoot,
                "src/NetWasm.CoreLib/bin/Release/net10.0/NetWasm.CoreLib.dll"));
        using var metadata = MetadataCompilationTestFactory.Load(
            built.Compilation.NetWasm.AssemblyPath,
            references,
            fixture.ReferenceAssemblyAliases);
        var methods = MetadataCompilationTestActors.MethodFinder(metadata.Snapshot);
        var bodies = MetadataCompilationTestActors.MethodBodies(metadata.Snapshot);
        var factory = methods.FindMethod(
            metadata.Snapshot.EntryAssemblyIdentity,
            "NetWasm.Tests.StjSourceGeneration.Fixture.FixtureJsonContext",
            "Create_NullableInt32");
        var calls = bodies.ReadMethodBody(factory).Instructions
            .Where(instruction => instruction.Operation is CilOperation.Call)
            .Select(instruction => instruction.Operand)
            .OfType<CilOperand.MethodInstance>()
            .Select(operand => operand.Value)
            .ToArray();

        var nullableConverter = Assert.Single(calls, method =>
            method.Definition.Name == "GetNullableConverter");
        Assert.Equal("primitive:i4", Assert.Single(
            nullableConverter.MethodArguments).CanonicalName);
        Assert.Contains(
            "JsonSerializerOptions",
            Assert.Single(nullableConverter.Signature.ParameterSignatureTypes)
                .CanonicalName,
            StringComparison.Ordinal);

        var valueInfo = Assert.Single(calls, method =>
            method.Definition.Name == "CreateValueInfo");
        var valueType = Assert.Single(valueInfo.MethodArguments);
        Assert.Contains("System.Nullable`1", valueType.CanonicalName, StringComparison.Ordinal);
        Assert.Contains("primitive:i4", valueType.CanonicalName, StringComparison.Ordinal);
    }

    [Fact]
    public void GeneratedConverterListCountAndNullableWriteHaveConcreteDispatchTargets()
    {
        using var services = CorrectnessTestAssets.CreateServices();
        var environment = services.GetRequiredService<CompilerCorrectnessEnvironment>();
        var processes = services.GetRequiredService<IQualifiedProcessRunner>();
        using var built = SourceGenerationFixtureBuild.Create(
            environment,
            processes,
            CilProfile.Debug);
        var fixture = built.Compilation.Fixture;
        var references = fixture.NetWasmReferencePaths.Insert(
            0,
            Path.Combine(
                environment.RepositoryRoot,
                "src/NetWasm.CoreLib/bin/Release/net10.0/NetWasm.CoreLib.dll"));

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            built.Compilation.NetWasm.AssemblyPath,
            references,
            "NetWasm.Tests.StjSourceGeneration.Fixture.EntryPoint",
            "Run",
            [],
            ReferenceAssemblyAliases: fixture.ReferenceAssemblyAliases));
        var converterListCountSites = result.Program.DispatchCallSites.Values
            .Where(candidate =>
                candidate.Declaration.Definition.Name == "get_Count" &&
                candidate.Declaration.DeclaringType.CanonicalName.Contains(
                    "ICollection`1",
                    StringComparison.Ordinal) &&
                candidate.Declaration.DeclaringType.CanonicalName.Contains(
                    "JsonConverter",
                    StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(converterListCountSites);
        Assert.All(converterListCountSites, site =>
            Assert.Contains(site.Targets, target =>
                target.ReceiverType.CanonicalName.Contains(
                    "ConverterList",
                    StringComparison.Ordinal)));

        var nullableWriteSites = result.Program.DispatchCallSites.Values
            .Where(candidate =>
                candidate.Declaration.Definition.Name == "Write" &&
                candidate.Caller.Contains("JsonConverter`1", StringComparison.Ordinal) &&
                candidate.Caller.Contains("System.Nullable`1", StringComparison.Ordinal) &&
                candidate.Caller.Contains("primitive:i4", StringComparison.Ordinal))
            .ToArray();
        Assert.NotEmpty(nullableWriteSites);
        Assert.Contains(
            nullableWriteSites.SelectMany(site => site.Targets),
            target => target.ReceiverType.CanonicalName.Contains(
                "NullableConverter`1",
                StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Debug", WasmTarget.Wasm32)]
    [InlineData("Debug", WasmTarget.Wasm64)]
    [InlineData("Release", WasmTarget.Wasm32)]
    [InlineData("Release", WasmTarget.Wasm64)]
    public void OrdinarySourceGeneratedMetadataExecutesForEveryProfileAndTarget(
        string profileName,
        WasmTarget target)
    {
        var profile = Enum.Parse<CilProfile>(profileName);
        using var services = CorrectnessTestAssets.CreateServices();
        var environment = services.GetRequiredService<CompilerCorrectnessEnvironment>();
        var processes = services.GetRequiredService<IQualifiedProcessRunner>();
        var desktop = services.GetRequiredService<IDesktopOracleRunner>();
        var netWasm = services.GetRequiredService<INetWasmOracleRunner>();

        using var built = SourceGenerationFixtureBuild.Create(
            environment,
            processes,
            profile);
        var expected = desktop.Run(built.Compilation);
        var execution = netWasm.CompileAndRun(built.Compilation, target);

        Assert.True(execution.Executed);
        Assert.Equal(target, execution.Target);
        Assert.Equal(expected.Keys.Order(), execution.Observations.Keys.Order());
        foreach (var input in expected.Keys)
        {
            var observation = execution.Observations[input];
            Assert.True(
                observation.Kind == OracleObservationKind.Value,
                $"{profile}/{target}/input={input}: " +
                $"kind={observation.Kind}, detail={observation.Detail}, " +
                $"exception={observation.ExceptionType}, trace={observation.Trace}");
            Assert.Equal(expected[input].Value, observation.Value);
            Assert.Null(observation.ExceptionType);
        }
    }

    [Fact]
    public void GeneratedMixedShapeDeserializesWithAdditionalGeneratedContexts()
    {
        using var services = CorrectnessTestAssets.CreateServices();
        var environment = services.GetRequiredService<CompilerCorrectnessEnvironment>();
        var processes = services.GetRequiredService<IQualifiedProcessRunner>();
        var desktop = services.GetRequiredService<IDesktopOracleRunner>();
        var netWasm = services.GetRequiredService<INetWasmOracleRunner>();

        using var built = SourceGenerationFixtureBuild.CreateGeneratedShapeClosure(
            environment,
            processes,
            CilProfile.Debug);
        var expected = desktop.Run(built.Compilation);
        var execution = netWasm.CompileAndRun(built.Compilation, WasmTarget.Wasm32);

        Assert.True(execution.Executed);
        Assert.Equal(expected.Keys.Order(), execution.Observations.Keys.Order());
        foreach (var input in expected.Keys)
        {
            var observation = execution.Observations[input];
            Assert.Equal(OracleObservationKind.Value, observation.Kind);
            Assert.Equal(expected[input].Value, observation.Value);
            Assert.Null(observation.ExceptionType);
        }
    }

}

internal sealed class SourceGenerationFixtureBuild : IDisposable
{
    private const string FixtureProject =
        "tests/end-to-end/stj-source-generation/" +
        "NetWasm.Stj.SourceGeneration.Fixture.csproj";

    private const string FixtureSource =
        "tests/end-to-end/stj-source-generation/SourceGenerationFixture.cs";

    private const string GeneratedShapeClosureFixtureProject =
        "tests/end-to-end/stj-generated-shape-closure/" +
        "NetWasm.Stj.GeneratedShapeClosure.Fixture.csproj";

    private const string GeneratedShapeClosureFixtureSource =
        "tests/end-to-end/stj-generated-shape-closure/" +
        "GeneratedShapeClosureFixture.cs";

    private const string AsyncFixtureProject =
        "tests/end-to-end/stj-source-generation-async/" +
        "NetWasm.Stj.SourceGeneration.Async.Fixture.csproj";

    private const string AsyncFixtureSource =
        "tests/end-to-end/stj-source-generation-async/SourceGenerationAsyncFixture.cs";

    private const string RegexFixtureProject =
        "tests/end-to-end/regex-source-generation/" +
        "NetWasm.Regex.SourceGeneration.Fixture.csproj";

    private const string RegexFixtureSource =
        "tests/end-to-end/regex-source-generation/SourceGenerationFixture.cs";

    private const string RegexFallbackFixtureProject =
        "tests/end-to-end/regex-source-generation-fallback/" +
        "NetWasm.Regex.SourceGeneration.Fallback.Fixture.csproj";

    private const string RegexFallbackFixtureSource =
        "tests/end-to-end/regex-source-generation-fallback/" +
        "SourceGenerationFallbackFixture.cs";

    private SourceGenerationFixtureBuild(
        string directory,
        CorpusCompilation compilation,
        string generatedText)
    {
        Directory = directory;
        Compilation = compilation;
        GeneratedText = generatedText;
    }

    private string Directory { get; }

    public CorpusCompilation Compilation { get; }

    public string GeneratedText { get; }

    public static SourceGenerationFixtureBuild Create(
        CompilerCorrectnessEnvironment environment,
        IQualifiedProcessRunner processes,
        CilProfile profile) => Create(
            environment,
            processes,
            profile,
            "netwasm-stj-source-generation",
            FixtureProject,
            FixtureSource,
            "NetWasm.Stj.SourceGeneration.Fixture.dll",
            "StjSourceGeneration",
            "NetWasm.Tests.StjSourceGeneration.Fixture",
            [
                "partial class FixtureJsonContext",
                "Envelope",
                "int?",
                "FixtureKind",
                "List<int>",
                "Dictionary<string, int?>",
                "Nested.Pair",
                "MarkerConverter",
            ],
            requiresReactor: false);

    public static SourceGenerationFixtureBuild CreateGeneratedShapeClosure(
        CompilerCorrectnessEnvironment environment,
        IQualifiedProcessRunner processes,
        CilProfile profile) => Create(
            environment,
            processes,
            profile,
            "netwasm-stj-generated-shape-closure",
            GeneratedShapeClosureFixtureProject,
            GeneratedShapeClosureFixtureSource,
            "NetWasm.Stj.GeneratedShapeClosure.Fixture.dll",
            "StjGeneratedShapeClosure",
            "NetWasm.Tests.StjGeneratedShapeClosure.Fixture",
            [
                "partial class GeneratedMixedContext",
                "partial class JsonTestContext",
                "partial class NamedJsonTestContext",
                "GeneratedMixedModel",
                "ExtensionEnvelope",
            ],
            requiresReactor: true,
            inputs: [0],
            wasmEntryMethod: "Run");

    public static SourceGenerationFixtureBuild CreateRegex(
        CompilerCorrectnessEnvironment environment,
        IQualifiedProcessRunner processes,
        CilProfile profile) => Create(
            environment,
            processes,
            profile,
            "netwasm-regex-source-generation",
            RegexFixtureProject,
            RegexFixtureSource,
            "NetWasm.Regex.SourceGeneration.Fixture.dll",
            "RegexSourceGeneration",
            "NetWasm.Tests.RegexSourceGeneration.Fixture",
            [
                "GeneratedPatterns",
                "GenericContainer",
                "RunnerFactory",
                "TryFindNextPossibleStartingPosition",
                "TryMatchAtCurrentPosition",
            ],
            requiresReactor: true,
            inputs: [0, 1, 2, 3, 4, 5, 6, 7],
            wasmEntryMethod: "Run",
            usesStjDependencies: false,
            usesRegexDependencies: true);

    public static SourceGenerationFixtureBuild CreateRegexFallback(
        CompilerCorrectnessEnvironment environment,
        IQualifiedProcessRunner processes,
        CilProfile profile) => Create(
            environment,
            processes,
            profile,
            "netwasm-regex-source-generation-fallback",
            RegexFallbackFixtureProject,
            RegexFallbackFixtureSource,
            "NetWasm.Regex.SourceGeneration.Fallback.Fixture.dll",
            "RegexSourceGenerationFallback",
            "NetWasm.Tests.RegexSourceGeneration.FallbackFixture",
            [
                "GeneratedFallbackPatterns",
                "A custom Regex-derived type could not be generated",
            ],
            requiresReactor: true,
            inputs: [0, 1, 2],
            wasmEntryMethod: "Run",
            usesStjDependencies: false,
            usesRegexDependencies: true);

    public static SourceGenerationFixtureBuild CreateAsync(
        CompilerCorrectnessEnvironment environment,
        IQualifiedProcessRunner processes,
        CilProfile profile) => Create(
            environment,
            processes,
            profile,
            "netwasm-stj-source-generation-async",
            AsyncFixtureProject,
            AsyncFixtureSource,
            "NetWasm.Stj.SourceGeneration.Async.Fixture.dll",
            "StjSourceGenerationAsync",
            "NetWasm.Tests.StjSourceGeneration.AsyncFixture",
            [
                "partial class AsyncFixtureJsonContext",
                "AsyncEnvelope",
                "IAsyncEnumerable<int>",
                "List<int>",
                "Dictionary<string, int?>",
            ],
            requiresReactor: true);

    public static SourceGenerationFixtureBuild CreateAsyncPipeRoundTrip(
        CompilerCorrectnessEnvironment environment,
        IQualifiedProcessRunner processes,
        CilProfile profile) => Create(
            environment,
            processes,
            profile,
            "netwasm-stj-source-generation-async-pipe",
            AsyncFixtureProject,
            AsyncFixtureSource,
            "NetWasm.Stj.SourceGeneration.Async.Fixture.dll",
            "StjSourceGenerationAsyncPipe",
            "NetWasm.Tests.StjSourceGeneration.AsyncFixture",
            [
                "partial class AsyncFixtureJsonContext",
                "AsyncEnvelope",
                "int?",
            ],
            requiresReactor: true,
            inputs: [10000, 10001],
            wasmEntryMethod: "StartPipeRoundTrip",
            reactorObserveMethod: "ObservePipeRoundTrip");

    public static SourceGenerationFixtureBuild CreateAsyncStreamRoundTrip(
        CompilerCorrectnessEnvironment environment,
        IQualifiedProcessRunner processes,
        CilProfile profile) => CreateAsyncSlice(
            environment,
            processes,
            profile,
            "stream",
            "StartStreamRoundTrip");

    public static SourceGenerationFixtureBuild CreateAsyncSequenceRoundTrip(
        CompilerCorrectnessEnvironment environment,
        IQualifiedProcessRunner processes,
        CilProfile profile) => CreateAsyncSlice(
            environment,
            processes,
            profile,
            "sequence",
            "StartSequenceRoundTrip");

    public static SourceGenerationFixtureBuild CreateAsyncSequenceFailures(
        CompilerCorrectnessEnvironment environment,
        IQualifiedProcessRunner processes,
        CilProfile profile) => CreateAsyncSlice(
            environment,
            processes,
            profile,
            "sequence-failures",
            "StartSequenceFailures");

    private static SourceGenerationFixtureBuild CreateAsyncSlice(
        CompilerCorrectnessEnvironment environment,
        IQualifiedProcessRunner processes,
        CilProfile profile,
        string slice,
        string entryMethod) => Create(
            environment,
            processes,
            profile,
            "netwasm-stj-source-generation-async-" + slice,
            AsyncFixtureProject,
            AsyncFixtureSource,
            "NetWasm.Stj.SourceGeneration.Async.Fixture.dll",
            "StjSourceGenerationAsync" + slice,
            "NetWasm.Tests.StjSourceGeneration.AsyncFixture",
            [
                "partial class AsyncFixtureJsonContext",
                "AsyncEnvelope",
                "IAsyncEnumerable<int>",
            ],
            requiresReactor: true,
            inputs: [2],
            wasmEntryMethod: entryMethod,
            reactorObserveMethod: "ObserveSlice");

    private static SourceGenerationFixtureBuild Create(
        CompilerCorrectnessEnvironment environment,
        IQualifiedProcessRunner processes,
        CilProfile profile,
        string temporaryDirectoryName,
        string fixtureProject,
        string fixtureSource,
        string assemblyFileName,
        string fixtureName,
        string fixtureNamespace,
        ImmutableArray<string> requiredGeneratedMetadata,
        bool requiresReactor,
        ImmutableArray<int> inputs = default,
        string? wasmEntryMethod = null,
        string? reactorObserveMethod = null,
        bool usesStjDependencies = true,
        bool usesRegexDependencies = false)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(processes);

        var directory = Path.Combine(
            Path.GetTempPath(),
            temporaryDirectoryName,
            $"{profile}-{Guid.NewGuid():N}");
        var outputBase = Path.Combine(directory, "bin") + Path.DirectorySeparatorChar;
        var intermediateBase = Path.Combine(directory, "obj") + Path.DirectorySeparatorChar;
        var generated = Path.Combine(intermediateBase, "generated");
        System.IO.Directory.CreateDirectory(directory);

        var projectPath = Path.Combine(environment.RepositoryRoot, fixtureProject);
        var process = processes.Run(new(
            environment.DotNetPath,
            [
                "build",
                projectPath,
                "--configuration",
                profile.ToString(),
                "--nologo",
                "--verbosity:minimal",
                "/p:BaseOutputPath=" + outputBase,
                "/p:BaseIntermediateOutputPath=" + intermediateBase,
            ],
            environment.ProcessTimeout));
        if (!process.Succeeded)
        {
            throw new InvalidOperationException(
                $"STJ source-generation host build failed for {profile}: " +
                $"completion={process.Completion}, exit={process.ExitCode}, " +
                process.StandardOutput + process.StandardError,
                process.LaunchException);
        }

        var generatedFiles = System.IO.Directory.EnumerateFiles(
                generated,
                "*.g.cs",
                SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .ToImmutableArray();
        var generatedText = string.Join(
            Environment.NewLine,
            generatedFiles.Select(File.ReadAllText));
        if (generatedFiles.Length == 0 || requiredGeneratedMetadata.Any(
                token => !generatedText.Contains(token, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                "ordinary SDK source generation did not emit the required metadata");
        }

        var outputDirectory = Path.Combine(outputBase, profile.ToString(), "net10.0");
        var assemblyPath = Path.Combine(
            outputDirectory,
            assemblyFileName);
        var pdbPath = Path.ChangeExtension(assemblyPath, ".pdb");
        RequireFile(assemblyPath, "host assembly");
        RequireFile(pdbPath, "host symbols");

        var source = File.ReadAllText(Path.Combine(environment.RepositoryRoot, fixtureSource));
        var compilationDirectory = Path.Combine(directory, "compilation");
        System.IO.Directory.CreateDirectory(compilationDirectory);
        File.WriteAllText(
            Path.Combine(compilationDirectory, fixtureName + ".cs"),
            source);

        var netWasmReferences = CreateNetWasmReferences(
            environment.RepositoryRoot,
            usesStjDependencies,
            usesRegexDependencies);
        var fixture = new CorpusFixture(
            fixtureName,
            fixtureNamespace,
            source,
            inputs.IsDefault ? [-2, 0, 1, 3] : inputs)
        {
            ExecuteWasm64 = true,
            EmitStackTrace = true,
            ExposesLegacyTrace = false,
            NetWasmReferencePaths = netWasmReferences,
            OracleMode = OracleMode.SameSource,
            SameSourceReason =
                "the desktop host is built by the installed SDK source generator, " +
                "while NetWasm uses its compatibility framework assemblies",
            ReferenceAssemblyAliases = CreateReferenceAssemblyAliases(
                usesStjDependencies,
                usesRegexDependencies),
            RequiresReactor = requiresReactor,
            DesktopEntryMethod = wasmEntryMethod is not null &&
                wasmEntryMethod.StartsWith("Start", StringComparison.Ordinal)
                ? "Run" + wasmEntryMethod["Start".Length..]
                : "Run",
            WasmEntryMethod = wasmEntryMethod ?? (requiresReactor ? "Start" : "Run"),
            ReactorObserveMethod = reactorObserveMethod ??
                (requiresReactor && wasmEntryMethod is null ? "Observe" : null),
        };
        var artifact = new CorpusArtifact(
            assemblyPath,
            pdbPath,
            Hash(assemblyPath),
            Hash(pdbPath),
            environment.SdkVersion,
            [
                "dotnet",
                "build",
                fixtureProject,
                "--configuration",
                profile.ToString(),
            ]);
        return new(
            directory,
            new CorpusCompilation(fixture, profile, artifact, artifact, compilationDirectory),
            generatedText);

    }

    public void Dispose()
    {
        if (System.IO.Directory.Exists(Directory))
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
    }

    private static ImmutableArray<string> CreateNetWasmReferences(
        string root,
        bool usesStjDependencies,
        bool usesRegexDependencies)
    {
        var references = ImmutableArray.CreateBuilder<string>();
        if (usesStjDependencies)
        {
            references.AddRange(new[]
            {
                ResolveCompatibilityReference(
                    root,
                    "NetWasm.System.Text.Json",
                    "System.Text.Json.dll"),
                ResolveCompatibilityReference(
                    root,
                    "NetWasm.System.Memory",
                    "System.Memory.dll"),
                ResolveCompatibilityReference(
                    root,
                    "NetWasm.System.Text.Encodings.Web",
                    "System.Text.Encodings.Web.dll"),
                ResolveCompatibilityReference(
                    root,
                    "NetWasm.System.IO.Pipelines",
                    "System.IO.Pipelines.dll"),
            });
        }

        if (usesRegexDependencies)
        {
            references.Add(ResolveCompatibilityReference(
                root,
                "NetWasm.System.Text.RegularExpressions",
                "System.Text.RegularExpressions.dll"));
        }

        foreach (var path in references)
        {
            RequireFile(path, "NetWasm source-generation reference");
        }

        return references.ToImmutable();
    }

    private static string ResolveCompatibilityReference(
        string repositoryRoot,
        string project,
        string assembly)
    {
        var preparedRoot = Environment.GetEnvironmentVariable(
            "NETWASM_COMPATIBILITY_REFERENCE_ROOT");
        if (string.IsNullOrWhiteSpace(preparedRoot))
        {
            return Path.Combine(
                repositoryRoot,
                "src",
                project,
                "bin/Release/netwasm0.1",
                assembly);
        }

        if (!Path.IsPathFullyQualified(preparedRoot))
        {
            throw new InvalidOperationException(
                "NETWASM_COMPATIBILITY_REFERENCE_ROOT must be an absolute path.");
        }
        return Path.Combine(preparedRoot, assembly);
    }

    private static ImmutableDictionary<string, string> CreateReferenceAssemblyAliases(
        bool usesStjDependencies,
        bool usesRegexDependencies)
    {
        var aliases = ImmutableDictionary<string, string>.Empty
            .Add("System.Private.CoreLib", "NetWasm.CoreLib")
            .Add("System.Runtime", "NetWasm.CoreLib")
            .Add("System.Collections", "NetWasm.CoreLib")
            .Add("System.Reflection", "NetWasm.CoreLib")
            .Add("System.Runtime.InteropServices", "NetWasm.CoreLib")
            .Add("System.Text", "NetWasm.CoreLib")
            .Add("System.Runtime.CompilerServices.Unsafe", "NetWasm.CoreLib");
        aliases = usesStjDependencies
            ? aliases
                .Add("System.Buffers", "System.Memory")
                .Add("System.Memory", "System.Memory")
                .Add("System.Text.Json", "System.Text.Json")
                .Add("System.Text.Encodings.Web", "System.Text.Encodings.Web")
                .Add("System.IO.Pipelines", "System.IO.Pipelines")
            : aliases
                .Add("System.Buffers", "NetWasm.CoreLib")
                .Add("System.Memory", "NetWasm.CoreLib");
        return usesRegexDependencies
            ? aliases.Add("System.Text.RegularExpressions", "System.Text.RegularExpressions")
            : aliases;
    }

    private static string Hash(string path) => Convert.ToHexString(
        SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private static void RequireFile(string path, string description)
    {
        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"required {description} is missing: {path}. " +
                "Build the corresponding NetWasm compatibility project first.");
        }
    }
}
