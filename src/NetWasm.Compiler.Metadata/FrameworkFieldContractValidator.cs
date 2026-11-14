using System;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

/// <summary>Checks a reached field redirection without assuming portable private storage.</summary>
internal sealed class FrameworkFieldContractValidator(IMetadataAssemblyResolver assemblies)
    : IFrameworkFieldContractValidator
{
    private readonly IMetadataAssemblyResolver _assemblies = assemblies ??
        throw new ArgumentNullException(nameof(assemblies));

    public void Validate(FrameworkFieldBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        var source = binding.OriginalSource ?? _assemblies.Resolve(binding.Original.Key.Assembly);
        var target = _assemblies.Resolve(binding.Target.Key.Assembly);
        var original = source.Reader.GetFieldDefinition(
            (FieldDefinitionHandle)MetadataTokens.EntityHandle(binding.Original.Key.MetadataToken));
        var replacement = target.Reader.GetFieldDefinition(
            (FieldDefinitionHandle)MetadataTokens.EntityHandle(binding.Target.Key.MetadataToken));
        if (!PreservesAccess(original.Attributes, replacement.Attributes, source.Identity == target.Identity))
            throw Incompatible(binding, "the field does not preserve a public or protected contract");
        var storage = FieldAttributes.Static | FieldAttributes.InitOnly | FieldAttributes.Literal;
        if ((original.Attributes & storage) != (replacement.Attributes & storage))
            throw Incompatible(binding, "the field changes static storage, mutability or literal semantics");
        var sourceType = original.DecodeSignature(new SignatureTypeProvider(source.Identity,
            source.Reader, source.AssemblyIdentityAliases, preserveCustomModifiers: true), null);
        var targetType = replacement.DecodeSignature(new SignatureTypeProvider(target.Identity,
            target.Reader, target.AssemblyIdentityAliases, preserveCustomModifiers: true), null);
        if (!sourceType.Equals(targetType))
            throw Incompatible(binding, "the field type or custom modifiers differ");
        if ((original.Attributes & FieldAttributes.Literal) == 0)
            return;
        var originalConstant = original.GetDefaultValue();
        var targetConstant = replacement.GetDefaultValue();
        if (originalConstant.IsNil || targetConstant.IsNil)
            throw Incompatible(binding, "a literal value is missing");
        var from = source.Reader.GetConstant(originalConstant);
        var to = target.Reader.GetConstant(targetConstant);
        if (from.TypeCode != to.TypeCode ||
            !source.Reader.GetBlobBytes(from.Value).AsSpan().SequenceEqual(target.Reader.GetBlobBytes(to.Value)))
            throw Incompatible(binding, "the literal value differs");
    }

    private static bool PreservesAccess(FieldAttributes source, FieldAttributes target, bool sameAssembly)
    {
        var destination = target & FieldAttributes.FieldAccessMask;
        // An unchanged private/internal member remains a valid contract within
        // the same assembly identity. Cross-assembly accessibility is checked
        // separately when the caller binds the selected implementation.
        if (sameAssembly && (source & FieldAttributes.FieldAccessMask) == destination)
            return true;
        return (source & FieldAttributes.FieldAccessMask) switch
        {
            FieldAttributes.Public => destination == FieldAttributes.Public,
            FieldAttributes.Family => destination is FieldAttributes.Public or FieldAttributes.Family or FieldAttributes.FamORAssem,
            FieldAttributes.FamORAssem => destination is FieldAttributes.Public or FieldAttributes.FamORAssem,
            _ => false
        };
    }

    private static CompilerException Incompatible(FrameworkFieldBinding binding, string reason) => new(
        new CompilerDiagnostic(DiagnosticCode.AssemblyResolution,
            $"Framework field '{binding.Original.Key.Assembly.Name}::{binding.Original.Name}' cannot bind to " +
            $"'{binding.Target.Key.Assembly.Name}::{binding.Target.Name}': {reason}.",
            binding.Caller, binding.IlOffset));
}
