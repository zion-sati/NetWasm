using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Layout;

internal interface IValueLayoutResolverFactory
{
    IValueLayoutResolver Create(
        ITypeDefinitionResolver types,
        IFieldRepository fields,
        WasmTargetLayout target,
        ManagedValueLayoutState state);
}
