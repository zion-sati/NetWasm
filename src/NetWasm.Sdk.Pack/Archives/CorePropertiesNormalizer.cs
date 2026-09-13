using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

using NetWasm.Sdk.Pack.Policies;

namespace NetWasm.Sdk.Pack.Archives;

public sealed class CorePropertiesNormalizer : ICorePropertiesNormalizer
{
    private const string CorePropertiesPrefix = "package/services/metadata/core-properties/";
    private const string StableCorePropertiesPath = CorePropertiesPrefix + "core-properties.psmdcp";
    private const string StableCorePropertiesPartName = "/" + StableCorePropertiesPath;

    public PackageArchive Normalize(PackageArchive archive)
    {
        ArgumentNullException.ThrowIfNull(archive);
        var coreEntries = archive.Entries.Where(static entry => IsCorePropertiesPath(entry.Path)).ToArray();
        if (coreEntries.Length > 1)
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK008, "The package archive contains multiple core-properties entries.");
        }

        var source = coreEntries.FirstOrDefault();
        var entries = archive.Entries
            .Select(entry => source is not null && string.Equals(entry.Path, source.Path, StringComparison.Ordinal)
                ? ArchiveEntry.FromBytes(StableCorePropertiesPath, NormalizeCoreProperties(source.Content.AsSpan(), archive.Policy.EntryTimestamp), entry.SourcePath)
                : NormalizeRelationships(entry, source?.Path))
            .Select(entry => NormalizeContentTypes(entry, source?.Path))
            .ToImmutableArray();
        return archive with { Entries = entries };
    }

    private static bool IsCorePropertiesPath(string path) =>
        path.StartsWith(CorePropertiesPrefix, StringComparison.Ordinal) &&
        path.EndsWith(".psmdcp", StringComparison.OrdinalIgnoreCase) &&
        path.Length > CorePropertiesPrefix.Length + ".psmdcp".Length;

    private static ArchiveEntry NormalizeRelationships(ArchiveEntry entry, string? sourcePath)
    {
        if (!string.Equals(entry.Path, "_rels/.rels", StringComparison.Ordinal))
        {
            return entry;
        }

        try
        {
            var document = ParseXml(entry.Content.AsSpan());
            var root = document.Root!;

            var whitespaceChanged = RemoveInsignificantWhitespace(document);
            var relationships = root.Elements()
                .Where(static element => element.Name.LocalName == "Relationship")
                .Select(element => NormalizeRelationship(element, sourcePath))
                .ToArray();
            var ordered = relationships
                .OrderBy(static relationship => relationship.Type, StringComparer.Ordinal)
                .ThenBy(static relationship => relationship.TargetMode, StringComparer.Ordinal)
                .ThenBy(static relationship => relationship.Target, StringComparer.Ordinal)
                .ToArray();

            var changed = whitespaceChanged || relationships.Any(static relationship => relationship.Changed) ||
                !relationships.Select(static relationship => relationship.Element)
                    .SequenceEqual(ordered.Select(static relationship => relationship.Element));
            if (!changed)
            {
                return entry;
            }

            var duplicateOrdinals = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var relationship in ordered)
            {
                var key = relationship.Type + "\n" + relationship.TargetMode + "\n" + relationship.Target;
                var ordinal = duplicateOrdinals.GetValueOrDefault(key);
                duplicateOrdinals[key] = ordinal + 1;
                var id = BuildRelationshipId(key, ordinal);
                relationship.Element.SetAttributeValue("Id", id);
            }

            foreach (var relationship in relationships)
            {
                relationship.Element.Remove();
            }

            foreach (var relationship in ordered)
            {
                root.Add(relationship.Element);
            }

            return ArchiveEntry.FromBytes(entry.Path, SerializeXml(document), entry.SourcePath);
        }
        catch (XmlException)
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK014, "The package relationships metadata is not well-formed XML.");
        }
    }

    private static RelationshipData NormalizeRelationship(XElement element, string? sourcePath)
    {
        var type = element.Attribute("Type")?.Value ?? string.Empty;
        var target = element.Attribute("Target")?.Value ?? string.Empty;
        var targetMode = element.Attribute("TargetMode")?.Value ?? string.Empty;
        var normalizedTarget = NormalizeRelationshipTarget(target, sourcePath);
        var changed = !string.Equals(target, normalizedTarget, StringComparison.Ordinal);
        if (changed)
        {
            element.SetAttributeValue("Target", normalizedTarget);
            target = normalizedTarget;
        }

        var key = type + "\n" + targetMode + "\n" + target;
        var expectedId = BuildRelationshipId(key, 0);
        var id = element.Attribute("Id")?.Value;
        changed |= !string.Equals(id, expectedId, StringComparison.Ordinal);
        return new RelationshipData(element, type, targetMode, target, changed);
    }

    private static string NormalizeRelationshipTarget(string target, string? sourcePath)
    {
        if (sourcePath is null || string.IsNullOrWhiteSpace(target))
        {
            return target;
        }

        return string.Equals(target.TrimStart('/').Replace('\\', '/'), sourcePath.TrimStart('/').Replace('\\', '/'), StringComparison.Ordinal)
            ? StableCorePropertiesPartName
            : target;
    }

    private static ArchiveEntry NormalizeContentTypes(ArchiveEntry entry, string? sourcePath)
    {
        if (!string.Equals(entry.Path, "[Content_Types].xml", StringComparison.Ordinal) || sourcePath is null)
        {
            return entry;
        }

        try
        {
            var document = ParseXml(entry.Content.AsSpan());
            var changed = RemoveInsignificantWhitespace(document);
            var sourcePartName = "/" + sourcePath.TrimStart('/').Replace('\\', '/');
            foreach (var overrideElement in document.Descendants().Where(static element => element.Name.LocalName == "Override"))
            {
                var partName = overrideElement.Attribute("PartName");
                if (partName is not null && string.Equals(partName.Value.TrimStart('/').Replace('\\', '/'), sourcePartName.TrimStart('/'), StringComparison.Ordinal))
                {
                    if (!string.Equals(partName.Value, StableCorePropertiesPartName, StringComparison.Ordinal))
                    {
                        partName.Value = StableCorePropertiesPartName;
                        changed = true;
                    }
                }
            }

            return changed
                ? ArchiveEntry.FromBytes(entry.Path, SerializeXml(document), entry.SourcePath)
                : entry;
        }
        catch (XmlException)
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK014, "The package content-types metadata is not well-formed XML.");
        }
    }

    private static byte[] NormalizeCoreProperties(ReadOnlySpan<byte> content, DateTimeOffset timestamp)
    {
        try
        {
            var document = ParseXml(content);
            var changed = false;
            var value = timestamp.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);
            foreach (var element in document.Descendants().Where(static element => element.Name.LocalName is "created" or "modified"))
            {
                if (!string.Equals(element.Value, value, StringComparison.Ordinal))
                {
                    element.Value = value;
                    changed = true;
                }
            }

            return changed ? SerializeXml(document) : content.ToArray();
        }
        catch (XmlException)
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK014, "The package core-properties metadata is not well-formed XML.");
        }
    }

    private static XDocument ParseXml(ReadOnlySpan<byte> content)
    {
        using var stream = new MemoryStream(content.ToArray(), writable: false);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = false,
            IgnoreWhitespace = false
        });
        return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
    }

    private static byte[] SerializeXml(XDocument document)
    {
        using var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(stream, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = true,
            NewLineChars = "\n",
            OmitXmlDeclaration = false
        }))
        {
            document.Save(writer);
        }

        return stream.ToArray();
    }

    private static bool RemoveInsignificantWhitespace(XDocument document)
    {
        var whitespace = document
            .DescendantNodes()
            .OfType<XText>()
            .Where(static text => string.IsNullOrWhiteSpace(text.Value))
            .ToArray();
        foreach (var text in whitespace)
        {
            text.Remove();
        }

        return whitespace.Length > 0;
    }

    private static string BuildRelationshipId(string key, int ordinal) =>
        "R" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16] +
        (ordinal == 0 ? string.Empty : ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private sealed record RelationshipData(XElement Element, string Type, string TargetMode, string Target, bool Changed);
}
