using System;
using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.UnsafeAccessors;

internal sealed class UnsafeAccessorSignatureReader(
    ImmutableDictionary<string, MetadataAssemblySnapshot> assemblies) : IUnsafeAccessorSignatureReader
{
    public MethodSignature<CliTypeIdentity> Read(MethodDefinitionModel method)
    {
        ArgumentNullException.ThrowIfNull(method);
        var assembly = assemblies[method.Key.Assembly.Name];
        var provider = new SignatureTypeProvider(assembly.Identity, assembly.Reader,
            assembly.AssemblyIdentityAliases, preserveCustomModifiers: true);
        return assembly.Reader.GetMethodDefinition((MethodDefinitionHandle)MetadataTokens.EntityHandle(method.Key.MetadataToken))
            .DecodeSignature(provider, genericContext: null);
    }

    public CliTypeIdentity Read(FieldDefinitionModel field)
    {
        ArgumentNullException.ThrowIfNull(field);
        var assembly = assemblies[field.Key.Assembly.Name];
        var provider = new SignatureTypeProvider(assembly.Identity, assembly.Reader,
            assembly.AssemblyIdentityAliases, preserveCustomModifiers: true);
        return assembly.Reader.GetFieldDefinition((FieldDefinitionHandle)MetadataTokens.EntityHandle(field.Key.MetadataToken))
            .DecodeSignature(provider, genericContext: null);
    }
}
