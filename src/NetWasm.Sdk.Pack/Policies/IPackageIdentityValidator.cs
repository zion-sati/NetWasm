namespace NetWasm.Sdk.Pack.Policies;

public interface IPackageIdentityValidator
{
    PackageIdentity Validate(PackageIdentity identity);
}
