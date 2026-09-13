using System;
using System.Threading.Tasks;

namespace NetWasm.Compiler.ComponentModel;

internal sealed class SystemStandardOutputReader : IStandardOutputReader
{
    public Task<string> ReadAsync(IProcessSession process)
    {
        ArgumentNullException.ThrowIfNull(process);
        return process.StandardOutput.ReadAsync();
    }
}
