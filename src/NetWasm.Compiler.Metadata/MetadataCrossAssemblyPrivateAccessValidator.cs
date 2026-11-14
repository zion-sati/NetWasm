using System;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

// Strategy: reject the access violation independent of containing-type or friend rules.
internal sealed class MetadataCrossAssemblyPrivateAccessValidator(IMetadataAssemblyResolver assemblies)
    : IMetadataCrossAssemblyPrivateAccessValidator
{
    private readonly IMetadataAssemblyResolver _assemblies = assemblies ??
        throw new ArgumentNullException(nameof(assemblies));

    public void Validate(MetadataMemberAccessRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.CallerAssembly == request.Member.Assembly)
            return;
        var source = _assemblies.Resolve(request.Member.Assembly);
        var handle = MetadataTokens.EntityHandle(request.Member.MetadataToken);
        var inaccessible = handle.Kind switch
        {
            HandleKind.MethodDefinition => (source.Reader.GetMethodDefinition((MethodDefinitionHandle)handle).Attributes
                & MethodAttributes.MemberAccessMask) is MethodAttributes.Private or MethodAttributes.PrivateScope,
            HandleKind.FieldDefinition => (source.Reader.GetFieldDefinition((FieldDefinitionHandle)handle).Attributes
                & FieldAttributes.FieldAccessMask) is FieldAttributes.Private or FieldAttributes.PrivateScope,
            _ => throw new ArgumentException("A method or field definition is required.", nameof(request)),
        };
        if (inaccessible)
            throw new CompilerException(new CompilerDiagnostic(DiagnosticCode.AssemblyResolution,
                "A referenced member is private to a different assembly.", request.Caller, request.IlOffset));
    }
}
