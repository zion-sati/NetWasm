namespace NetWasm.TimeZones;

internal interface IArtifactWriter
{
    void Write(string path, byte[] contents);
}
