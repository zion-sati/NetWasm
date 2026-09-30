using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace NetWasm.Compiler.Tasks.MsBuild;

internal sealed class CompilerBuildMessageWriter(
    TaskLoggingHelper log) : ICompilerBuildMessageWriter
{
    private readonly TaskLoggingHelper _log = log ??
        throw new ArgumentNullException(nameof(log));

    public void Write(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        _log.LogMessage(MessageImportance.High, message);
    }
}
