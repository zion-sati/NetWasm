using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Layout;

internal sealed class InstanceFieldLayoutProviderFactory :
    IInstanceFieldLayoutProviderFactory
{
    public IInstanceFieldLayoutProvider Create(ManagedLayoutSnapshot snapshot) =>
        new InstanceFieldLayoutProvider(snapshot);
}
