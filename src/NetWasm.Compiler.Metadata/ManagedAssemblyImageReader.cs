using System;
using System.IO;

namespace NetWasm.Compiler.Metadata;

public sealed class ManagedAssemblyImageReader : IManagedAssemblyImageReader
{
    public byte[] Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return File.ReadAllBytes(path);
    }
}
