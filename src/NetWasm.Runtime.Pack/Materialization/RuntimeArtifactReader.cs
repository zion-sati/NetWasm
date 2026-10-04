using System;
using System.IO;

namespace NetWasm.Runtime.Pack.Materialization;

internal sealed class RuntimeArtifactReader : IRuntimeArtifactReader
{
    public byte[] Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return File.ReadAllBytes(path);
    }
}
