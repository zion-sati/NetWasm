namespace NetWasm.Toolchain.Resolution;

public sealed record WitPackagePaths(
    string PackageId,
    string PackageVersion,
    string CommandPath,
    string AsyncCommandPath,
    string CompilerPath);

public interface IWitPackagePathResolver
{
    WitPackagePaths Resolve();
}
