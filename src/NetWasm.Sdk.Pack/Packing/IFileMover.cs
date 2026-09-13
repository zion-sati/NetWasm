namespace NetWasm.Sdk.Pack.Packing;

/// <summary>
/// Moves one file as part of an output publication operation.
/// </summary>
public interface IFileMover
{
    void Move(string sourcePath, string destinationPath, bool overwrite);
}
