using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Attributes;

// Strategy: require one closed target and one closed attribute filter per call.
// Only the inheritance Boolean can remain a runtime choice.
internal sealed class AttributeQueryResolver(
    ITypeDefinitionResolver definitions,
    ITypeRelationshipClassifier relationships,
    CliTypeIdentity attributeType,
    ImmutableHashSet<CliTypeIdentity> synthesizedAttributes) : IAttributeQueryResolver
{
    public ResolvedAttributeQuery? Resolve(AttributeQueryCall call, ImmutableArray<AttributeValueProof> stack)
    {
        ArgumentNullException.ThrowIfNull(call);
        if (stack.IsDefault || stack.Length < call.ArgumentCount)
        {
            throw new ArgumentException("Query arguments must have validated stack entries.", nameof(stack));
        }
        var start = stack.Length - call.ArgumentCount;
        var target = ExactType(stack[start]);
        var filter = call.GenericFilter ?? ExactType(stack[start + call.FilterArgument!.Value]);
        if (target is null || filter is null || !IsClosedDeclaration(target) || !IsClosedDeclaration(filter) ||
            synthesizedAttributes.Contains(filter) || filter.Equals(attributeType) ||
            !relationships.Classify(filter, attributeType).IsAssignmentCompatible)
        {
            return null;
        }
        var inherit = call.InheritArgument is { } argument
            ? stack[start + argument] is { Kind: AttributeValueKind.Int32 } proof
                ? proof.Integer != 0
                : (bool?)null
            : true;
        return new(call, target, filter, inherit);
    }

    private static CliTypeIdentity? ExactType(AttributeValueProof proof) =>
        proof is { Kind: AttributeValueKind.Type, Type: { } type } ? type : null;

    private bool IsClosedDeclaration(CliTypeIdentity type)
    {
        if (type.ContainsGenericParameters || type.Shape is not
            (CliTypeShape.Named or CliTypeShape.Primitive or CliTypeShape.GenericInstantiation))
        {
            return false;
        }
        var definition = definitions.ResolveTypeIdentity(type);
        return definition.GenericArity == type.TypeArguments.Length;
    }
}
