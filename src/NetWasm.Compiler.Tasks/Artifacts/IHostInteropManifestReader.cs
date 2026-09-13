using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tasks.Artifacts;

internal interface IHostInteropManifestReader
{
    HostInteropManifest Read(string path, string target);
}
