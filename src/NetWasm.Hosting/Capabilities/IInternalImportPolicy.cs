namespace NetWasm.Hosting.Capabilities;

internal interface IInternalImportPolicy
{
    bool IsInternal(string module);
}
