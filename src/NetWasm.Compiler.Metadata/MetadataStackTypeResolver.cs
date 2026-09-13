using System;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal sealed class MetadataStackTypeResolver(
    ITypeDefinitionResolver typeDefinitions) : IMetadataStackTypeResolver
{
    private readonly ITypeDefinitionResolver _typeDefinitions =
        typeDefinitions ?? throw new ArgumentNullException(nameof(typeDefinitions));

    public CliTypeIdentity Resolve(CliTypeIdentity type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (type.StackKind != CliValueKind.ValueType ||
            type.Shape is not (CliTypeShape.Named or CliTypeShape.GenericInstantiation))
        {
            return type;
        }

        var definition = _typeDefinitions.ResolveTypeIdentity(type);
        return definition.IsEnum
            ? type.WithStackStorageType(definition.EnumUnderlyingType)
            : type;
    }

    public CliTypeIdentity Resolve(CliTypeIdentity type, CliGenericContext genericContext)
    {
        var context = genericContext.Normalize();
        return Resolve(type.Substitute(context.TypeArguments, context.MethodArguments));
    }

    public MethodSignatureModel Resolve(MethodSignatureModel signature)
    {
        ArgumentNullException.ThrowIfNull(signature);
        return new MethodSignatureModel(
            Resolve(signature.ReturnSignatureType),
            [.. signature.ParameterSignatureTypes.Select(Resolve)]);
    }

    public MethodSignatureModel Resolve(MethodSignatureModel signature, CliGenericContext genericContext)
    {
        var context = genericContext.Normalize();
        return Resolve(signature.Substitute(context.TypeArguments, context.MethodArguments));
    }
}
