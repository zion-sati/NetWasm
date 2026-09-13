using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Compiler.Tasks.ComponentModel;

internal interface IComponentManifestWriter
{
    void Write(ComponentManifestWriteRequest request);
}

internal sealed record ComponentManifestWriteRequest(
    string Path,
    ComponentManifest Manifest);
