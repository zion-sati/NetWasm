using Mono.Cecil;

namespace NetWasm.Sdk.Pack.References;

/// <summary>Checks the type-level promises required by a framework forwarder.</summary>
public sealed class FrameworkTypeContractChecker(IFrameworkContractTypeComparer contracts,
    IFrameworkTypeHierarchyReader hierarchy) : IFrameworkTypeContractChecker
{
    public FrameworkTypeMismatch Check(FrameworkTypeContract request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var source = request.Source;
        var target = request.Target;
        var scope = request.Scope;
        if (!IsVisible(source) || !IsVisible(target) || source.IsValueType != target.IsValueType ||
            source.IsEnum != target.IsEnum || source.IsInterface != target.IsInterface ||
            !source.IsAbstract && target.IsAbstract ||
            source.GenericParameters.Count != target.GenericParameters.Count)
            return FrameworkTypeMismatch.Declaration;
        const string refLikeAttribute = "System.Runtime.CompilerServices.IsByRefLikeAttribute";
        if (source.CustomAttributes.Any(attribute => attribute.AttributeType.FullName == refLikeAttribute) !=
            target.CustomAttributes.Any(attribute => attribute.AttributeType.FullName == refLikeAttribute))
            return FrameworkTypeMismatch.RefLike;
        for (var index = 0; index < source.GenericParameters.Count; index++)
        {
            var original = source.GenericParameters[index];
            var replacement = target.GenericParameters[index];
            var variance = GenericParameterAttributes.VarianceMask;
            var constraints = GenericParameterAttributes.SpecialConstraintMask;
            const GenericParameterAttributes allowsByRefLike = (GenericParameterAttributes)0x20;
            var originalVariance = original.Attributes & variance;
            var replacementVariance = replacement.Attributes & variance;
            // A runtime may add conversions to an invariant reference contract,
            // as modern EventHandler<T> does. It must retain promised conversions.
            if (!ValidVariance(source, originalVariance) || !ValidVariance(target, replacementVariance) ||
                originalVariance != GenericParameterAttributes.NonVariant && originalVariance != replacementVariance ||
                (replacement.Attributes & constraints & ~original.Attributes) != 0 ||
                (original.Attributes & allowsByRefLike) != 0 && (replacement.Attributes & allowsByRefLike) == 0 ||
                replacement.Constraints.Any(constraint => !original.Constraints.Any(candidate =>
                    contracts.Compare(candidate.ConstraintType, constraint.ConstraintType, scope))))
                return FrameworkTypeMismatch.GenericConstraints;
        }
        if (source.IsEnum && source.Fields.Single(field => field.Name == "value__").FieldType.FullName !=
            target.Fields.Single(field => field.Name == "value__").FieldType.FullName)
            return FrameworkTypeMismatch.EnumStorage;
        if (source.IsEnum && source.Fields.Where(field => field.IsPublic && field.IsLiteral).Any(original =>
            !target.Fields.Any(replacement => replacement.IsPublic && replacement.IsLiteral &&
                replacement.Name == original.Name && Equals(replacement.Constant, original.Constant))))
            return FrameworkTypeMismatch.EnumValues;
        var inherited = hierarchy.Read(new(target, request.PlatformTypes, scope));
        if (source.BaseType is { } baseType &&
            !inherited.BaseTypes.Any(candidate => contracts.Compare(baseType, candidate, scope)) ||
            source.Interfaces.Any(implementation => !inherited.Interfaces.Any(candidate =>
                contracts.Compare(implementation.InterfaceType, candidate, scope))))
            return FrameworkTypeMismatch.Inheritance;
        return FrameworkTypeMismatch.None;
    }

    private static bool ValidVariance(TypeDefinition type, GenericParameterAttributes variance) =>
        variance == GenericParameterAttributes.NonVariant ||
        variance is GenericParameterAttributes.Covariant or GenericParameterAttributes.Contravariant &&
        (type.IsInterface || type.BaseType?.FullName == "System.MulticastDelegate");

    private static bool IsVisible(TypeDefinition type) => type.IsPublic ||
        (type.IsNestedPublic || type.IsNestedFamily || type.IsNestedFamilyOrAssembly) &&
        type.DeclaringType is { } parent && IsVisible(parent);
}
