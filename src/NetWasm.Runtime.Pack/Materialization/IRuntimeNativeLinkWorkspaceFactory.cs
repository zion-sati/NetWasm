using System;

namespace NetWasm.Runtime.Pack.Materialization;

internal interface IRuntimeNativeLinkWorkspaceFactory
{
    IRuntimeNativeLinkWorkspace Create(string logDirectory);
}

internal interface IRuntimeNativeLinkWorkspace : IDisposable
{
    string DirectoryPath { get; }
    string LogDirectoryPath { get; }
}
