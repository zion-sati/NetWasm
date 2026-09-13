using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Compiler.Tasks.ComponentModel;

internal interface IComponentBuildSession : IDisposable
{
    ComponentManifest Build(ComponentBuildRequest request);
}
