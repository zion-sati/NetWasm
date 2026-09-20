using System;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Layout;

internal sealed class ManagedLayoutForkSourceFactory(
    IValueLayoutResolverFactory resolvers,
    IValueLayoutProviderFactory values,
    IInstanceFieldLayoutProviderFactory fields) :
    IManagedLayoutForkSourceFactory
{
    public IManagedLayoutForkSource Create(
        ITargetLayout layout,
        ITypeRepository types,
        ITypeDefinitionResolver typeDefinitions,
        IFieldRepository fieldsRepository)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(types);
        ArgumentNullException.ThrowIfNull(typeDefinitions);
        ArgumentNullException.ThrowIfNull(fieldsRepository);
        if (layout is not ManagedLayoutSnapshot snapshot)
            throw new ArgumentException(
                "Managed layout forks require a managed layout snapshot.",
                nameof(layout));
        return new ManagedLayoutForkSetFactory(snapshot, types,
            typeDefinitions, fieldsRepository, resolvers, values, fields);
    }
}
