using System;
using System.IO;

namespace NetWasm.Toolchain.Prerequisites;

public sealed class PhysicalHostToolsPackageFileReader : IHostToolsPackageFileReader
{
    public HostToolsPackageFileContents Read(string absolutePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);
        if (!Path.IsPathFullyQualified(absolutePath))
        {
            throw new ArgumentException("Host-tools package file path must be absolute.", nameof(absolutePath));
        }

        var file = new FileInfo(absolutePath);
        var bytes = File.ReadAllBytes(absolutePath);
        var executable = OperatingSystem.IsWindows() ||
            (File.GetUnixFileMode(absolutePath) &
             (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
        return new(bytes, file.LinkTarget is not null, executable);
    }
}
