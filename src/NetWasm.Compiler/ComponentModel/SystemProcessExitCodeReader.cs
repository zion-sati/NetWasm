using System;

namespace NetWasm.Compiler.ComponentModel;

internal sealed class SystemProcessExitCodeReader : IProcessExitCodeReader
{
    public int Read(IProcessSession process)
    {
        ArgumentNullException.ThrowIfNull(process);
        return process.ExitCode.Read();
    }
}
