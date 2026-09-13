using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Layout;

internal sealed class ValueLayoutProviderFactory : IValueLayoutProviderFactory
{
    public IValueLayoutProvider Create(IValueLayoutResolver resolver) =>
        new ValueLayoutProvider(resolver);
}
