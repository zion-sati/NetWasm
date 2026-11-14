using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

/// <summary>Checks an actually reached redirected method against its original contract.</summary>
internal sealed class FrameworkMethodContractValidator(IMetadataAssemblyResolver assemblies)
    : IFrameworkMethodContractValidator
{
    private readonly IMetadataAssemblyResolver _assemblies = assemblies ??
        throw new ArgumentNullException(nameof(assemblies));

    public void Validate(FrameworkMethodBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        var source = binding.OriginalSource ?? _assemblies.Resolve(binding.Original.Key.Assembly);
        var target = _assemblies.Resolve(binding.Target.Key.Assembly);
        var original = source.Reader.GetMethodDefinition(
            (MethodDefinitionHandle)MetadataTokens.EntityHandle(binding.Original.Key.MetadataToken));
        var replacement = target.Reader.GetMethodDefinition(
            (MethodDefinitionHandle)MetadataTokens.EntityHandle(binding.Target.Key.MetadataToken));
        if (!PreservesAccess(original.Attributes, replacement.Attributes, source.Identity == target.Identity))
            throw Incompatible(binding, "the method does not preserve a public or protected contract");
        var dispatch = MethodAttributes.Static | MethodAttributes.Virtual;
        if ((original.Attributes & dispatch) != (replacement.Attributes & dispatch) ||
            (replacement.Attributes & (MethodAttributes.Abstract | MethodAttributes.Final) & ~original.Attributes) != 0)
            throw Incompatible(binding, "the method changes static or virtual dispatch requirements");

        var sourceTypes = Provider(source);
        var targetTypes = Provider(target);
        var from = original.DecodeSignature(sourceTypes, null);
        var to = replacement.DecodeSignature(targetTypes, null);
        if (from.Header.RawValue != to.Header.RawValue ||
            from.GenericParameterCount != to.GenericParameterCount ||
            from.RequiredParameterCount != to.RequiredParameterCount ||
            !from.ReturnType.Equals(to.ReturnType) ||
            !from.ParameterTypes.SequenceEqual(to.ParameterTypes))
            throw Incompatible(binding, "the method signature or custom modifiers differ");

        var sourceParameters = original.GetGenericParameters().ToArray();
        var targetParameters = replacement.GetGenericParameters().ToArray();
        if (sourceParameters.Length != targetParameters.Length)
            throw Incompatible(binding, "the generic parameter counts differ");
        for (var index = 0; index < sourceParameters.Length; index++)
        {
            var sourceParameter = source.Reader.GetGenericParameter(sourceParameters[index]);
            var targetParameter = target.Reader.GetGenericParameter(targetParameters[index]);
            const GenericParameterAttributes allowsByRefLike = (GenericParameterAttributes)0x20;
            if ((targetParameter.Attributes & GenericParameterAttributes.SpecialConstraintMask & ~sourceParameter.Attributes) != 0 ||
                (sourceParameter.Attributes & allowsByRefLike) != 0 && (targetParameter.Attributes & allowsByRefLike) == 0)
                throw Incompatible(binding, "the destination adds a generic parameter requirement");
            var sourceConstraints = sourceParameter.GetConstraints()
                .Select(handle => DecodeConstraint(source, sourceTypes, handle)).ToArray();
            if (targetParameter.GetConstraints().Any(handle =>
                !sourceConstraints.Contains(DecodeConstraint(target, targetTypes, handle))))
                throw Incompatible(binding, "the destination adds a generic type constraint");
        }
    }

    private static bool PreservesAccess(MethodAttributes source, MethodAttributes target, bool sameAssembly)
    {
        var destination = target & MethodAttributes.MemberAccessMask;
        // An unchanged private/internal member remains a valid contract within
        // the same assembly identity. Cross-assembly accessibility is checked
        // separately when the caller binds the selected implementation.
        if (sameAssembly && (source & MethodAttributes.MemberAccessMask) == destination)
            return true;
        return (source & MethodAttributes.MemberAccessMask) switch
        {
            MethodAttributes.Public => destination == MethodAttributes.Public,
            MethodAttributes.Family => destination is MethodAttributes.Public or MethodAttributes.Family or MethodAttributes.FamORAssem,
            MethodAttributes.FamORAssem => destination is MethodAttributes.Public or MethodAttributes.FamORAssem,
            _ => false
        };
    }

    private static SignatureTypeProvider Provider(MetadataAssemblySnapshot source) =>
        new(source.Identity, source.Reader, source.AssemblyIdentityAliases, preserveCustomModifiers: true);

    private static CliTypeIdentity DecodeConstraint(MetadataAssemblySnapshot source,
        SignatureTypeProvider provider, GenericParameterConstraintHandle handle)
    {
        var type = source.Reader.GetGenericParameterConstraint(handle).Type;
        return type.Kind switch
        {
            HandleKind.TypeDefinition => provider.GetTypeFromDefinition(source.Reader, (TypeDefinitionHandle)type, 0x12),
            HandleKind.TypeReference => provider.GetTypeFromReference(source.Reader, (TypeReferenceHandle)type, 0x12),
            // A constraint's TypeDefOrRef coded index has only these three kinds.
            _ => provider.GetTypeFromSpecification(source.Reader, null, (TypeSpecificationHandle)type, 0x12),
        };
    }

    private static CompilerException Incompatible(FrameworkMethodBinding binding, string reason) => new(
        new CompilerDiagnostic(DiagnosticCode.AssemblyResolution,
            $"Framework method '{binding.Original.Key.Assembly.Name}::{binding.Original.Name}' cannot bind to " +
            $"'{binding.Target.Key.Assembly.Name}::{binding.Target.Name}': {reason}.",
            binding.Caller, binding.IlOffset));
}
