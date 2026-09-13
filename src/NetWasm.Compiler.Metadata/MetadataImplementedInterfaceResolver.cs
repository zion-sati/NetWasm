using System.Collections.Immutable;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal sealed class MetadataImplementedInterfaceResolver(
    ITypeDefinitionResolver typeDefinitions,
    IMetadataAssemblyResolver assemblies,
    IMetadataSignatureTypeResolver signatureTypes) : IImplementedInterfaceResolver
{
    private readonly Dictionary<CliTypeIdentity, ImmutableArray<CliTypeIdentity>> _interfaces = [];

    public ImmutableArray<CliTypeIdentity> GetInterfaces(CliTypeIdentity type)
    {
        if (_interfaces.TryGetValue(type, out var interfaces))
        {
            return interfaces;
        }

        interfaces = GetInterfacesUncached(type);
        _interfaces.Add(type, interfaces);
        return interfaces;
    }

    private ImmutableArray<CliTypeIdentity> GetInterfacesUncached(CliTypeIdentity type)
    {
        var definition = typeDefinitions.ResolveTypeIdentity(type);
        var source = assemblies.Resolve(definition.Key.Assembly);
        var context = new CliGenericContext(
            type.Shape == CliTypeShape.GenericInstantiation ? type.TypeArguments : [],
            []);
        return [.. source.ImplementedInterfaces[definition.Key.MetadataToken]
            .Select(handle => signatureTypes.Resolve(source, handle, context))];
    }
}
