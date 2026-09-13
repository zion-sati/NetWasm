using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Layout;

internal sealed class StaticDataLayoutProviderFactory : IStaticDataLayoutProviderFactory
{
    public IStaticDataLayout Create(ManagedLayoutSnapshot snapshot) =>
        new StaticDataLayoutProvider(snapshot);
}
