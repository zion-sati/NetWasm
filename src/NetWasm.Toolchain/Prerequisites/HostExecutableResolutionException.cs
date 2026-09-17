using System;

namespace NetWasm.Toolchain.Prerequisites;

public enum HostExecutableResolutionFailure
{
    PathUnavailable,
    InvalidPathEntry,
    InvalidOverride,
    InvalidFallback,
    ConfiguredExecutableNotFound,
    ExecutableNotFound,
}

public sealed class HostExecutableResolutionException : InvalidOperationException
{
    public HostExecutableResolutionException(
        string toolId,
        HostExecutableResolutionFailure failure,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ToolId = toolId;
        Failure = failure;
    }

    public string ToolId { get; }

    public HostExecutableResolutionFailure Failure { get; }
}
