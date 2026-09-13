namespace NetWasm.TimeZones;

internal sealed class IanaVersionReader : IIanaVersionReader
{
    public string Read(string sourceDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourceDirectory);
        var versionPath = Path.Combine(sourceDirectory, "version");
        if (File.Exists(versionPath))
        {
            return Validate(File.ReadAllText(versionPath).Trim());
        }
        var platformVersionPath = Path.Combine(sourceDirectory, "+VERSION");
        if (File.Exists(platformVersionPath))
        {
            return Validate(File.ReadAllText(platformVersionPath).Trim());
        }
        var sourcePath = Path.Combine(sourceDirectory, "tzdata.zi");
        if (!File.Exists(sourcePath))
        {
            throw new InvalidOperationException(
                "Timezone source does not identify its pinned IANA version.");
        }
        using var reader = File.OpenText(sourcePath);
        var firstLine = reader.ReadLine();
        const string prefix = "# version ";
        if (firstLine is null || !firstLine.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Timezone source has an invalid tzdata.zi version header.");
        }
        return Validate(firstLine[prefix.Length..].Trim());
    }

    private static string Validate(string version)
    {
        if (version.Length < 5 ||
            !version[..4].All(char.IsAsciiDigit) ||
            !version[4..].All(char.IsAsciiLetterLower))
        {
            throw new InvalidOperationException("IANA timezone version is invalid.");
        }
        return version;
    }
}
