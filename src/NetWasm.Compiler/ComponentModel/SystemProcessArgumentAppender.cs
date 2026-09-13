using System;

namespace NetWasm.Compiler.ComponentModel;

internal sealed class SystemProcessArgumentAppender : IProcessArgumentAppender
{
    public void Append(IProcessSession process, string argument)
    {
        ArgumentNullException.ThrowIfNull(process);
        process.Arguments.Append(argument);
    }
}
