using System;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal sealed class CustomAttributeMemberResolver(
    ImmutableDictionary<AssemblyIdentity, MetadataAssemblySnapshot> sources,
    ImmutableArray<PropertyDefinitionModel> properties,
    ITypeDefinitionResolver definitions,
    IFieldRepository fields,
    IMethodRepository methods,
    IMetadataFieldReferenceResolver fieldInstances,
    IMethodInstanceResolver methodInstances) : ICustomAttributeMemberResolver
{
    public CustomAttributeMemberBinding? Resolve(CliTypeIdentity declaringType, string name,
        CustomAttributeNamedArgumentKind kind)
    {
        var definition = definitions.ResolveTypeIdentity(declaringType);
        var source = sources[definition.Key.Assembly];
        var context = new CliGenericContext(declaringType.TypeArguments, []);
        if (kind == CustomAttributeNamedArgumentKind.Field)
        {
            var field = definition.Fields.Select(fields.GetField).SingleOrDefault(field => field.Name == name);
            if (field is null) return null;
            var attributes = source.Reader.GetFieldDefinition(
                (FieldDefinitionHandle)MetadataTokens.Handle(field.Key.MetadataToken)).Attributes;
            if ((attributes & FieldAttributes.FieldAccessMask) != FieldAttributes.Public) return null;
            if (field.IsStatic || field.IsLiteral || field.IsInitOnly)
                throw Unsupported(name);
            return new(fieldInstances.Resolve(source, field.Key.MetadataToken, name, 0, context), null);
        }
        if (kind != CustomAttributeNamedArgumentKind.Property) throw Unsupported(name);
        var property = properties.SingleOrDefault(property =>
            property.DeclaringType == definition.Key && property.Name == name);
        if (property is null) return null;
        if (!(property.Getter is { } getter && methods.GetMethod(getter).IsPublic) &&
            !(property.Setter is { } publicSetter && methods.GetMethod(publicSetter).IsPublic)) return null;
        if (property.Setter is not { } setterKey || !property.IndexParameterTypes.IsEmpty)
            throw Unsupported(name);
        var setter = methods.GetMethod(setterKey);
        if (setter.IsStatic || !setter.IsPublic || setter.Signature.ParameterTypes.Length != 1 ||
            setter.Signature.ReturnType != CliValueKind.Void)
            throw Unsupported(name);
        return new(null, methodInstances.ResolveMethodInstance(setterKey.Assembly,
            setterKey.MetadataToken, name, 0, context));
    }

    private static CompilerException Unsupported(string name) => new(new CompilerDiagnostic(
        DiagnosticCode.UnsupportedMetadata, $"custom attribute named argument '{name}' is not a writable public instance member"));
}
