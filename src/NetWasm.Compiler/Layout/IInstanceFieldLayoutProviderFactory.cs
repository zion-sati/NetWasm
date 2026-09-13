using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Layout;

internal interface IInstanceFieldLayoutProviderFactory
{
    IInstanceFieldLayoutProvider Create(ManagedLayoutSnapshot snapshot);
}
