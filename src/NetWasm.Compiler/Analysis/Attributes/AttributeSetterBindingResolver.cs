using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis.Attributes;

internal sealed class AttributeSetterBindingResolver(
    IMethodInstanceResolver methods,
    EntityKey delegateConstructor,
    EntityKey invoke) : IAttributeSetterBindingResolver
{
    public AttributeSetterBinding Resolve(CliTypeIdentity valueType)
    {
        ArgumentNullException.ThrowIfNull(valueType);
        return new(
            methods.ResolveMethodInstance(delegateConstructor.Assembly, delegateConstructor.MetadataToken,
                "attribute setter delegate", 0, new([valueType], [])),
            methods.ResolveMethodInstance(invoke.Assembly, invoke.MetadataToken,
                "attribute setter invocation", 0, new([], [valueType])));
    }
}
