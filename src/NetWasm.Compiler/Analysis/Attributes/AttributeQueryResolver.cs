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
    public ResolvedAttributeQuery Resolve(AttributeQueryCall call, ImmutableArray<AttributeValueProof> stack)
    {
        ArgumentNullException.ThrowIfNull(call);
        if (stack.IsDefault || stack.Length < call.ArgumentCount)
        {
            throw new ArgumentException("Query arguments must have validated stack entries.", nameof(stack));
        }
        var start = stack.Length - call.ArgumentCount;
        var target = ExactType(stack[start], "target");
        var filter = call.GenericFilter ?? ExactType(stack[start + call.FilterArgument!.Value], "attribute filter");
        RequireDeclaration(target, "target");
        RequireDeclaration(filter, "attribute filter");
        if (synthesizedAttributes.Contains(filter))
        {
            throw Unsupported("Queries for attributes synthesized from metadata flags are not supported.");
        }
        if (filter.Equals(attributeType) || !relationships.Classify(filter, attributeType).IsAssignmentCompatible)
        {
            throw Unsupported("The attribute filter must be a closed attribute class other than System.Attribute.");
        }
        var inherit = call.InheritArgument is { } argument
            ? stack[start + argument] is { Kind: AttributeValueKind.Int32 } proof
                ? proof.Integer != 0
                : (bool?)null
            : true;
        return new(call, target, filter, inherit);
    }

    private static CliTypeIdentity ExactType(AttributeValueProof proof, string role) =>
        proof is { Kind: AttributeValueKind.Type, Type: { } type }
            ? type
            : throw Unsupported($"Custom attribute query {role} must resolve to one non-null type; use typeof(T) in a closed specialization.");

    private void RequireDeclaration(CliTypeIdentity type, string role)
    {
        if (type.ContainsGenericParameters || type.Shape is not
            (CliTypeShape.Named or CliTypeShape.Primitive or CliTypeShape.GenericInstantiation))
        {
            throw Unsupported($"Custom attribute query {role} must name a closed type declaration.");
        }
        var definition = definitions.ResolveTypeIdentity(type);
        if (definition.GenericArity != type.TypeArguments.Length)
        {
            throw Unsupported($"Custom attribute query {role} must name a closed type declaration.");
        }
    }

    private static CompilerException Unsupported(string message) =>
        new(new CompilerDiagnostic(DiagnosticCode.UnsupportedMetadata, message));
}
