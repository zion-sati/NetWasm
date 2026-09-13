using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Layout;

internal sealed class TypeLayoutProviderFactory : ITypeLayoutProviderFactory
{
    public ITypeLayoutProvider Create(
        ITypeDefinitionResolver types,
        ManagedLayoutSnapshot snapshot) => new TypeLayoutProvider(types, snapshot);
}
