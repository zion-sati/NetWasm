using NetWasm.Compiler.ComponentModel.Raw;

namespace NetWasm.Compiler.Tasks.ComponentModel;

internal interface IRawModuleLinkSession : IDisposable
{
    void Link(RawModuleLinkRequest request);
}
