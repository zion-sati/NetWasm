using Mono.Cecil;

namespace NetWasm.Sdk.Pack.References;

/// <summary>Traverses supplied platform definitions without ambient assembly resolution.</summary>
public sealed class FrameworkTypeHierarchyReader(
    IFrameworkReferenceTypeRewriter rewrite,
    IFrameworkContractTypeComparer contracts) : IFrameworkTypeHierarchyReader
{
    public FrameworkTypeHierarchy Read(FrameworkTypeHierarchyRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var bases = new List<TypeReference>();
        var interfaces = new List<TypeReference>();
        var ancestry = new HashSet<TypeDefinition>();
        var forwards = new Dictionary<string, TypeReference>(StringComparer.Ordinal);
        Walk(request.Root, request.Root);
        return new(bases, interfaces);

        void Walk(TypeDefinition definition, TypeReference instance)
        {
            if (!ancestry.Add(definition))
                throw new ArgumentException("Framework inheritance contains a cycle.");
            var arguments = new Dictionary<GenericParameter, TypeReference>();
            if (instance is GenericInstanceType generic)
            {
                if (generic.GenericArguments.Count != definition.GenericParameters.Count)
                    throw new ArgumentException("Framework inheritance has incompatible generic arity.");
                for (var index = 0; index < definition.GenericParameters.Count; index++)
                    arguments.Add(definition.GenericParameters[index], generic.GenericArguments[index]);
            }
            var map = new FrameworkReferenceMap(definition.Module, forwards, arguments);
            if (definition.BaseType is { } baseType) Follow(rewrite.Rewrite(baseType, map), false);
            foreach (var implementation in definition.Interfaces)
                Follow(rewrite.Rewrite(implementation.InterfaceType, map), true);
            ancestry.Remove(definition);
        }

        void Follow(TypeReference reference, bool isInterface)
        {
            (isInterface ? interfaces : bases).Add(reference);
            var element = reference.GetElementType();
            if (request.PlatformTypes.TryGetValue(element.FullName, out var definition) &&
                contracts.Compare(element, definition, request.Scope))
                Walk(definition, reference);
        }
    }
}
