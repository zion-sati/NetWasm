namespace NetWasm.Toolchain.Resolution;

public interface IFilePresenceChecker
{
    bool Exists(string path);
}
