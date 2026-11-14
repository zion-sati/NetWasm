using System.Reflection.Metadata;

namespace NetWasm.Compiler.Metadata;

/// <summary>Formats a type definition's name as ILLink.Substitutions.xml names it.</summary>
internal interface ITypeDefinitionNameFormatter
{
    string Format(MetadataReader reader, TypeDefinitionHandle type);
}
