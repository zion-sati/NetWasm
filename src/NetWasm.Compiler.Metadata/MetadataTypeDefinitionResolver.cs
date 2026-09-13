using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal sealed class MetadataTypeDefinitionResolver : ITypeDefinitionResolver
{
    private readonly ImmutableDictionary<(string Assembly, string FullName),
        TypeDefinitionModel> _types;
    private readonly ImmutableDictionary<string, string> _aliases;
    private readonly ITypeFinder _typeFinder;
    private readonly IMetadataAvailabilityValidator _availability;

    public MetadataTypeDefinitionResolver(
        ImmutableArray<TypeDefinitionModel> types,
        ImmutableDictionary<string, string> aliases,
        ITypeFinder typeFinder,
        IMetadataAvailabilityValidator availability)
    {
        _types = types.ToImmutableDictionary(
            type => (type.Key.Assembly.Name, type.FullName));
        _aliases = aliases;
        _typeFinder = typeFinder;
        _availability = availability;
    }

    public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity)
    {
        _availability.Validate();
        ArgumentNullException.ThrowIfNull(identity);
        if (identity.Shape == CliTypeShape.Primitive)
        {
            return _typeFinder.FindType(PrimitiveTypeName(identity));
        }

        var definitionIdentity = identity.Shape == CliTypeShape.GenericInstantiation
            ? identity.ElementType!
            : identity;
        if (definitionIdentity.Shape != CliTypeShape.Named ||
            definitionIdentity.Assembly is not AssemblyIdentity assembly ||
            definitionIdentity.FullName is not string fullName)
        {
            throw new CompilerException(new CompilerDiagnostic(
                DiagnosticCode.UnsupportedMetadata,
                $"type identity '{identity}' does not name a type definition"));
        }

        var assemblyName = _aliases.TryGetValue(assembly.Name, out var alias)
            ? alias
            : assembly.Name;
        return _types.TryGetValue((assemblyName, fullName), out var definition)
            ? definition
            : throw new CompilerException(new CompilerDiagnostic(
                DiagnosticCode.AssemblyResolution,
                $"type identity '{identity}' was not found"));
    }

    private static string PrimitiveTypeName(CliTypeIdentity identity) =>
        identity.CanonicalName switch
        {
            "primitive:void" => "System.Void",
            "primitive:bool" => "System.Boolean",
            "primitive:char" => "System.Char",
            "primitive:i1" => "System.SByte",
            "primitive:u1" => "System.Byte",
            "primitive:i2" => "System.Int16",
            "primitive:u2" => "System.UInt16",
            "primitive:i4" => "System.Int32",
            "primitive:u4" => "System.UInt32",
            "primitive:i8" => "System.Int64",
            "primitive:u8" => "System.UInt64",
            "primitive:f4" => "System.Single",
            "primitive:f8" => "System.Double",
            "primitive:nativeint" => "System.IntPtr",
            "primitive:nativeuint" => "System.UIntPtr",
            "primitive:string" => "System.String",
            "primitive:object" => "System.Object",
            _ => throw new CompilerException(new CompilerDiagnostic(
                DiagnosticCode.UnsupportedMetadata,
                $"primitive type identity '{identity}' has no type definition")),
        };
}
