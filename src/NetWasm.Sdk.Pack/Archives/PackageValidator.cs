using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace NetWasm.Sdk.Pack.Archives;

public sealed class PackageValidator : IPackageValidator
{
    private readonly IPackagePathValidator pathValidator;

    public PackageValidator(IPackagePathValidator pathValidator)
    {
        this.pathValidator = pathValidator ?? throw new ArgumentNullException(nameof(pathValidator));
    }

    public void Validate(ArchivePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Policy is null || plan.Identity is null || string.IsNullOrWhiteSpace(plan.OutputPath) || plan.Entries.IsDefaultOrEmpty)
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK014, "The package archive plan is empty.");
        }

        if (plan.Policy.EntryTimestamp < new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero) ||
            plan.Policy.EntryTimestamp > new DateTimeOffset(2107, 12, 31, 23, 59, 58, TimeSpan.Zero) ||
            plan.Policy.EntryTimestamp.Offset != TimeSpan.Zero ||
            plan.Policy.EntryTimestamp.Ticks % TimeSpan.TicksPerSecond != 0)
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK013, "The deterministic archive policy is incomplete.");
        }

        if (plan.Policy.CompressionLevel < 0 || plan.Policy.CompressionLevel > 9 || !plan.Policy.Utf8Names ||
            plan.Entries.Length > plan.Policy.MaxEntries || plan.Policy.MaxEntries <= 0 || plan.Policy.MaxEntryBytes <= 0 || plan.Policy.MaxArchiveBytes <= 0)
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK013, "The deterministic archive policy is incomplete.");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var totalBytes = 0L;
        foreach (var entry in plan.Entries)
        {
            var path = pathValidator.Validate(entry.Path);
            if (!seen.Add(path) || !string.Equals(path, entry.Path, StringComparison.Ordinal))
            {
                throw new NetWasmPackException(NetWasmPackErrorCode.NWPK008, "The package archive contains a duplicate or non-normalized path.");
            }

            if (entry.Content.IsDefault)
            {
                throw new NetWasmPackException(NetWasmPackErrorCode.NWPK014, "The package archive contains an uninitialized entry.");
            }

            if (entry.Content.Length > plan.Policy.MaxEntryBytes)
            {
                throw new NetWasmPackException(NetWasmPackErrorCode.NWPK014, "A package archive entry exceeds the deterministic size policy.");
            }

            if (entry.Content.Length > plan.Policy.MaxArchiveBytes - totalBytes)
            {
                throw new NetWasmPackException(NetWasmPackErrorCode.NWPK014, "The package archive exceeds the deterministic size policy.");
            }

            totalBytes += entry.Content.Length;
        }

        if (!seen.Contains("[Content_Types].xml") || !seen.Contains("_rels/.rels") || !seen.Contains($"{plan.Identity.Id}.nuspec"))
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK014, "The package envelope is incomplete.");
        }

        var nuspec = plan.Entries.First(entry => string.Equals(entry.Path, $"{plan.Identity.Id}.nuspec", StringComparison.Ordinal));
        var xml = Encoding.UTF8.GetString(nuspec.Content.AsSpan()).TrimStart('\uFEFF');
        if (string.Equals(xml, "NetWasm0.1", StringComparison.OrdinalIgnoreCase))
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK002, "The package contains a normalized custom framework identity.");
        }

        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            });
            var document = XDocument.Load(reader);
            if (document.Descendants()
                    .Attributes("targetFramework")
                    .Any(static attribute => string.Equals(attribute.Value, "NetWasm0.1", StringComparison.OrdinalIgnoreCase)))
            {
                throw new NetWasmPackException(NetWasmPackErrorCode.NWPK002, "The package contains a normalized custom framework identity.");
            }
        }
        catch (NetWasmPackException)
        {
            throw;
        }
        catch (XmlException)
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK014, "The package nuspec is not well-formed XML.");
        }
    }
}
