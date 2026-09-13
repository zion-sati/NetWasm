using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Layout;

internal interface IStaticDataLayoutProviderFactory
{
    IStaticDataLayout Create(ManagedLayoutSnapshot snapshot);
}
