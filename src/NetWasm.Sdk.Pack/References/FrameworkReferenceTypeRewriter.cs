using Mono.Cecil;

namespace NetWasm.Sdk.Pack.References;

/// <summary>Rebinds declaration signatures without evaluating or rewriting method bodies.</summary>
public sealed class FrameworkReferenceTypeRewriter : IFrameworkReferenceTypeRewriter
{
    public TypeReference Rewrite(TypeReference type, FrameworkReferenceMap map)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(map);
        if (type is GenericParameter generic)
            return map.GenericParameters?.TryGetValue(generic, out var replacement) == true ? replacement : generic;
        if (type is GenericInstanceType instance)
        {
            var result = new GenericInstanceType(Rewrite(instance.ElementType, map));
            foreach (var argument in instance.GenericArguments) result.GenericArguments.Add(Rewrite(argument, map));
            return result;
        }
        if (type is ArrayType array)
        {
            var result = new ArrayType(Rewrite(array.ElementType, map), array.Rank);
            for (var index = 0; index < array.Dimensions.Count; index++) result.Dimensions[index] = array.Dimensions[index];
            return result;
        }
        if (type is ByReferenceType byref) return new ByReferenceType(Rewrite(byref.ElementType, map));
        if (type is PointerType pointer) return new PointerType(Rewrite(pointer.ElementType, map));
        if (type is RequiredModifierType required) return new RequiredModifierType(Rewrite(required.ModifierType, map), Rewrite(required.ElementType, map));
        if (type is OptionalModifierType optional) return new OptionalModifierType(Rewrite(optional.ModifierType, map), Rewrite(optional.ElementType, map));
        if (type is PinnedType pinned) return new PinnedType(Rewrite(pinned.ElementType, map));
        if (type is SentinelType sentinel) return new SentinelType(Rewrite(sentinel.ElementType, map));
        if (type is FunctionPointerType function)
        {
            var result = new FunctionPointerType
            {
                HasThis = function.HasThis,
                ExplicitThis = function.ExplicitThis,
                CallingConvention = function.CallingConvention,
                ReturnType = Rewrite(function.ReturnType, map)
            };
            foreach (var parameter in function.Parameters)
                result.Parameters.Add(new ParameterDefinition(Rewrite(parameter.ParameterType, map)));
            return result;
        }
        var owner = type.Scope switch
        {
            AssemblyNameReference assembly => assembly.FullName,
            ModuleDefinition definition => definition.Assembly.Name.FullName,
            _ => null
        };
        return owner == map.Source.Assembly.Name.FullName && map.ForwardedTypes.TryGetValue(type.FullName, out var target)
            ? target : type;
    }

    public MethodReference Rewrite(MethodReference method, FrameworkReferenceMap map)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(map);
        if (method is GenericInstanceMethod instance)
        {
            var closed = new GenericInstanceMethod(Rewrite(instance.ElementMethod, map));
            foreach (var argument in instance.GenericArguments)
                closed.GenericArguments.Add(Rewrite(argument, map));
            return closed;
        }
        var result = new MethodReference(method.Name, method.ReturnType, Rewrite(method.DeclaringType, map))
        {
            HasThis = method.HasThis,
            ExplicitThis = method.ExplicitThis,
            CallingConvention = method.CallingConvention
        };
        var parameters = map.GenericParameters is null
            ? new Dictionary<GenericParameter, TypeReference>()
            : new Dictionary<GenericParameter, TypeReference>(map.GenericParameters);
        foreach (var parameter in method.GenericParameters)
        {
            var replacement = new GenericParameter(parameter.Name, result) { Attributes = parameter.Attributes };
            result.GenericParameters.Add(replacement);
            parameters[parameter] = replacement;
        }
        var methodMap = map with { GenericParameters = parameters };
        result.ReturnType = Rewrite(method.ReturnType, methodMap);
        foreach (var parameter in method.Parameters)
            result.Parameters.Add(new ParameterDefinition(parameter.Name, parameter.Attributes,
                Rewrite(parameter.ParameterType, methodMap)));
        return result;
    }

}
