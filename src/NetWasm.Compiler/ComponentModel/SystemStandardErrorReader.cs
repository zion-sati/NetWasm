using System;
using System.Threading.Tasks;

namespace NetWasm.Compiler.ComponentModel;

internal sealed class SystemStandardErrorReader : IStandardErrorReader
{
    public Task<string> ReadAsync(IProcessSession process)
    {
        ArgumentNullException.ThrowIfNull(process);
        return process.StandardError.ReadAsync();
    }
}
