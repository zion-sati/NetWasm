using System.Text.Json;

namespace NetWasm.TimeZones;

internal sealed class TimeZoneManifestEncoder : ITimeZoneManifestEncoder
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        NewLine = "\n",
    };

    public byte[] Encode(TimeZoneDeploymentManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return JsonSerializer.SerializeToUtf8Bytes(manifest, Options);
    }
}
