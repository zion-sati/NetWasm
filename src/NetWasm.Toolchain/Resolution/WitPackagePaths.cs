namespace NetWasm.Toolchain.Resolution;

public sealed record WitPackagePaths(
    string PackageId,
    string PackageVersion,
    string CommandPath,
    string DiagnosticCommandPath,
    string CompilerPath);

public interface IWitPackagePathResolver
{
    WitPackagePaths Resolve();
}
