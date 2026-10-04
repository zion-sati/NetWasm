namespace NetWasm.Runtime.Pack.Materialization;

internal interface IRuntimeNativeArchiveReader
{
    string Read(string path);
}
