using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal sealed class MetadataMethodFinder(
    ImmutableArray<TypeDefinitionModel> types,
    IMethodRepository methods,
    IMetadataAvailabilityValidator availability) : IMethodFinder
{
    public MethodDefinitionModel FindMethod(
        AssemblyIdentity assembly,
        string typeFullName,
        string methodName)
    {
        availability.Validate();
        var type = types.SingleOrDefault(candidate =>
                       candidate.Key.Assembly == assembly &&
                       candidate.FullName == typeFullName)
                   ?? throw new CompilerException(
                       new CompilerDiagnostic(
                           DiagnosticCode.InvalidEntryPoint,
                           $"type '{typeFullName}' was not found in '{assembly.Name}'"));
        var matches = type.Methods
            .Select(methods.GetMethod)
            .Where(method => method.Name == methodName)
            .ToArray();
        return matches.Length == 1
            ? matches[0]
            : throw new CompilerException(
                new CompilerDiagnostic(
                    DiagnosticCode.InvalidEntryPoint,
                    $"method '{typeFullName}::{methodName}' was not found uniquely"));
    }

    public MethodDefinitionModel FindMethod(
        AssemblyIdentity assembly,
        int metadataToken)
    {
        availability.Validate();
        var key = new EntityKey(assembly, metadataToken);
        var declaringType = types.SingleOrDefault(candidate =>
            candidate.Key.Assembly == assembly && candidate.Methods.Contains(key));
        return declaringType is not null
            ? methods.GetMethod(key)
            : throw new CompilerException(
                new CompilerDiagnostic(
                    DiagnosticCode.InvalidEntryPoint,
                    $"entry-point method token '0x{metadataToken:x8}' was not found in '{assembly.Name}'"));
    }
}
