namespace NetWasm.Sdk.Pack.Policies;

public interface IMetadataValidator
{
    void Validate(PackageMetadata metadata);
}
