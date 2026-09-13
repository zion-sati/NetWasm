namespace NetWasm.TimeZones;

internal interface ITimeZoneManifestEncoder
{
    byte[] Encode(TimeZoneDeploymentManifest manifest);
}
