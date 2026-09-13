using System.Collections.Immutable;
using NuGet.Versioning;

namespace NetWasm.Sdk.Pack.Packing;

public sealed class CanonicalPackPlanBuilder : IPackRequestBuilder
{
    public const string CanonicalTargetFramework = "NetWasm,Version=v0.1";

    private readonly IProfileResolver profileResolver;
    private readonly IDependencyValidator dependencyValidator;
    private readonly IPackagePathValidator pathValidator;
    private readonly IPackageIdentityValidator identityValidator;
    private readonly IMetadataValidator metadataPolicy;
    private readonly IRestoreEvidenceValidator restoreValidator;
    private readonly IRestoreEvidenceReader restoreReader;
    private readonly IPackCacheValidator cacheValidator;
    private readonly ITargetIdentityValidator targetIdentityValidator;
    private readonly ILinkTargetResolver linkTargetResolver;

    public CanonicalPackPlanBuilder(
        IProfileResolver profileResolver,
        IDependencyValidator dependencyValidator,
        IPackagePathValidator pathValidator,
        IPackageIdentityValidator identityValidator,
        IMetadataValidator metadataPolicy,
        IRestoreEvidenceValidator restoreValidator,
        IRestoreEvidenceReader restoreReader,
        IPackCacheValidator cacheValidator,
        ITargetIdentityValidator targetIdentityValidator,
        ILinkTargetResolver linkTargetResolver)
    {
        this.profileResolver = profileResolver ?? throw new ArgumentNullException(nameof(profileResolver));
        this.dependencyValidator = dependencyValidator ?? throw new ArgumentNullException(nameof(dependencyValidator));
        this.pathValidator = pathValidator ?? throw new ArgumentNullException(nameof(pathValidator));
        this.identityValidator = identityValidator ?? throw new ArgumentNullException(nameof(identityValidator));
        this.metadataPolicy = metadataPolicy ?? throw new ArgumentNullException(nameof(metadataPolicy));
        this.restoreValidator = restoreValidator ?? throw new ArgumentNullException(nameof(restoreValidator));
        this.restoreReader = restoreReader ?? throw new ArgumentNullException(nameof(restoreReader));
        this.cacheValidator = cacheValidator ?? throw new ArgumentNullException(nameof(cacheValidator));
        this.targetIdentityValidator = targetIdentityValidator ?? throw new ArgumentNullException(nameof(targetIdentityValidator));
        this.linkTargetResolver = linkTargetResolver ?? throw new ArgumentNullException(nameof(linkTargetResolver));
    }

    public CanonicalPackage Build(CanonicalPackInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        var identity = identityValidator.Validate(new PackageIdentity(inputs.Id, inputs.Version));
        var metadata = inputs.Metadata with { Authors = inputs.Authors, Description = inputs.Description };
        metadataPolicy.Validate(metadata);
        restoreValidator.Validate(inputs.Restore);
        var restore = restoreReader.Read(inputs.Restore);
        ValidateOutputPath(inputs.OutputPath);
        var declaredTargetIdentities = inputs.TargetIdentities is { Count: > 0 }
            ? inputs.TargetIdentities
            : [TargetOutputIdentity.NetWasmV01];
        var targetIdentities = targetIdentityValidator.Validate(declaredTargetIdentities);
        ValidateCanonicalTarget(inputs, targetIdentities);
        var profiles = ResolveProfiles(inputs, targetIdentities);
        restore = MapRestoreTargets(restore, profiles, targetIdentities);
        var files = BuildFiles(inputs, profiles, identity);
        ValidateDeclaredMetadataFiles(metadata, files);
        var groups = BuildDependencies(inputs, profiles);
        ValidateRestoreParity(inputs.Dependencies, restore);
        ValidateOptions(inputs, files);

        var package = new CanonicalPackage(
            identity.Id,
            identity.Version,
            metadata.Authors,
            metadata.Description,
            inputs.OutputPath,
            files,
            groups)
        {
            Identity = identity,
            Metadata = metadata,
            Targets = profiles,
            TargetIdentities = targetIdentities,
            Symbols = inputs.Symbols,
            Source = inputs.Source,
            Restore = restore,
            Determinism = inputs.Determinism,
            SymbolOutputPath = inputs.SymbolOutputPath,
            SuppressDependencies = inputs.SuppressDependencies,
            NoBuild = inputs.NoBuild,
            ExpectedRequestHash = inputs.ExpectedRequestHash,
            NuspecOutputPath = inputs.NuspecOutputPath,
            ManifestOutputPath = inputs.ManifestOutputPath
        };
        cacheValidator.Validate(inputs, package);
        return package;
    }

    private ImmutableArray<TargetProfile> ResolveProfiles(
        CanonicalPackInputs inputs,
        ImmutableArray<TargetOutputIdentity> targetIdentities)
    {
        var customIdentities = targetIdentities
            .Where(static identity => string.Equals(identity.Alias, TargetProfile.NetWasmV01.Alias, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var customIdentity = customIdentities[0];
        var declared = inputs.Targets ?? [];
        if (declared.Count == 0)
        {
            return [ResolveCustomProfile(customIdentity)];
        }

        var resolved = declared.Select(static profile =>
        {
            ArgumentNullException.ThrowIfNull(profile);
            return profile;
        }).ToImmutableArray();

        if (resolved.Select(static profile => profile.Alias).Distinct(StringComparer.OrdinalIgnoreCase).Count() != resolved.Length)
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK016, "A framework profile is declared more than once.");
        }

        if (!resolved.Any(profile => string.Equals(profile.Alias, customIdentity.Alias, StringComparison.OrdinalIgnoreCase)))
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK002, "The declared framework profiles do not include the canonical custom target.");
        }

        var registeredCustom = profileResolver.Resolve(customIdentity.Alias);
        var declaredCustom = resolved.First(profile => string.Equals(profile.Alias, customIdentity.Alias, StringComparison.OrdinalIgnoreCase));
        if (declaredCustom != registeredCustom)
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK002, "The declared custom framework profile does not match its registered identity.");
        }

        return resolved;
    }

    private TargetProfile ResolveCustomProfile(TargetOutputIdentity identity)
    {
        var profile = profileResolver.Resolve(identity.Alias);
        if (!string.Equals(profile.Identifier, identity.Identifier, StringComparison.Ordinal) ||
            !string.Equals(profile.Version, identity.Version, StringComparison.Ordinal) ||
            !string.Equals(profile.CanonicalFolder, identity.AssetFolder, StringComparison.Ordinal) ||
            !string.Equals(profile.CanonicalDependencyGroup, identity.DependencyGroup, StringComparison.Ordinal))
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK002, "The custom target identity does not match its registered profile.");
        }

        return profile;
    }

    private ImmutableArray<CanonicalPackageFile> BuildFiles(
        CanonicalPackInputs inputs,
        ImmutableArray<TargetProfile> profiles,
        PackageIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(inputs.Files);
        var files = inputs.Files
            .Where(static file => !file.ExcludeFromMainPackage)
            .Select(file =>
            {
                ArgumentNullException.ThrowIfNull(file);
                var source = Require(file.SourcePath, "A package source file is missing.");
                ValidateSourceRoot(source, file.SourceRoot);
                var target = pathValidator.Validate(file.TargetPath);
                ValidateTargetProfilePath(target, file.TargetFrameworkAlias, profiles);
                return new CanonicalPackageFile(source, target)
                {
                    Kind = file.Kind,
                    TargetFrameworkAlias = file.TargetFrameworkAlias,
                    SourceRoot = file.SourceRoot,
                    ExpectedSha256 = file.ExpectedSha256,
                    ExpectedLength = file.ExpectedLength
                };
            })
            .OrderBy(static file => file.TargetPath, StringComparer.Ordinal)
            .ToImmutableArray();

        var sourceFiles = BuildSourceFiles(inputs.Source, profiles);
        files = files.AddRange(sourceFiles);
        EnsureUniquePackagePaths(files, identity.Id);
        return files;
    }

    private ImmutableArray<CanonicalPackageFile> BuildSourceFiles(SourceInputs source, ImmutableArray<TargetProfile> profiles)
    {
        if (!source.IncludeSource)
        {
            return ImmutableArray<CanonicalPackageFile>.Empty;
        }

        return source.Files
            .Select(file =>
            {
                ArgumentNullException.ThrowIfNull(file);
                var sourcePath = Require(file.SourcePath, "A source input is missing.");
                ValidateSourceRoot(sourcePath, file.SourceRoot);
                var target = pathValidator.Validate(file.TargetPath);
                ValidateTargetProfilePath(target, file.TargetFrameworkAlias, profiles);
                return new CanonicalPackageFile(sourcePath, target)
                {
                    Kind = PackageFileKind.Content,
                    TargetFrameworkAlias = file.TargetFrameworkAlias,
                    SourceRoot = file.SourceRoot,
                    ExpectedSha256 = file.ExpectedSha256,
                    ExpectedLength = file.ExpectedLength
                };
            })
            .OrderBy(static file => file.TargetPath, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    private ImmutableArray<CanonicalPackageDependencyGroup> BuildDependencies(
        CanonicalPackInputs inputs,
        ImmutableArray<TargetProfile> profiles)
    {
        ArgumentNullException.ThrowIfNull(inputs.Dependencies);
        if (inputs.SuppressDependencies)
        {
            return ImmutableArray<CanonicalPackageDependencyGroup>.Empty;
        }

        var suppressedGroups = inputs.TargetIdentities?
            .Where(static identity => identity.SuppressDependencies)
            .Select(static identity => identity.DependencyGroup)
            .Where(static group => !string.IsNullOrWhiteSpace(group))
            .ToHashSet(StringComparer.Ordinal) ?? [];

        var dependenciesByGroup = inputs.Dependencies
            .Select(dependency =>
            {
                ArgumentNullException.ThrowIfNull(dependency);
                var profile = profiles.FirstOrDefault(candidate =>
                    string.Equals(candidate.CanonicalDependencyGroup, dependency.TargetFramework, StringComparison.Ordinal));
                if (profile is null)
                {
                    throw new NetWasmPackException(NetWasmPackErrorCode.NWPK006, "A dependency group is not registered for this package.");
                }

                return (profile, Dependency: dependencyValidator.Validate(dependency, profile));
            })
            .GroupBy(static item => item.profile.CanonicalDependencyGroup, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.ToArray(), StringComparer.Ordinal);

        return profiles
            .Where(profile => !suppressedGroups.Contains(profile.CanonicalDependencyGroup))
            .OrderBy(static profile => profile.CanonicalDependencyGroup, StringComparer.Ordinal)
            .Select(profile =>
            {
                var dependencies = dependenciesByGroup.GetValueOrDefault(profile.CanonicalDependencyGroup) ?? [];
                var orderedDependencies = dependencies
                    .OrderBy(static item => item.Dependency.Id, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                if (orderedDependencies.Select(static item => item.Dependency.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != orderedDependencies.Length)
                {
                    throw new NetWasmPackException(NetWasmPackErrorCode.NWPK006, "A dependency occurs more than once in a framework group.");
                }

                return new CanonicalPackageDependencyGroup(
                    profile.CanonicalDependencyGroup,
                    orderedDependencies.Select(static item => item.Dependency).ToImmutableArray())
                {
                    TargetFrameworkAlias = profile.Alias
                };
            })
            .ToImmutableArray();
    }

    private static void ValidateCanonicalTarget(CanonicalPackInputs inputs, ImmutableArray<TargetOutputIdentity> targetIdentities)
    {
        if (inputs.TargetIdentities is not { Count: > 0 } &&
            !string.Equals(inputs.CanonicalTargetFramework, CanonicalTargetFramework, StringComparison.Ordinal))
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK002, "Only the registered NetWasm 0.1 canonical identity is supported.");
        }

        var custom = targetIdentities
            .Where(identity => string.Equals(identity.Alias, TargetProfile.NetWasmV01.Alias, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (custom.Length != 1 || !string.Equals(custom[0].DependencyGroup, CanonicalTargetFramework, StringComparison.Ordinal))
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK002, "Only the registered NetWasm 0.1 canonical identity is supported.");
        }
    }

    private static void ValidateRestoreParity(
        IReadOnlyList<CanonicalPackageDependencyInput> dependencies,
        RestoreEvidence restore)
    {
        var graph = restore.Graph;
        if (graph.IsDefaultOrEmpty)
        {
            if ((restore.Required || !string.IsNullOrWhiteSpace(restore.AssetsFilePath)) && dependencies.Count > 0)
            {
                throw new NetWasmPackException(NetWasmPackErrorCode.NWPK006, "The restore graph contains no edge for a declared package dependency.");
            }

            return;
        }

        foreach (var dependency in dependencies)
        {
            var edge = graph.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, dependency.Id, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(candidate.TargetFramework, dependency.TargetFramework, StringComparison.Ordinal) &&
                RestoreRangeMatches(candidate.VersionRange ?? candidate.Version, dependency.VersionRange));
            if (edge is null)
            {
                throw new NetWasmPackException(NetWasmPackErrorCode.NWPK006, "A declared package dependency is absent from restore evidence.");
            }

            if (edge.IsPrivate || edge.IsDevelopmentDependency)
            {
                throw new NetWasmPackException(NetWasmPackErrorCode.NWPK007, "A private or development restore edge cannot enter a public dependency group.");
            }
        }
    }

    private static bool RestoreRangeMatches(string restoredRange, string declaredRange)
    {
        if (string.Equals(restoredRange, declaredRange, StringComparison.Ordinal))
        {
            return true;
        }

        // Restore evidence is canonicalized by the reader before it reaches
        // the plan builder. Compare the parsed ranges so equivalent NuGet
        // interval spellings remain equivalent without rewriting declarations.
        if (!VersionRange.TryParse(restoredRange, allowFloating: true, out var restored))
        {
            return false;
        }

        var declared = VersionRange.Parse(declaredRange);
        return declared.Equals(restored);
    }

    private static RestoreEvidence MapRestoreTargets(
        RestoreEvidence evidence,
        ImmutableArray<TargetProfile> profiles,
        ImmutableArray<TargetOutputIdentity> targetIdentities)
    {
        if (evidence.TargetKeys.IsDefaultOrEmpty)
        {
            return evidence;
        }

        var keys = evidence.TargetKeys.Select(key => MapTargetKey(key, profiles, targetIdentities)).ToImmutableArray();
        var graph = evidence.Graph.Select(edge => edge with
        {
            TargetFramework = MapTargetKey(edge.TargetFramework, profiles, targetIdentities)
        }).ToImmutableArray();
        return evidence with { TargetKeys = keys, Graph = graph };
    }

    private static string MapTargetKey(
        string key,
        ImmutableArray<TargetProfile> profiles,
        ImmutableArray<TargetOutputIdentity> targetIdentities)
    {
        var profile = profiles.FirstOrDefault(candidate =>
            string.Equals(candidate.Alias, key, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(candidate.CanonicalFolder, key, StringComparison.Ordinal) ||
            key.StartsWith(candidate.Alias + "/", StringComparison.OrdinalIgnoreCase));
        if (profile is not null)
        {
            return profile.CanonicalDependencyGroup;
        }

        var identity = targetIdentities.FirstOrDefault(candidate =>
            string.Equals(candidate.Alias, key, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(candidate.AssetFolder, key, StringComparison.Ordinal) ||
            key.StartsWith(candidate.Alias + "/", StringComparison.OrdinalIgnoreCase));
        if (identity is not null)
        {
            return identity.DependencyGroup ?? key;
        }

        throw new NetWasmPackException(NetWasmPackErrorCode.NWPK005, "The restored target key is not registered in the package target map.");
    }

    private static void ValidateTargetProfilePath(string target, string? alias, ImmutableArray<TargetProfile> profiles)
    {
        if (target.Contains("NetWasm0.1", StringComparison.OrdinalIgnoreCase) || target.Contains("netwasm0.1", StringComparison.OrdinalIgnoreCase))
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK002, "A package path uses the normalized custom framework spelling.");
        }

        if (alias is null)
        {
            return;
        }

        var profile = profiles.FirstOrDefault(candidate => string.Equals(candidate.Alias, alias, StringComparison.OrdinalIgnoreCase));
        if (profile is null || !target.Contains(profile.CanonicalFolder, StringComparison.Ordinal))
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK002, "A target output path does not use its exact framework identity.");
        }
    }

    private static void EnsureUniquePackagePaths(ImmutableArray<CanonicalPackageFile> files, string packageId)
    {
        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "[Content_Types].xml",
            "_rels/.rels",
            $"{packageId}.nuspec"
        };
        if (files.Any(file => reserved.Contains(file.TargetPath)))
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK008, "A package file collides with a reserved package entry.");
        }

        var duplicate = files.GroupBy(static file => file.TargetPath, StringComparer.OrdinalIgnoreCase).FirstOrDefault(static group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK008, "A package path occurs more than once.");
        }
    }

    private void ValidateDeclaredMetadataFiles(PackageMetadata metadata, ImmutableArray<CanonicalPackageFile> files)
    {
        ValidateDeclaredMetadataFile(metadata.Readme, "readme", files);
        ValidateDeclaredMetadataFile(metadata.LicenseFile, "license", files);
        ValidateDeclaredMetadataFile(metadata.Icon, "icon", files);
    }

    private void ValidateDeclaredMetadataFile(string? declaredPath, string metadataName, ImmutableArray<CanonicalPackageFile> files)
    {
        if (string.IsNullOrWhiteSpace(declaredPath))
        {
            return;
        }

        var normalizedPath = pathValidator.Validate(declaredPath);
        if (!files.Any(file => string.Equals(file.TargetPath, normalizedPath, StringComparison.Ordinal)))
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK011, $"The declared package {metadataName} file is missing from the package inputs.");
        }
    }

    private static void ValidateOptions(CanonicalPackInputs inputs, ImmutableArray<CanonicalPackageFile> files)
    {
        if (inputs.Determinism is null || inputs.Determinism.MaxEntries <= 0 || inputs.Determinism.MaxEntryBytes <= 0 || inputs.Determinism.MaxArchiveBytes <= 0 ||
            inputs.Determinism.EntryTimestamp < new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero) ||
            inputs.Determinism.EntryTimestamp > new DateTimeOffset(2107, 12, 31, 23, 59, 58, TimeSpan.Zero) ||
            inputs.Determinism.CompressionLevel is < 0 or > 9)
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK013, "The deterministic archive policy is incomplete.");
        }

        if (inputs.Symbols is null)
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK012, "Symbol inputs are missing.");
        }

        if (inputs.Symbols.IncludeSymbols && !string.Equals(inputs.Symbols.Format, "snupkg", StringComparison.OrdinalIgnoreCase))
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK012, "Only the supported snupkg symbol format is accepted.");
        }

        if (inputs.Symbols.IncludeSymbols && inputs.Symbols.Files.Any(file => file is null))
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK011, "A symbol input is missing.");
        }

        if (files.Any(file => file.SourcePath.Any(char.IsControl)))
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK009, "A package source path contains invalid characters.");
        }

        if (!string.IsNullOrWhiteSpace(inputs.Restore.AssetsFilePath) && string.IsNullOrWhiteSpace(inputs.Restore.AssetsFileHash))
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK004, "Restore evidence is missing its assets hash.");
        }
    }

    private static string Require(string? value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK011, message);
        }

        return value;
    }

    private void ValidateSourceRoot(string sourcePath, string? sourceRoot)
    {
        if (string.IsNullOrWhiteSpace(sourceRoot))
        {
            return;
        }

        string root;
        string path;
        try
        {
            root = Path.GetFullPath(sourceRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            path = Path.GetFullPath(sourcePath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK009, "A source input has an invalid path.");
        }

        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK009, "A source input is outside its declared source root.");
        }

        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            var resolved = linkTargetResolver.Resolve(path);
            if (resolved is not null && !resolved.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new NetWasmPackException(NetWasmPackErrorCode.NWPK009, "A source input resolves outside its declared source root.");
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK009, "A source input could not be checked against its declared source root.");
        }
    }

    private static void ValidateOutputPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Any(char.IsControl) || Path.GetFileName(path).Length == 0)
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK014, "The package output path is invalid.");
        }
    }
}
