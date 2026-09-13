using System;
using System.IO;

namespace NetWasm.Toolchain.Prerequisites;

public sealed class SystemHostPathCanonicalizer : IHostPathCanonicalizer
{
    public string Canonicalize(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.GetFullPath(path);
    }
}
