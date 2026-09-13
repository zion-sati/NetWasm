namespace NetWasm.TimeZones;

internal interface ITimeZoneGenerationCommand
{
    TimeZoneDeploymentManifest Execute(TimeZoneGenerationRequest request);
}
