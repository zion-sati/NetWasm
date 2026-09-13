using System.Xml;

namespace NetWasm.Sdk.Pack.Policies;

public sealed class MetadataPolicy : IMetadataValidator
{
    private readonly Func<char, bool> xmlCharacterPolicy = XmlConvert.IsXmlChar;

    public void Validate(PackageMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        if (string.IsNullOrWhiteSpace(metadata.Authors) || string.IsNullOrWhiteSpace(metadata.Description))
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK006, "Package authors and description are required metadata.");
        }

        foreach (var value in EnumerateValues(metadata))
        {
            if (value is not null && value.Any(character => !xmlCharacterPolicy(character)))
            {
                throw new NetWasmPackException(NetWasmPackErrorCode.NWPK006, "Package metadata contains an invalid XML character.");
            }
        }

        if (metadata.PublishRepositoryUrl && string.IsNullOrWhiteSpace(metadata.RepositoryUrl))
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK006, "Public repository metadata requires a repository URL.");
        }
    }

    private static IEnumerable<string?> EnumerateValues(PackageMetadata metadata)
    {
        yield return metadata.Authors;
        yield return metadata.Description;
        yield return metadata.Title;
        yield return metadata.Owners;
        yield return metadata.Summary;
        yield return metadata.ProjectUrl;
        yield return metadata.LicenseExpression;
        yield return metadata.LicenseFile;
        yield return metadata.Icon;
        yield return metadata.Readme;
        yield return metadata.Copyright;
        yield return metadata.Tags;
        yield return metadata.ReleaseNotes;
        yield return metadata.RepositoryUrl;
        yield return metadata.RepositoryType;
        yield return metadata.RepositoryBranch;
        yield return metadata.RepositoryCommit;
        yield return metadata.PackageTypes;
    }
}
