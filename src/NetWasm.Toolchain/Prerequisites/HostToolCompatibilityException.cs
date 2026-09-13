using System;

namespace NetWasm.Toolchain.Prerequisites;

public enum HostToolCompatibilityFailure
{
    ToolIdentityMismatch,
    VersionBelowMinimum,
    VersionAtOrAboveMaximum,
    MissingCapability,
}

public sealed class HostToolCompatibilityException : InvalidOperationException
{
    public HostToolCompatibilityException(
        string toolId,
        HostToolCompatibilityFailure failure,
        string message)
        : base(message)
    {
        ToolId = toolId;
        Failure = failure;
    }

    public string ToolId { get; }

    public HostToolCompatibilityFailure Failure { get; }
}
