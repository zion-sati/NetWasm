namespace NetWasm.Compiler.Tests.Correctness.TimeZoneInfoFacadeQualification;

internal sealed class TimeZoneInfoFacadeAssetConfiguration : IDisposable
{
    private TimeZoneInfoFacadeAssetConfiguration(
        string? assetPath,
        string timeZone,
        string? temporaryAssetPath)
    {
        AssetPath = assetPath;
        TimeZone = timeZone;
        TemporaryAssetPath = temporaryAssetPath;
    }

    internal string? AssetPath { get; }

    internal string TimeZone { get; }

    private string? TemporaryAssetPath { get; }

    internal static TimeZoneInfoFacadeAssetConfiguration FromEnvironment()
    {
        var assetPath = Environment.GetEnvironmentVariable(
            "NETWASM_TIMEZONE_ASSET_PATH");
        if (string.IsNullOrWhiteSpace(assetPath) || !File.Exists(assetPath))
        {
            throw new InvalidOperationException(
                "NETWASM_TIMEZONE_ASSET_PATH must name the selected timezone asset.");
        }

        return new(
            Path.GetFullPath(assetPath),
            Environment.GetEnvironmentVariable("TZ") is { Length: > 0 } timeZone
                ? timeZone
                : "Australia/Melbourne",
            null);
    }

    internal static TimeZoneInfoFacadeAssetConfiguration Missing(
        string timeZone) => new(null, timeZone, null);

    internal static TimeZoneInfoFacadeAssetConfiguration Corrupt(
        string sourceAssetPath,
        string directory,
        string timeZone)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceAssetPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(timeZone);
        if (!File.Exists(sourceAssetPath))
        {
            throw new InvalidOperationException(
                "NETWASM_TIMEZONE_ASSET_PATH must name the selected timezone asset.");
        }

        var corruptPath = Path.Combine(directory, "timezone-corrupt.nwtz");
        var contents = File.ReadAllBytes(sourceAssetPath);
        if (contents.Length == 0)
        {
            throw new InvalidOperationException("timezone asset is empty");
        }

        contents[^1] ^= 0x01;
        File.WriteAllBytes(corruptPath, contents);
        return new(corruptPath, timeZone, corruptPath);
    }

    public void Dispose()
    {
        if (TemporaryAssetPath is not null && File.Exists(TemporaryAssetPath))
        {
            File.Delete(TemporaryAssetPath);
        }
    }
}
