using System.Text;
using System.Xml;

namespace NetWasm.Sdk.Pack.Nuspec;

public sealed class NuspecDocumentWriter : INuspecWriter
{
    private const string LicenseExpressionUrl = "https://licenses.nuget.org/";
    private const string NuspecNamespace = "http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd";

    public byte[] Write(CanonicalPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        using var stream = new MemoryStream();
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = true,
            NewLineChars = "\n",
            NewLineHandling = NewLineHandling.Replace,
            OmitXmlDeclaration = false
        };

        using (var writer = XmlWriter.Create(stream, settings))
        {
            writer.WriteStartDocument();
            writer.WriteStartElement("package", NuspecNamespace);
            writer.WriteStartElement("metadata");
            WriteRequired(writer, "id", package.Identity.Id);
            WriteRequired(writer, "version", package.Identity.Version);
            WriteRequired(writer, "authors", package.Metadata.Authors);
            WriteRequired(writer, "description", package.Metadata.Description);
            WriteOptional(writer, "title", package.Metadata.Title);
            WriteOptional(writer, "owners", package.Metadata.Owners);
            WriteOptional(writer, "summary", package.Metadata.Summary);
            WriteOptional(writer, "projectUrl", package.Metadata.ProjectUrl);
            var licenseExpression = package.Metadata.LicenseExpression;
            if (!string.IsNullOrWhiteSpace(licenseExpression))
            {
                WriteOptional(writer, "license", licenseExpression, "type", "expression");
                WriteRequired(writer, "licenseUrl", LicenseExpressionUrl + Uri.EscapeDataString(licenseExpression));
            }
            else
            {
                WriteOptional(writer, "license", package.Metadata.LicenseFile, "type", "file");
            }
            WriteOptional(writer, "icon", package.Metadata.Icon);
            WriteOptional(writer, "readme", package.Metadata.Readme);
            WriteOptional(writer, "copyright", package.Metadata.Copyright);
            WriteOptional(writer, "tags", package.Metadata.Tags);
            WriteOptional(writer, "releaseNotes", package.Metadata.ReleaseNotes);
            if (package.Metadata.PublishRepositoryUrl && !string.IsNullOrWhiteSpace(package.Metadata.RepositoryUrl))
            {
                writer.WriteStartElement("repository");
                writer.WriteAttributeString("url", package.Metadata.RepositoryUrl);
                if (!string.IsNullOrWhiteSpace(package.Metadata.RepositoryType))
                {
                    writer.WriteAttributeString("type", package.Metadata.RepositoryType);
                }

                if (!string.IsNullOrWhiteSpace(package.Metadata.RepositoryBranch))
                {
                    writer.WriteAttributeString("branch", package.Metadata.RepositoryBranch);
                }

                if (!string.IsNullOrWhiteSpace(package.Metadata.RepositoryCommit))
                {
                    writer.WriteAttributeString("commit", package.Metadata.RepositoryCommit);
                }

                writer.WriteEndElement();
            }

            if (package.Metadata.DevelopmentDependency)
            {
                WriteRequired(writer, "developmentDependency", string.Empty);
            }

            if (package.Metadata.Serviceable)
            {
                WriteRequired(writer, "serviceable", string.Empty);
            }

            WritePackageTypes(writer, package.Metadata.PackageTypes);
            WriteDependencies(writer, package);
            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndDocument();
        }

        return stream.ToArray();
    }

    private static void WriteDependencies(XmlWriter writer, CanonicalPackage package)
    {
        if (package.SuppressDependencies || package.DependencyGroups.IsDefaultOrEmpty)
        {
            return;
        }

        writer.WriteStartElement("dependencies");
        var registeredGroups = package.Targets.IsDefaultOrEmpty
            ? [TargetProfile.NetWasmV01.CanonicalDependencyGroup]
            : package.Targets.Select(static target => target.CanonicalDependencyGroup).ToArray();
        if (package.DependencyGroups.Any(group => !registeredGroups.Contains(group.TargetFramework, StringComparer.Ordinal)))
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK002, "A dependency group is not an exact registered framework identity.");
        }

        if (package.DependencyGroups.Select(static group => group.TargetFramework).Distinct(StringComparer.Ordinal).Count() != package.DependencyGroups.Length)
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK006, "A dependency group occurs more than once.");
        }

        foreach (var group in package.DependencyGroups.OrderBy(static group => group.TargetFramework, StringComparer.Ordinal))
        {
            writer.WriteStartElement("group");
            writer.WriteAttributeString("targetFramework", group.TargetFramework);
            foreach (var dependency in group.Dependencies.OrderBy(static dependency => dependency.Id, StringComparer.OrdinalIgnoreCase))
            {
                writer.WriteStartElement("dependency");
                writer.WriteAttributeString("id", dependency.Id);
                writer.WriteAttributeString("version", dependency.VersionRange);
                if (!string.Equals(dependency.IncludeAssets, "all", StringComparison.OrdinalIgnoreCase))
                {
                    writer.WriteAttributeString("include", dependency.IncludeAssets);
                }

                if (!string.Equals(dependency.ExcludeAssets, "none", StringComparison.OrdinalIgnoreCase))
                {
                    writer.WriteAttributeString("exclude", dependency.ExcludeAssets);
                }

                writer.WriteEndElement();
            }

            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    }

    private static void WritePackageTypes(XmlWriter writer, string? packageTypes)
    {
        if (string.IsNullOrWhiteSpace(packageTypes))
        {
            return;
        }

        writer.WriteStartElement("packageTypes");
        foreach (var type in packageTypes.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Order(StringComparer.Ordinal))
        {
            writer.WriteStartElement("packageType");
            writer.WriteAttributeString("name", type);
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    }

    private static void WriteRequired(XmlWriter writer, string name, string value) => writer.WriteElementString(name, value);

    private static void WriteOptional(XmlWriter writer, string name, string? value, string? attributeName = null, string? attributeValue = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        writer.WriteStartElement(name);
        if (attributeName is not null)
        {
            writer.WriteAttributeString(attributeName, attributeValue);
        }

        writer.WriteString(value);
        writer.WriteEndElement();
    }
}
