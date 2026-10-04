namespace NetWasm.Runtime.Pack.Materialization;

internal interface IRuntimeArtifactReader
{
    byte[] Read(string path);
}
