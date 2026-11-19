// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Attributes;

// Strategy: require one closed target and one closed attribute filter per call, or an open generic attribute type
// (typeof(TagAttribute<>)), which matches its constructed instances. Only the inheritance Boolean can remain a runtime
// choice.
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
        var assembly = stack[start] is { Kind: AttributeValueKind.Assembly, Assembly: { } owner } ? owner : (AssemblyIdentity?)null;
        var target = assembly is null ? ExactType(stack[start]) : null;
        var filter = call.GenericFilter ?? ExactType(stack[start + call.FilterArgument!.Value]);
        if (assembly is null && target is null || filter is null || target is not null && !IsClosedDeclaration(target))
        {
            return null;
        }
        var open = AttributeTypeFilter.IsOpenGenericDefinition(filter, definitions);
        if (!open && !IsClosedDeclaration(filter) || synthesizedAttributes.Contains(filter) || filter.Equals(attributeType) ||
            !relationships.Classify(filter, attributeType).IsAssignmentCompatible)
        {
            return null;
        }
        // An open filter's matches are of different constructed types, so they come back as Attribute[].
        var element = open ? attributeType : filter;
        var inherit = call.InheritArgument is { } argument
            ? stack[start + argument] is { Kind: AttributeValueKind.Int32 } proof
                ? proof.Integer != 0
                : (bool?)null
            : true;
        if (assembly is { } targetAssembly)
        {
            // An assembly's attributes are found by the attribute type's name (IAssemblyAttributeDescriptorReader), which
            // finds every one of a sealed, top-level attribute type; any other filter keeps the runtime query.
            var declaration = definitions.ResolveTypeIdentity(filter);
            return filter.Shape == CliTypeShape.Named && declaration.IsSealed && declaration.DeclaringType is null
                ? new(call, null, filter, inherit) { TargetAssembly = targetAssembly, ResultElementType = element }
                : null;
        }
        return new(call, target, filter, inherit) { ResultElementType = element };
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
