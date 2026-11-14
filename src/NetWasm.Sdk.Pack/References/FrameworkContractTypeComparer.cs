using System.Collections.Frozen;
using Mono.Cecil;

namespace NetWasm.Sdk.Pack.References;

/// <summary>Compares contract signatures while preserving unrelated assembly identities.</summary>
public sealed class FrameworkContractTypeComparer : IFrameworkContractTypeComparer
{
    private static readonly FrozenDictionary<string, string> frameworkTokens =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["netstandard"] = "cc7b13ffcd2ddd51",
            ["mscorlib"] = "b77a5c561934e089",
            ["System.Runtime"] = "b03f5f7f11d50a3a",
            ["System.Private.CoreLib"] = "7cec85d7bea7798e"
        }.ToFrozenDictionary(StringComparer.Ordinal);

    public bool Compare(TypeReference? source, TypeReference? target, FrameworkContractScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (source is null || target is null) return source is null && target is null;
        if (source is GenericParameter || target is GenericParameter)
            return source is GenericParameter left && target is GenericParameter right &&
                left.Type == right.Type && left.Position == right.Position;
        if (source is GenericInstanceType || target is GenericInstanceType)
            return source is GenericInstanceType left && target is GenericInstanceType right &&
                Compare(left.ElementType, right.ElementType, scope) &&
                left.GenericArguments.Count == right.GenericArguments.Count &&
                left.GenericArguments.Zip(right.GenericArguments).All(pair => Compare(pair.First, pair.Second, scope));
        if (source is ArrayType || target is ArrayType)
            return source is ArrayType left && target is ArrayType right &&
                left.IsVector == right.IsVector && left.Rank == right.Rank &&
                left.Dimensions.Zip(right.Dimensions).All(pair =>
                    pair.First.LowerBound == pair.Second.LowerBound && pair.First.UpperBound == pair.Second.UpperBound) &&
                Compare(left.ElementType, right.ElementType, scope);
        if (source is FunctionPointerType || target is FunctionPointerType)
            return source is FunctionPointerType left && target is FunctionPointerType right &&
                left.HasThis == right.HasThis && left.ExplicitThis == right.ExplicitThis &&
                left.CallingConvention == right.CallingConvention &&
                Compare(left.ReturnType, right.ReturnType, scope) && left.Parameters.Count == right.Parameters.Count &&
                left.Parameters.Zip(right.Parameters).All(pair => Compare(pair.First.ParameterType, pair.Second.ParameterType, scope));
        if (source is TypeSpecification || target is TypeSpecification)
        {
            if (source is not TypeSpecification left || target is not TypeSpecification right || source.GetType() != target.GetType())
                return false;
            if (source is RequiredModifierType requiredLeft && target is RequiredModifierType requiredRight &&
                !Compare(requiredLeft.ModifierType, requiredRight.ModifierType, scope)) return false;
            if (source is OptionalModifierType optionalLeft && target is OptionalModifierType optionalRight &&
                !Compare(optionalLeft.ModifierType, optionalRight.ModifierType, scope)) return false;
            return Compare(left.ElementType, right.ElementType, scope);
        }
        return source.FullName == target.FullName && source.IsValueType == target.IsValueType &&
            Owner(source, scope) == Owner(target, scope);
    }

    private static string Owner(TypeReference type, FrameworkContractScope scope)
    {
        var identity = type.Scope switch
        {
            AssemblyNameReference assembly => assembly,
            ModuleDefinition module => module.Assembly.Name,
            ModuleReference => type.Module.Assembly.Name,
            _ => null
        };
        if (identity is null) return type.Scope?.ToString() ?? "";
        if (identity.FullName == scope.Source.FullName)
            return scope.ForwardedNames.Contains(type.FullName) ? scope.Platform.FullName : identity.FullName;
        if (scope.PlatformTypeNames.Contains(type.FullName) && string.IsNullOrEmpty(identity.Culture) &&
            (frameworkTokens.TryGetValue(identity.Name, out var token) ||
                FrameworkAssemblyIdentities.ReferencePackages.TryGetValue(identity.Name, out token)) &&
            string.Equals(Convert.ToHexString(identity.PublicKeyToken), token, StringComparison.OrdinalIgnoreCase))
            return scope.Platform.FullName;
        return identity.FullName;
    }
}
