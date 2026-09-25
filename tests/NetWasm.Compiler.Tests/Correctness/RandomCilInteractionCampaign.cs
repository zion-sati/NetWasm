using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace NetWasm.Compiler.Tests.Correctness;

internal interface IRandomCilInteractionCampaign
{
    Task<ImmutableArray<RandomCilCampaignResult>> RunAsync(
        int? maximumShards = null,
        int startShard = 0,
        CancellationToken cancellationToken = default);
}

internal sealed class RandomCilInteractionCampaign(
    IRandomCilRunDirectoryFactory runDirectories,
    IRandomCilInteractionShardPlanner planner,
    IRandomCilInteractionFixtureFactory fixtures,
    IRandomCilInteractionGenerator generator,
    IGeneratedCilValidator validator,
    IGeneratedCilSerializer serializer,
    IMethodBodyPatcher patcher,
    IRandomCilMetadataTokenResolver metadataTokens,
    IRoslynCorpusCompiler compiler,
    ICorpusSourceArtifactWriter sources,
    IPatchedCorpusCompilationBuilder compilations,
    IDesktopOracleRunner desktop,
    ICompiledCorpusComparisonRunner compiled,
    IOracleModePolicyRegistry oracleModes,
    IRandomCilCampaignScheduler scheduler,
    IRandomCilCaseProgressReporter caseProgress) : IRandomCilInteractionCampaign
{
    internal const int MaximumCasesPerProgram = 100;
    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        WriteIndented = true,
        NewLine = "\n",
    };

    private sealed record PreparedTemplate(
        CorpusCompilation Compilation,
        RandomCilMetadataTokens Tokens);

    private readonly ConcurrentPreparationCache<string, PreparedTemplate> _templates =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<(CilProfile Profile, int Shard), int> _failedCaseIndexes = new();
    private readonly string _runId =
        $"{DateTime.UtcNow:yyyyMMddTHHmmss}-{Guid.NewGuid():N}";

    public async Task<ImmutableArray<RandomCilCampaignResult>> RunAsync(
        int? maximumShards = null,
        int startShard = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(startShard);
        if (maximumShards <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumShards));
        }

        var selectedShards = maximumShards is null
            ? planner.CreateShards()
            : [.. planner.CreateShards().Skip(startShard).Take(maximumShards.Value)];
        PrepareTemplate(CilProfile.Debug, exact: false);
        PrepareTemplate(CilProfile.Release, exact: false);

        var cases = selectedShards
            .Select(CreateCampaignCase)
            .ToImmutableArray();
        return await scheduler.RunAsync(cases, cancellationToken);
    }

    private RandomCilCampaignCase CreateCampaignCase(RandomCilInteractionShard shard)
    {
        var id = $"shard-{shard.Index:D4}";
        return new(
            id,
            token => RunShardPair(shard, captureDiagnostics: false, token),
            token => DiagnoseShardPair(shard, token));
    }

    private void RunShardPair(
        RandomCilInteractionShard shard,
        bool captureDiagnostics,
        CancellationToken cancellationToken)
    {
        var expected = RunShard(
            CilProfile.Debug,
            shard,
            captureDiagnostics,
            cancellationToken);
        RunShard(
            CilProfile.Release,
            shard,
            captureDiagnostics,
            cancellationToken,
            expectedBySlice: expected);
    }

    private void DiagnoseShardPair(
        RandomCilInteractionShard shard,
        CancellationToken cancellationToken) =>
        RunShardPair(shard, captureDiagnostics: true, cancellationToken);

    private PreparedTemplate PrepareTemplate(CilProfile profile, bool exact) =>
        PrepareTemplate(profile, exact ? 1 : MaximumCasesPerProgram);

    private PreparedTemplate PrepareTemplate(CilProfile profile, int slotCount) =>
        _templates.GetOrAdd($"{profile}:{slotCount}", _ =>
        {
            var fixture = fixtures.CreateTemplate(profile, slotCount);
            var directory = Path.Combine(
                Path.GetTempPath(),
                "netwasm-qualification",
                "random-cil-interactions",
                _runId,
                profile.ToString().ToLowerInvariant(),
                    $"template-{slotCount:D4}");
            try
            {
                var compilation = compiler.Compile(fixture, profile, directory);
                var tokens = metadataTokens.Resolve(compilation.Desktop.AssemblyPath);
                return new(compilation, tokens);
            }
            catch (Exception failure)
            {
                failure.Data["CorpusTemplateDirectory"] = directory;
                throw;
            }
        });

    private ImmutableArray<ImmutableDictionary<int, OracleObservation>> RunShard(
        CilProfile profile,
        RandomCilInteractionShard shard,
        bool captureDiagnostics,
        CancellationToken cancellationToken,
        bool exactTemplate = false,
        ImmutableArray<ImmutableDictionary<int, OracleObservation>>? expectedBySlice = null)
    {
        var progressId = $"rit-shard-{shard.Index:D4}-{profile}";
        caseProgress.Report(progressId, RandomCilCaseStage.PrepareTemplate);

        var slotCount = exactTemplate ? 1 : MaximumCasesPerProgram;
        var template = PrepareTemplate(profile, slotCount);
        var slices = RandomCilInteractionProgramSlicer.Create(shard, slotCount);
        var captured = expectedBySlice is null
            ? ImmutableArray.CreateBuilder<ImmutableDictionary<int, OracleObservation>>(slices.Length)
            : null;

        if (expectedBySlice is { } sharedSlices && sharedSlices.Length != slices.Length)
        {
            throw new InvalidOperationException(
                $"Expected {slices.Length} shared oracle slices but received {sharedSlices.Length}.");
        }

        foreach (var programSlice in slices)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fixture = fixtures.CreateShard(
                profile,
                programSlice.Shard,
                slotCount) with
            {
                CaptureCompilerDiagnostics = captureDiagnostics,
            };
            var runDirectory = runDirectories.Create(
                profile,
                shard.Index,
                programSlice.Index,
                captureDiagnostics);
            try
            {
                caseProgress.ReportProgramSlices(
                    progressId,
                    programSlice.Index,
                    slices.Length);
                caseProgress.Report(progressId, RandomCilCaseStage.PatchShard);
                var compilation = PatchShard(
                    template,
                    fixture,
                    programSlice.Shard,
                    profile,
                    runDirectory.Directory);

                try
                {
                    var expected = expectedBySlice is { } shared
                        ? shared[programSlice.Index]
                        : desktop.Run(compilation, cancellationToken);
                    captured?.Add(expected);
                    compiled.RunAgainstOracle(compilation, expected, cancellationToken);
                }
                catch (CorpusOracleMismatchException mismatch)
                {
                    _failedCaseIndexes[(profile, shard.Index)] = checked(
                        programSlice.Offset +
                        RandomCilDiagnosticReplaySelector.SelectCase(
                            mismatch.Input,
                            0,
                            programSlice.Shard.Cases.Length));
                    throw;
                }

                caseProgress.ReportProgramSlices(
                    progressId,
                    programSlice.Index + 1,
                    slices.Length);
            }
            catch (Exception failure)
            {
                failure.Data["CorpusRunDirectory"] = runDirectory.Root;
                failure.Data["CorpusTemplateDirectory"] = template.Compilation.Directory;
                throw;
            }
        }

        caseProgress.Report(progressId, RandomCilCaseStage.Complete);
        return captured?.ToImmutable() ?? expectedBySlice!.Value;
    }

    private void DiagnoseShard(
        CilProfile profile,
        RandomCilInteractionShard shard,
        CancellationToken cancellationToken)
    {
        var hasKnownFailure = _failedCaseIndexes.TryGetValue((profile, shard.Index), out var failedIndex);
        var firstIndex = hasKnownFailure ? failedIndex : 0;
        var endIndex = hasKnownFailure ? failedIndex + 1 : shard.Cases.Length;

        for (var index = firstIndex; index < endIndex; index++)
        {
            var exact = new RandomCilInteractionShard(
                shard.Index,
                [shard.Cases[index]]);
            try
            {
                RunShard(
                    profile,
                    exact,
                    captureDiagnostics: false,
                    cancellationToken,
                    exactTemplate: true);
            }
            catch (Exception initialFailure)
            {
                try
                {
                    RunShard(
                        profile,
                        exact,
                        captureDiagnostics: true,
                        cancellationToken,
                        exactTemplate: true);
                }
                catch (Exception diagnosticFailure)
                {
                    throw new InvalidOperationException(
                        $"RIT case '{shard.Cases[index].Id}' failed in {profile}; " +
                        $"diagnostic replay: {diagnosticFailure.GetType().Name}: " +
                        diagnosticFailure.Message,
                        initialFailure);
                }

                throw new InvalidOperationException(
                    $"RIT case '{shard.Cases[index].Id}' failed in {profile} but " +
                    "passed its diagnostic replay.",
                    initialFailure);
            }
        }

        throw new InvalidOperationException(
            $"RIT shard {shard.Index} failed in {profile}, but every isolated case passed.");
    }

    private CorpusCompilation PatchShard(
        PreparedTemplate template,
        CorpusFixture fixture,
        RandomCilInteractionShard shard,
        CilProfile profile,
        string directory)
    {
        Directory.CreateDirectory(directory);
        var source = sources.Write(new(fixture.Name + ".cs", fixture.Source), directory);
        var assemblyPath = CopyArtifact(template.Compilation.Desktop.AssemblyPath, directory);
        var pdbPath = CopyArtifact(template.Compilation.Desktop.PdbPath, directory);
        PatchedMethodBody? finalPatch = null;

        for (var slot = 0; slot < shard.Cases.Length; slot++)
        {
            var program = generator.Generate(shard.Cases[slot], template.Tokens);
            validator.Validate(program);
            var method = serializer.SerializeMethod(program);
            finalPatch = patcher.Patch(
                assemblyPath,
                fixture.EntryType,
                $"Case{slot:D3}",
                method,
                maxStack: validator.MeasureMaximumStackDepth(program));
        }

        if (finalPatch is null)
        {
            throw new InvalidOperationException("An interaction shard cannot be empty.");
        }

        var artifact = template.Compilation.Desktop with
        {
            AssemblyPath = assemblyPath,
            PdbPath = pdbPath,
            AssemblySha256 = finalPatch.AssemblySha256,
            CompilerOptions = template.Compilation.Desktop.CompilerOptions.Add(
                $"random-cil-interaction-shard:{shard.Index}"),
        };
        var compilation = compilations.Build(template.Compilation, fixture, artifact, source, directory);
        oracleModes.Get(OracleMode.SameIl).ValidateCompilation(compilation);
        WriteManifest(directory, shard, profile, artifact.AssemblySha256);
        return compilation;
    }

    private static string CopyArtifact(string source, string directory)
    {
        var destination = Path.Combine(directory, Path.GetFileName(source));
        File.Copy(source, destination, overwrite: false);
        return destination;
    }

    private static void WriteManifest(
        string directory,
        RandomCilInteractionShard shard,
        CilProfile profile,
        string assemblySha256)
    {
        var manifest = new
        {
            profile = profile.ToString(),
            shard = shard.Index,
            assemblySha256,
            cases = shard.Cases.Select((testCase, slot) => new
            {
                slot,
                testCase.Id,
                testCase.Seed,
                levels = testCase.Levels,
            }),
        };
        var path = Path.Combine(directory, "interaction-manifest.json");
        File.WriteAllText(path, JsonSerializer.Serialize(manifest, ManifestJsonOptions));
        _ = SHA256.HashData(File.ReadAllBytes(path));
    }
}
