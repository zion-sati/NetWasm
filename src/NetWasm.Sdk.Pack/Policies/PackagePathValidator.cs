namespace NetWasm.Sdk.Pack.Policies;

public sealed class PackagePathValidator : IPackagePathValidator
{
    public string Validate(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK009, "A package path is empty.");
        }

        var normalized = path.Replace('\\', '/');
        var segments = normalized.Split('/');
        if (normalized.StartsWith('/') || normalized.Contains(':', StringComparison.Ordinal) ||
            segments.Any(static segment => segment is "" or "." or ".." || segment.Any(char.IsControl)))
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK009, "A package path is absolute, traverses the archive, or contains invalid characters.");
        }

        return normalized;
    }
}
