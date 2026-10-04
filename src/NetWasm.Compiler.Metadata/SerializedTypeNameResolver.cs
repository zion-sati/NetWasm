using System;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection.Metadata;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal sealed class SerializedTypeNameResolver(
    ImmutableDictionary<(AssemblyIdentity Assembly, string Name), EntityKey> declarations,
    ITypeDefinitionResolver definitions,
    ITypeIdentityResolver identities,
    AssemblyIdentity coreAssembly) : ISerializedTypeNameResolver
{
    public CliTypeIdentity? Resolve(string? name, MetadataAssemblySnapshot source)
    {
        if (name is null) return null;
        if (!TypeName.TryParse(name, out var parsed, new TypeNameParseOptions { MaxNodes = int.MaxValue }))
        {
            throw Unsupported("custom attribute contains an invalid serialized type name");
        }
        return ResolveName(parsed);

        CliTypeIdentity ResolveName(TypeName type)
        {
            if (type.IsArray)
            {
                var element = ResolveName(type.GetElementType());
                return type.IsSZArray ? CliTypeIdentity.SzArray(element) :
                    CliTypeIdentity.Array(element, type.GetArrayRank());
            }
            if (type.IsPointer) return CliTypeIdentity.UnmanagedPointer(ResolveName(type.GetElementType()));
            if (type.IsByRef) return CliTypeIdentity.ManagedByReference(ResolveName(type.GetElementType()));
            if (type.IsConstructedGenericType)
            {
                var definition = ResolveName(type.GetGenericTypeDefinition());
                var arguments = type.GetGenericArguments().Select(ResolveName).ToImmutableArray();
                if (definitions.ResolveTypeIdentity(definition).GenericArity != arguments.Length)
                {
                    throw Unsupported("custom attribute serialized generic type has the wrong arity");
                }
                return CliTypeIdentity.GenericInstantiation(definition, arguments);
            }

            var assembly = source.AssemblyIdentityAliases.Canonicalize(
                type.AssemblyName is { } specified ? new(specified.Name) : source.Identity);
            var fullName = TypeName.Unescape(type.FullName);
            if (declarations.TryGetValue((assembly, fullName), out var key))
            {
                return identities.GetTypeIdentity(key);
            }
            if (type.AssemblyName is null && declarations.TryGetValue((coreAssembly, fullName), out key))
            {
                return identities.GetTypeIdentity(key);
            }
            throw Unsupported($"custom attribute serialized type '{type.AssemblyQualifiedName}' was not found");
        }
    }

    private static CompilerException Unsupported(string message) =>
        new(new CompilerDiagnostic(DiagnosticCode.UnsupportedMetadata, message));
}
