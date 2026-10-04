using System;
using System.IO;

namespace NetWasm.Runtime.Pack.Materialization;

internal sealed class RuntimeNativeLinkWorkspaceFactory : IRuntimeNativeLinkWorkspaceFactory
{
    public IRuntimeNativeLinkWorkspace Create(string logDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logDirectory);
        var logs = Path.Combine(Path.GetFullPath(logDirectory), "native-run-" + Guid.NewGuid().ToString("N"));
        var directory = Path.Combine(logs, "work");
        Directory.CreateDirectory(directory);
        return new Workspace(directory, logs);
    }

    private sealed class Workspace(string directory, string logs) : IRuntimeNativeLinkWorkspace
    {
        public string DirectoryPath => directory;
        public string LogDirectoryPath => logs;

        public void Dispose()
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
