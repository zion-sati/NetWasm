using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Layout;

internal interface ITypeLayoutProviderFactory
{
    ITypeLayoutProvider Create(
        ITypeDefinitionResolver types,
        ManagedLayoutSnapshot snapshot);
}
