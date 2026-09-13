using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal sealed class MetadataTypeClassifier(
    ITypeIdentityResolver identities,
    ITypeDefinitionResolver definitions,
    IMetadataIdentityBaseTypeResolver baseTypes) : ITypeClassifier
{
    public bool IsDelegateType(EntityKey type)
    {
        var current = (CliTypeIdentity?)identities.GetTypeIdentity(type);
        while (current is not null)
        {
            var definition = definitions.ResolveTypeIdentity(current);
            if (definition.FullName is "System.Delegate" or
                "System.MulticastDelegate")
            {
                return true;
            }
            if (definition.GenericArity != 0 &&
                current.Shape != CliTypeShape.GenericInstantiation)
            {
                current = CliTypeIdentity.GenericInstantiation(
                    current,
                    [.. Enumerable.Range(0, definition.GenericArity)
                        .Select(index => CliTypeIdentity.GenericParameter(false, index))]);
            }
            current = baseTypes.GetBaseType(current);
        }
        return false;
    }
}
