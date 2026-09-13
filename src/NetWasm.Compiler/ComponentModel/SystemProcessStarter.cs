using System;

namespace NetWasm.Compiler.ComponentModel;

internal sealed class SystemProcessStarter : IProcessStarter
{
    public void Start(IProcessSession process)
    {
        ArgumentNullException.ThrowIfNull(process);
        process.Starter.Start();
    }
}
