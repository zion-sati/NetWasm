using System;
using System.Reflection.Metadata;
using NetWasm.Compiler.Core.UnsafeAccessors;

namespace NetWasm.Compiler.Metadata.UnsafeAccessors;

internal sealed class UnsafeAccessorDeclarationReader(
    ICustomAttributeTypeProvider<string> valueTypes) : IUnsafeAccessorDeclarationReader
{
    private const string AccessorAttribute = "System.Runtime.CompilerServices.UnsafeAccessorAttribute";
    private const string TranslationAttribute = "System.Runtime.CompilerServices.UnsafeAccessorTypeAttribute";

    public UnsafeAccessorDeclaration? Read(MetadataReader metadata, MethodDefinition method)
    {
        // CLR synthesis does not replace a real IL body, even when annotated.
        if (method.RelativeVirtualAddress != 0) return null;
        UnsafeAccessorDeclaration? declaration = null;
        foreach (var handle in method.GetCustomAttributes())
        {
            var attribute = metadata.GetCustomAttribute(handle);
            if (AttributeName(metadata, attribute) != AccessorAttribute) continue;
            if (declaration is not null)
            {
                declaration = declaration with { IsMalformed = true };
                continue;
            }
            try
            {
                var value = attribute.DecodeValue(valueTypes);
                int kind = -1;
                bool malformed = value.FixedArguments.Length != 1 || value.FixedArguments[0].Value is not int;
                if (!malformed) kind = (int)value.FixedArguments[0].Value!;
                string? name = null;
                bool specified = false;
                foreach (var argument in value.NamedArguments)
                {
                    if (argument.Name != "Name" || specified ||
                        argument.Kind != CustomAttributeNamedArgumentKind.Property ||
                        argument.Type != "String" || argument.Value is not (null or string))
                    {
                        malformed = true;
                        continue;
                    }
                    name = argument.Value as string;
                    specified = true;
                }
                declaration = new(kind, name, specified, malformed, false);
            }
            catch (BadImageFormatException)
            {
                declaration = new(-1, null, false, true, false);
            }
        }
        if (declaration is null) return null;
        foreach (var parameterHandle in method.GetParameters())
        {
            foreach (var handle in metadata.GetParameter(parameterHandle).GetCustomAttributes())
            {
                if (AttributeName(metadata, metadata.GetCustomAttribute(handle)) == TranslationAttribute)
                    return declaration with { HasTypeTranslation = true };
            }
        }
        return declaration;
    }

    private string AttributeName(MetadataReader metadata, CustomAttribute attribute)
    {
        var owner = attribute.Constructor.Kind == HandleKind.MethodDefinition
            ? metadata.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor).GetDeclaringType()
            : metadata.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent;
        return owner.Kind switch
        {
            HandleKind.TypeDefinition => valueTypes.GetTypeFromDefinition(metadata, (TypeDefinitionHandle)owner, 0),
            HandleKind.TypeReference => valueTypes.GetTypeFromReference(metadata, (TypeReferenceHandle)owner, 0),
            _ => string.Empty,
        };
    }
}
