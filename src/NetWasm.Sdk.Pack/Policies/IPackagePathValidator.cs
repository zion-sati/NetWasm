namespace NetWasm.Sdk.Pack.Policies;

public interface IPackagePathValidator
{
    string Validate(string path);
}
