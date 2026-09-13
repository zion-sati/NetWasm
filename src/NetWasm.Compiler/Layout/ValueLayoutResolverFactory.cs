using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Layout;

internal sealed class ValueLayoutResolverFactory(
    IExplicitValueLayoutResolver explicitLayouts) : IValueLayoutResolverFactory
{
    private readonly IExplicitValueLayoutResolver _explicitLayouts =
        explicitLayouts ?? throw new ArgumentNullException(nameof(explicitLayouts));

    public IValueLayoutResolver Create(
        ITypeDefinitionResolver types,
        IFieldRepository fields,
        WasmTargetLayout target,
        ManagedValueLayoutState state) =>
        new ValueLayoutResolver(types, fields, _explicitLayouts, target, state);
}
