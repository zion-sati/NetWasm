namespace NetWasm.Sdk.Pack.Packing;

/// <summary>
/// Publishes the complete pack output set as one validate-then-commit
/// transaction. The manifest is deliberately the last destination moved.
/// </summary>
public sealed class PackOutputTransaction : IPackOutputTransaction
{
    private readonly IArchiveWriter archiveWriter;
    private readonly ISymbolPackageWriter symbolWriter;
    private readonly IPackManifestBuilder manifestBuilder;
    private readonly IPackManifestSerializer manifestSerializer;
    private readonly IPackArtifactWriter artifactWriter;
    private readonly IFileMover fileMover;

    public PackOutputTransaction(
        IArchiveWriter archiveWriter,
        ISymbolPackageWriter symbolWriter,
        IPackManifestBuilder manifestBuilder,
        IPackManifestSerializer manifestSerializer,
        IPackArtifactWriter artifactWriter,
        IFileMover fileMover)
    {
        this.archiveWriter = archiveWriter ?? throw new ArgumentNullException(nameof(archiveWriter));
        this.symbolWriter = symbolWriter ?? throw new ArgumentNullException(nameof(symbolWriter));
        this.manifestBuilder = manifestBuilder ?? throw new ArgumentNullException(nameof(manifestBuilder));
        this.manifestSerializer = manifestSerializer ?? throw new ArgumentNullException(nameof(manifestSerializer));
        this.artifactWriter = artifactWriter ?? throw new ArgumentNullException(nameof(artifactWriter));
        this.fileMover = fileMover ?? throw new ArgumentNullException(nameof(fileMover));
    }

    public CanonicalPackResult Commit(CanonicalPackage package, ArchivePlan plan)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(plan);

        var finalPackagePath = FullPath(package.OutputPath);
        var finalSymbolPath = package.Symbols.IncludeSymbols
            ? FullPath(package.SymbolOutputPath ?? Path.ChangeExtension(package.OutputPath, ".snupkg"))
            : null;
        var finalNuspecPath = FullPathOrNull(package.NuspecOutputPath);
        var finalManifestPath = FullPathOrNull(package.ManifestOutputPath);
        var destinations = new[] { finalPackagePath, finalSymbolPath, finalNuspecPath, finalManifestPath }
            .Where(static path => path is not null)
            .Cast<string>()
            .ToArray();
        if (destinations.Distinct(StringComparer.OrdinalIgnoreCase).Count() != destinations.Length)
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK014, "Package outputs cannot share an output path.");
        }

        var outputDirectory = Path.GetDirectoryName(finalPackagePath)!;
        var stagingDirectory = Path.Combine(outputDirectory, $".netwasm-pack-{Guid.NewGuid():N}");
        var staged = new List<StagedOutput>();
        try
        {
            Directory.CreateDirectory(stagingDirectory);
            var stagedPackagePath = Path.Combine(stagingDirectory, Path.GetFileName(finalPackagePath));
            var stagedSymbolPath = finalSymbolPath is null ? null : Path.Combine(stagingDirectory, Path.GetFileName(finalSymbolPath));
            var stagedNuspecPath = finalNuspecPath is null ? null : Path.Combine(stagingDirectory, Path.GetFileName(finalNuspecPath));
            var stagedManifestPath = finalManifestPath is null ? null : Path.Combine(stagingDirectory, Path.GetFileName(finalManifestPath));
            var stagedPackage = package with
            {
                OutputPath = stagedPackagePath,
                SymbolOutputPath = stagedSymbolPath,
                NuspecOutputPath = stagedNuspecPath,
                ManifestOutputPath = stagedManifestPath
            };
            var stagedPlan = plan with { OutputPath = stagedPackagePath };
            var packageOutput = archiveWriter.Write(stagedPlan);
            var symbolOutput = symbolWriter.Write(stagedPackage);
            var manifest = manifestBuilder.Build(stagedPackage, stagedPlan, packageOutput, symbolOutput);
            var nuspec = stagedPlan.Entries.Single(entry => string.Equals(entry.Path, $"{stagedPackage.Identity.Id}.nuspec", StringComparison.Ordinal));
            artifactWriter.Write(new PackArtifacts(
                stagedNuspecPath,
                nuspec.Content.ToArray(),
                stagedManifestPath,
                manifestSerializer.Serialize(manifest)));

            AddStaged(staged, packageOutput.Path, finalPackagePath);
            if (symbolOutput is not null)
            {
                AddStaged(staged, symbolOutput.Path, finalSymbolPath!);
            }

            if (stagedNuspecPath is not null)
            {
                AddStaged(staged, stagedNuspecPath, finalNuspecPath!);
            }

            if (stagedManifestPath is not null)
            {
                AddStaged(staged, stagedManifestPath, finalManifestPath!);
            }

            Publish(staged);
            var publishedPackage = packageOutput with { Path = finalPackagePath };
            var publishedSymbols = symbolOutput is null ? null : symbolOutput with { Path = finalSymbolPath! };
            return new CanonicalPackResult(publishedPackage, publishedSymbols, manifest, finalNuspecPath, finalManifestPath);
        }
        catch (NetWasmPackException)
        {
            Rollback(staged);
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            Rollback(staged);
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK014, "The package output transaction could not be committed.");
        }
        finally
        {
            DeleteDirectory(stagingDirectory);
        }
    }

    private void Publish(IReadOnlyList<StagedOutput> outputs)
    {
        foreach (var output in outputs)
        {
            var directory = Path.GetDirectoryName(output.Destination)!;
            Directory.CreateDirectory(directory);
            if (File.Exists(output.Destination))
            {
                output.Backup = output.Temporary + ".previous";
                fileMover.Move(output.Destination, output.Backup, overwrite: false);
            }

            fileMover.Move(output.Temporary, output.Destination, overwrite: false);
            output.Published = true;
        }

        foreach (var output in outputs)
        {
            if (output.Backup is not null && File.Exists(output.Backup))
            {
                TryDelete(output.Backup);
            }
        }
    }

    private static void AddStaged(ICollection<StagedOutput> outputs, string temporary, string destination)
    {
        outputs.Add(new StagedOutput(temporary, destination));
    }

    private static string FullPath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK014, "The package output path is invalid.");
        }
    }

    private static string? FullPathOrNull(string? path) => string.IsNullOrWhiteSpace(path) ? null : FullPath(path);

    private void RestoreBackup(StagedOutput output)
    {
        if (output.Backup is not null && File.Exists(output.Backup))
        {
            fileMover.Move(output.Backup, output.Destination, overwrite: true);
        }
    }

    private void Rollback(IEnumerable<StagedOutput> outputs)
    {
        foreach (var output in outputs)
        {
            if (output.Published)
            {
                TryDelete(output.Destination);
            }
            RestoreBackup(output);
            TryDelete(output.Temporary);
        }
    }

    private static void TryDelete(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private sealed class StagedOutput(string temporary, string destination)
    {
        public string Temporary { get; } = temporary;
        public string Destination { get; } = destination;
        public string? Backup { get; set; }
        public bool Published { get; set; }
    }
}
