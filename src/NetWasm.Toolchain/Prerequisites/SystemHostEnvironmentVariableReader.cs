using System;

namespace NetWasm.Toolchain.Prerequisites;

public sealed class SystemHostEnvironmentVariableReader : IHostEnvironmentVariableReader
{
    public string? Read(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return Environment.GetEnvironmentVariable(name);
    }
}
