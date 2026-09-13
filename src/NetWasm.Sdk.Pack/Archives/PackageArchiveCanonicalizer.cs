namespace NetWasm.Sdk.Pack.Archives;

public sealed class PackageArchiveCanonicalizer : IPackageArchiveCanonicalizer
{
    private readonly IPackageArchiveReader reader;
    private readonly ICorePropertiesNormalizer corePropertiesNormalizer;
    private readonly IArchiveWriter archiveWriter;

    public PackageArchiveCanonicalizer(
        IPackageArchiveReader reader,
        ICorePropertiesNormalizer corePropertiesNormalizer,
        IArchiveWriter archiveWriter)
    {
        this.reader = reader ?? throw new ArgumentNullException(nameof(reader));
        this.corePropertiesNormalizer = corePropertiesNormalizer ?? throw new ArgumentNullException(nameof(corePropertiesNormalizer));
        this.archiveWriter = archiveWriter ?? throw new ArgumentNullException(nameof(archiveWriter));
    }

    public PackageOutput Canonicalize(PackageArchiveNormalizationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var archive = reader.Read(request);
        var normalized = corePropertiesNormalizer.Normalize(archive);
        var plan = new ArchivePlan(request.OutputPath, normalized.Entries, normalized.Policy, normalized.Identity);
        return archiveWriter.Write(plan);
    }
}
