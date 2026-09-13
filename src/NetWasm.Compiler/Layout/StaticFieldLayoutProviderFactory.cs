using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Layout;

internal sealed class StaticFieldLayoutProviderFactory : IStaticFieldLayoutProviderFactory
{
    public IStaticFieldLayoutProvider Create(ManagedLayoutSnapshot snapshot) =>
        new StaticFieldLayoutProvider(snapshot);
}
