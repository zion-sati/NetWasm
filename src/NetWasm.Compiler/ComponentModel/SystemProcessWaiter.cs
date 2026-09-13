using System;

namespace NetWasm.Compiler.ComponentModel;

internal sealed class SystemProcessWaiter : IProcessWaiter
{
    public void Wait(IProcessSession process)
    {
        ArgumentNullException.ThrowIfNull(process);
        process.Waiter.Wait();
    }
}
