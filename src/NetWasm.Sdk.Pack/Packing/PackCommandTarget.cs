namespace NetWasm.Sdk.Pack.Packing;

public sealed class PackCommandTarget : IPackageBuilder
{
    private readonly IPackRequestBuilder requestBuilder;
    private readonly IArchivePlanner archivePlanner;
    private readonly IPackOutputTransaction outputTransaction;

    public PackCommandTarget(
        IPackRequestBuilder requestBuilder,
        IArchivePlanner archivePlanner,
        IPackOutputTransaction outputTransaction)
    {
        this.requestBuilder = requestBuilder ?? throw new ArgumentNullException(nameof(requestBuilder));
        this.archivePlanner = archivePlanner ?? throw new ArgumentNullException(nameof(archivePlanner));
        this.outputTransaction = outputTransaction ?? throw new ArgumentNullException(nameof(outputTransaction));
    }

    public CanonicalPackResult BuildPackage(CanonicalPackInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        var package = requestBuilder.Build(inputs);
        var plan = archivePlanner.Plan(package);
        return outputTransaction.Commit(package, plan);
    }
}
