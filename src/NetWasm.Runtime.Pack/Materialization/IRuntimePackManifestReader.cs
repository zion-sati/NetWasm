namespace NetWasm.Runtime.Pack.Materialization;

internal interface IRuntimePackManifestReader
{
    RuntimePackManifest Read(string path);
}
