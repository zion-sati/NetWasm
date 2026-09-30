using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Layout;

internal interface IStaticDataLayoutProviderFactory
{
    IManagedStaticDataLayout Create(ManagedLayoutSnapshot snapshot);
}
