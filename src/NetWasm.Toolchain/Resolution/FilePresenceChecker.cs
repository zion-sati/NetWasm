using System.IO;

namespace NetWasm.Toolchain.Resolution;

public sealed class FilePresenceChecker : IFilePresenceChecker
{
    public bool Exists(string path) => File.Exists(path);
}
