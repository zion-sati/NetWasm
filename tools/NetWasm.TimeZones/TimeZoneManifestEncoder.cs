using System.Text.Json;

namespace NetWasm.TimeZones;

internal sealed class TimeZoneManifestEncoder : ITimeZoneManifestEncoder
{
    public byte[] Encode(TimeZoneDeploymentManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return JsonSerializer.SerializeToUtf8Bytes(manifest, TimeZoneJsonContext.Default.TimeZoneDeploymentManifest);
    }
}
