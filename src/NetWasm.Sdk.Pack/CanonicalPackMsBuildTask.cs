using System.Collections.Immutable;
using System.Globalization;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using MsBuildTask = Microsoft.Build.Utilities.Task;

namespace NetWasm.Sdk.Pack;

public sealed class CanonicalPackMsBuildTask : MsBuildTask
{
    private readonly IMsBuildPackInputAdapter inputAdapter;
    private readonly IProjectReferenceAdapter projectReferenceAdapter;
    private readonly IPackageBuilder packageBuilder;
    private readonly IPackDiagnosticWriter diagnosticWriter;

    public CanonicalPackMsBuildTask()
        : this(PackComposition.CreateTaskDependencies())
    {
    }

    private CanonicalPackMsBuildTask(PackTaskDependencies dependencies)
        : this(dependencies.InputAdapter, dependencies.ProjectReferenceAdapter, dependencies.PackageBuilder, dependencies.DiagnosticWriter)
    {
    }

    public CanonicalPackMsBuildTask(
        IMsBuildPackInputAdapter inputAdapter,
        IProjectReferenceAdapter projectReferenceAdapter,
        IPackageBuilder packageBuilder,
        IPackDiagnosticWriter diagnosticWriter)
    {
        this.inputAdapter = inputAdapter ?? throw new ArgumentNullException(nameof(inputAdapter));
        this.projectReferenceAdapter = projectReferenceAdapter ?? throw new ArgumentNullException(nameof(projectReferenceAdapter));
        this.packageBuilder = packageBuilder ?? throw new ArgumentNullException(nameof(packageBuilder));
        this.diagnosticWriter = diagnosticWriter ?? throw new ArgumentNullException(nameof(diagnosticWriter));
    }

    public string PackageId { get; set; } = string.Empty;
    public string PackageVersion { get; set; } = string.Empty;
    public string Authors { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string PackageOutputPath { get; set; } = string.Empty;
    public string? NuspecOutputDirectory { get; set; }
    public string CanonicalTargetFramework { get; set; } = string.Empty;
    public ITaskItem[] TargetProfiles { get; set; } = [];
    public ITaskItem[] PackageFiles { get; set; } = [];
    public ITaskItem[] PackageDependencies { get; set; } = [];
    public ITaskItem[] ProjectReferencesWithVersions { get; set; } = [];
    public ITaskItem[] FrameworksWithSuppressedDependencies { get; set; } = [];
    public bool IncludeBuildOutput { get; set; } = true;
    public bool IncludeContentInPack { get; set; } = true;
    public ITaskItem[] SymbolFiles { get; set; } = [];
    public ITaskItem[] SourceFiles { get; set; } = [];
    public bool IncludeSymbols { get; set; }
    public string SymbolPackageFormat { get; set; } = "snupkg";
    public bool IncludeSource { get; set; }
    public string? SymbolPackageOutputPath { get; set; }
    public string? NuspecOutputPath { get; set; }
    public string? ManifestOutputPath { get; set; }
    public string? DiagnosticOutputPath { get; set; }
    public bool SuppressDependenciesWhenPacking { get; set; }
    public bool NoBuild { get; set; }
    public string? ExpectedRequestHash { get; set; }
    public string? PackageTitle { get; set; }
    public string? PackageOwners { get; set; }
    public string? PackageSummary { get; set; }
    public string? PackageProjectUrl { get; set; }
    public string? PackageLicenseExpression { get; set; }
    public string? PackageLicenseFile { get; set; }
    public string? PackageIcon { get; set; }
    public string? PackageReadmeFile { get; set; }
    public string? PackageCopyright { get; set; }
    public string? PackageTags { get; set; }
    public string? PackageReleaseNotes { get; set; }
    public string? PackageType { get; set; }
    public string? RepositoryUrl { get; set; }
    public string? RepositoryType { get; set; }
    public string? RepositoryBranch { get; set; }
    public string? RepositoryCommit { get; set; }
    public bool PublishRepositoryUrl { get; set; }
    public string? SourceLinkJson { get; set; }
    public string? RestoreAssetsFilePath { get; set; }
    public string? ProjectAssetsFile { get; set; }
    public string? RestoreAssetsFileHash { get; set; }
    public string? RestoreLockFilePath { get; set; }
    public string? RestoreLockFileHash { get; set; }
    public ITaskItem[] RestoreTargetKeys { get; set; } = [];
    public string? RestoreNuGetVersion { get; set; }
    public string? RestoreSdkVersion { get; set; }
    public bool RestoreEvidenceRequired { get; set; }
    public string? SourceDateEpoch { get; set; }

    public override bool Execute()
    {
        try
        {
            var inputs = inputAdapter.Adapt(
                PackageId,
                PackageVersion,
                Authors,
                Description,
                PackageOutputPath,
                FilterPackageFiles(PackageFiles),
                PackageDependencies,
            CanonicalTargetFramework) with
            {
                TargetIdentities = TargetProfiles.Select(AdaptTarget).ToImmutableArray(),
                Targets = TargetProfiles.Select(AdaptProfile).ToImmutableArray(),
                Metadata = new PackageMetadata(
                    Authors,
                    Description,
                    PackageTitle,
                    PackageOwners,
                    PackageSummary,
                    PackageProjectUrl,
                    PackageLicenseExpression,
                    PackageLicenseFile,
                    PackageIcon,
                    PackageReadmeFile,
                    PackageCopyright,
                    PackageTags,
                    PackageReleaseNotes,
                    RepositoryUrl,
                    RepositoryType,
                    RepositoryBranch,
                    RepositoryCommit,
                    PackageType,
                    PublishRepositoryUrl: PublishRepositoryUrl),
                Symbols = new SymbolInputs(
                    IncludeSymbols,
                    SymbolPackageFormat,
                    SymbolFiles.Select(AdaptSymbol).ToImmutableArray()),
                Source = new SourceInputs(
                    IncludeSource,
                    SourceFiles.Select(AdaptSource).ToImmutableArray(),
                    PublishRepositoryUrl,
                    SourceLinkJson),
                SymbolOutputPath = SymbolPackageOutputPath,
                SuppressDependencies = SuppressDependenciesWhenPacking,
                NuspecOutputPath = NuspecOutputPath ?? BuildNuspecPath(NuspecOutputDirectory, PackageId),
                ManifestOutputPath = ManifestOutputPath,
                NoBuild = NoBuild,
                ExpectedRequestHash = ExpectedRequestHash,
                Determinism = DeterminismPolicy.Default with { EntryTimestamp = ResolveTimestamp(SourceDateEpoch) },
                Restore = new RestoreEvidence(
                    ProjectAssetsFile ?? RestoreAssetsFilePath ?? string.Empty,
                    RestoreAssetsFileHash ?? string.Empty,
                    RestoreLockFilePath,
                    RestoreLockFileHash,
                    RestoreTargetKeys.Select(static item => item.ItemSpec).ToImmutableArray(),
                    RestoreNuGetVersion ?? string.Empty,
                    RestoreSdkVersion ?? string.Empty)
                {
                    Required = RestoreEvidenceRequired
                }
            };
            var projectDependencies = ProjectReferencesWithVersions
                .Select(item => projectReferenceAdapter.Adapt(item, CanonicalTargetFramework))
                .Where(static dependency => dependency is not null)
                .Select(static dependency => dependency!)
                .ToArray();
            inputs = inputs with { Dependencies = inputs.Dependencies.Concat(projectDependencies).ToArray() };
            var result = packageBuilder.BuildPackage(inputs);
            Log.LogMessage(MessageImportance.Low, "Wrote canonical NetWasm package.");
            return result is not null;
        }
        catch (Exception exception)
        {
            var code = exception is NetWasmPackException packException ? packException.Code : NetWasmPackErrorCode.NWPK014;
            var message = exception is NetWasmPackException safeException ? safeException.SafeMessage : "The SDK-owned pack operation failed.";
            Log.LogError($"{code}: {message}");
            diagnosticWriter.Write(DiagnosticOutputPath, code, message);
            return false;
        }
    }

    private static SymbolInput AdaptSymbol(ITaskItem item) => new(
        item.GetMetadata("SourcePath"),
        item.GetMetadata("PackagePath"),
        "snupkg")
    {
        TargetFrameworkAlias = item.GetMetadata("TargetFrameworkAlias"),
        ExpectedSha256 = item.GetMetadata("ExpectedSha256"),
        ExpectedLength = ParseLength(item.GetMetadata("ExpectedLength"))
    };

    private static SourceInput AdaptSource(ITaskItem item) => new(
        item.GetMetadata("SourcePath"),
        item.GetMetadata("PackagePath"),
        item.GetMetadata("SourceRoot"))
    {
        ExpectedSha256 = item.GetMetadata("ExpectedSha256"),
        ExpectedLength = ParseLength(item.GetMetadata("ExpectedLength"))
    };

    private static long? ParseLength(string value) => long.TryParse(value, out var length) && length >= 0 ? length : null;

    private ITaskItem[] FilterPackageFiles(IReadOnlyList<ITaskItem> files) =>
        files.Where(item => IncludeBuildOutput || !string.Equals(item.GetMetadata("PackKind"), nameof(PackageFileKind.BuildOutput), StringComparison.OrdinalIgnoreCase))
            .Where(item => IncludeContentInPack || !string.Equals(item.GetMetadata("PackKind"), nameof(PackageFileKind.Content), StringComparison.OrdinalIgnoreCase))
            .ToArray();

    private TargetOutputIdentity AdaptTarget(ITaskItem item)
    {
        var identity = AdaptTargetIdentity(item);
        var suppressed = FrameworksWithSuppressedDependencies.Any(framework =>
            string.Equals(framework.ItemSpec, identity.Alias, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(framework.GetMetadata("Alias"), identity.Alias, StringComparison.OrdinalIgnoreCase));
        return identity with { SuppressDependencies = suppressed };
    }

    private static TargetOutputIdentity AdaptTargetIdentity(ITaskItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var alias = RequiredMetadata(item, "Alias", "TargetFrameworkAlias", "TargetFramework");
        var identifier = RequiredMetadata(item, "Identifier", "TargetFrameworkIdentifier");
        var version = RequiredMetadata(item, "Version", "TargetFrameworkVersion");
        var folder = RequiredMetadata(item, "AssetFolder", "TargetFrameworkMoniker", "TargetFramework");
        var dependencyGroup = OptionalMetadata(item, "DependencyGroup", "CanonicalDependencyGroup");
        return new TargetOutputIdentity(alias, identifier, version, folder, dependencyGroup);
    }

    private static TargetProfile AdaptProfile(ITaskItem item)
    {
        var identity = AdaptTargetIdentity(item);
        return new TargetProfile(
            identity.Alias,
            identity.Identifier,
            identity.Version,
            identity.AssetFolder,
            identity.DependencyGroup ?? identity.AssetFolder);
    }

    private static string RequiredMetadata(ITaskItem item, params string[] names)
    {
        foreach (var name in names)
        {
            var value = item.GetMetadata(name);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return item.ItemSpec;
    }

    private static string? OptionalMetadata(ITaskItem item, params string[] names)
    {
        foreach (var name in names)
        {
            var value = item.GetMetadata(name);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static string? BuildNuspecPath(string? directory, string packageId) =>
        string.IsNullOrWhiteSpace(directory) ? null : Path.Combine(directory, $"{packageId}.nuspec");

    private static DateTimeOffset ResolveTimestamp(string? sourceDateEpoch)
    {
        if (string.IsNullOrEmpty(sourceDateEpoch))
        {
            return DeterminismPolicy.Default.EntryTimestamp;
        }

        if (!long.TryParse(sourceDateEpoch, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var epoch))
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK013, "The source date epoch must be a whole-number Unix timestamp.");
        }

        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(epoch);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK013, "The source date epoch is outside the supported range.");
        }
    }
}
