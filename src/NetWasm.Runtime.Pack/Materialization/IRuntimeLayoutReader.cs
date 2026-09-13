namespace NetWasm.Runtime.Pack.Materialization;

internal interface IRuntimeLayoutReader
{
    RuntimeLayout Read(string path);
}
