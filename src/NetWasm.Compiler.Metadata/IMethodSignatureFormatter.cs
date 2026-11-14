using System.Reflection.Metadata;

namespace NetWasm.Compiler.Metadata;

/// <summary>Formats a method's signature as ILLink.Substitutions.xml names it.</summary>
internal interface IMethodSignatureFormatter
{
    string Format(MetadataReader reader, MethodDefinition method);
}
