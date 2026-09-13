namespace NetWasm.Sdk.Pack.Packing;

internal static class PackComposition
{
    public static PackTaskDependencies CreateTaskDependencies() =>
        new(new MsBuildPackInputAdapter(), new ProjectReferenceAdapter(new MsBuildProjectPackageIdentityResolver()), CreateDefaultBuilder(), new FilePackDiagnosticWriter());

    public static IPackageBuilder CreateDefaultBuilder()
    {
        var pathValidator = new PackagePathValidator();
        var nuspecWriter = new NuspecDocumentWriter();
        var fileReader = new LocalPackageFileReader(new LocalInputFileStreamOpener());
        var archiveWriter = new DeterministicArchiveWriter(new PackageValidator(pathValidator), new PackageArchiveOutputValidator(pathValidator));
        var fingerprintBuilder = new PackRequestFingerprintBuilder(fileReader);
        var planner = new ArchiveEntryPlanner(nuspecWriter, fileReader, pathValidator);
        var symbols = new SymbolPackageWriter(nuspecWriter, fileReader, archiveWriter, pathValidator);
        var restoreValidator = new RestoreEvidenceValidator();
        var requestBuilder = new CanonicalPackPlanBuilder(
            new ProfileRegistry(),
            new DependencyPolicy(),
            pathValidator,
            new PackageIdentityValidator(),
            new MetadataPolicy(),
            restoreValidator,
            new FileRestoreEvidenceReader(restoreValidator),
            new PackCacheValidator(fingerprintBuilder),
            new TargetIdentityPolicy(),
            new LocalLinkTargetResolver());
        return new PackCommandTarget(
            requestBuilder,
            planner,
            new PackOutputTransaction(
                archiveWriter,
                symbols,
                new PackManifestBuilder(fingerprintBuilder),
                new PackManifestSerializer(),
                new AtomicPackArtifactWriter(),
                new FileMover()));
    }

    public static IPackageArchiveCanonicalizer CreateArchiveCanonicalizer()
    {
        var pathValidator = new PackagePathValidator();
        var archiveWriter = new DeterministicArchiveWriter(
            new PackageValidator(pathValidator),
            new PackageArchiveOutputValidator(pathValidator));
        return new PackageArchiveCanonicalizer(
            new PackageArchiveReader(pathValidator),
            new CorePropertiesNormalizer(),
            archiveWriter);
    }
}
