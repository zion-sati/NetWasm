using System;

namespace NetWasm.Toolchain.Prerequisites;

public sealed class UnsupportedHostToolException : InvalidOperationException
{
    public UnsupportedHostToolException(string toolId)
        : base($"Host tool '{toolId}' has no compatibility validator.")
    {
        ToolId = toolId;
    }

    public string ToolId { get; }
}
