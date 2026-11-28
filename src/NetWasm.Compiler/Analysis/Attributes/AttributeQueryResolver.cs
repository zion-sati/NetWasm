// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Attributes;

// Strategy: require one closed target and one closed attribute filter per call, or an open generic attribute type
// (typeof(TagAttribute<>)), which matches its constructed instances. Only the inheritance Boolean can remain a runtime
// choice. A query naming no filter takes its family's (object or Attribute); a target's own query also takes object and
// an interface attributes implement, which Attribute's statics and the extensions reject with ArgumentException, as
// their bodies do.
internal sealed class AttributeQueryResolver(
    ITypeDefinitionResolver definitions,
    ITypeRelationshipClassifier relationships,
    CliTypeIdentity attributeType,
    CliTypeIdentity objectType) : IAttributeQueryResolver
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
        var module = stack[start] is { Kind: AttributeValueKind.Module, Assembly: { } moduleOwner } ? moduleOwner : (AssemblyIdentity?)null;
        var target = assembly is null && module is null ? ExactType(stack[start]) : null;
        var filter = call.GenericFilter ??
            (call.FilterArgument is { } filterArgument ? ExactType(stack[start + filterArgument]) : call.ImplicitFilter);
        if (assembly is null && module is null && target is null || filter is null ||
            target is not null && !IsClosedDeclaration(target))
        {
            return null;
        }
        var open = AttributeTypeFilter.IsOpenGenericDefinition(filter, definitions);
        if (!open && !IsClosedDeclaration(filter) ||
            !open && !relationships.Classify(filter, attributeType).IsAssignmentCompatible &&
            !(call.Api == AttributeQueryApiKind.Instance &&
                (filter.Equals(objectType) || definitions.ResolveTypeIdentity(filter).IsInterface)))
        {
            return null;
        }
        // A filter other than a sealed type may match attribute types the program never names; one of those that cannot
        // be constructed at compile time leaves the query to its fallback rather than failing the build.
        var broad = open || !definitions.ResolveTypeIdentity(filter).IsSealed;
        // An open filter's matches are of different constructed types, so they come back as Attribute[].
        var element = open ? attributeType : filter;
        var inherit = call.InheritArgument is { } argument
            ? stack[start + argument] is { Kind: AttributeValueKind.Int32 } proof
                ? proof.Integer != 0
                : (bool?)null
            : true;
        if (module is { } targetModule)
        {
            // A module's attributes are read all at once and filtered, as an assembly's are for a broad filter.
            return new(call, null, filter, inherit) { TargetModule = targetModule, ResultElementType = element, Broad = broad };
        }
        if (assembly is { } targetAssembly)
        {
            // An assembly's attributes of a sealed, top-level attribute type are found by its name; any other filter reads
            // them all (IAssemblyAttributeDescriptorReader) and keeps those it takes.
            var declaration = definitions.ResolveTypeIdentity(filter);
            return new(call, null, filter, inherit)
            {
                TargetAssembly = targetAssembly,
                ResultElementType = element,
                Broad = broad,
                AttributeName = !open && filter.Shape == CliTypeShape.Named && declaration.IsSealed &&
                    declaration.DeclaringType is null ? filter.FullName : null,
            };
        }
        return new(call, target, filter, inherit) { ResultElementType = element, Broad = broad };
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
