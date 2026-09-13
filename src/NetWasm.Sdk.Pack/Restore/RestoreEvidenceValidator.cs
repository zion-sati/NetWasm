using NuGet.Versioning;

namespace NetWasm.Sdk.Pack.Restore;

public sealed class RestoreEvidenceValidator : IRestoreEvidenceValidator
{
    public void Validate(RestoreEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        if (!evidence.Required && string.IsNullOrWhiteSpace(evidence.AssetsFilePath) && evidence.Graph.IsDefaultOrEmpty)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(evidence.AssetsFilePath) || string.IsNullOrWhiteSpace(evidence.AssetsFileHash))
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK004, "Restore assets evidence is incomplete.");
        }

        if (!string.IsNullOrWhiteSpace(evidence.LockFilePath) && string.IsNullOrWhiteSpace(evidence.LockFileHash))
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK004, "Restore lock-file evidence is incomplete.");
        }

        if (evidence.TargetKeys.IsDefaultOrEmpty || evidence.TargetKeys.Any(string.IsNullOrWhiteSpace) ||
            evidence.TargetKeys.Distinct(StringComparer.Ordinal).Count() != evidence.TargetKeys.Length)
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK005, "Restore evidence does not identify each restored target exactly once.");
        }

        var duplicate = evidence.Graph
            .GroupBy(static dependency => $"{dependency.Source}|{dependency.Id}|{dependency.Version}|{dependency.TargetFramework}", StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK006, "Restore evidence contains a duplicate dependency edge.");
        }

        foreach (var dependency in evidence.Graph)
        {
            if (!NuGetVersion.TryParse(dependency.Version, out _) ||
                dependency.VersionRange is not null && !VersionRange.TryParse(dependency.VersionRange, allowFloating: false, out _) ||
                string.IsNullOrWhiteSpace(dependency.Id) || string.IsNullOrWhiteSpace(dependency.TargetFramework))
            {
                throw new NetWasmPackException(NetWasmPackErrorCode.NWPK006, "Restore evidence contains an invalid dependency edge.");
            }
        }
    }
}
