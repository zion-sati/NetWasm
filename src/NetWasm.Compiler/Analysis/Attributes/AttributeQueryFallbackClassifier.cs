// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Attributes;

// Strategy: why a reached attribute query reference the compile did not answer keeps its run-time path. Every reference
// warns unless its path is never taken or throws what desktop throws first:
// - code nothing reaches has no argument proofs;
// - a null target throws ArgumentNullException (Attribute's statics, the extensions) or NullReferenceException (a
//   target's own query);
// - Attribute's statics and the extensions check a Type, Assembly or Module target and then the filter: a null filter
//   throws ArgumentNullException and one that is not an attribute type ArgumentException. A member target fails before
//   that check (CustomAttributeExtensions.TypeTarget), and a Type's own query checks nothing.
// A query that dispatches on a receiver the compile cannot name (a target's own query, or Attribute's Assembly and Module
// statics, which ask the target) throws only on the runtime's own reflection objects: an object of a class the program
// creates that answers it (one of its own, or a ParameterInfo) answers itself, and the report says so.
internal sealed class AttributeQueryFallbackClassifier(
    IAttributeQueryResolver resolver,
    IAttributeQueryPlanner planner,
    IRuntimeTypeAttributeQueryAcceptor runtimeTypeQueries,
    ITypeDefinitionResolver definitions,
    ITypeRelationshipClassifier relationships,
    CliTypeIdentity attributeType,
    CliTypeIdentity objectType,
    CliTypeIdentity? assemblyType,
    CliTypeIdentity? moduleType,
    CliTypeIdentity? parameterInfoType) : IAttributeQueryFallbackClassifier
{
    public AttributeQueryFallback? Classify(AttributeQueryReference reference, IEnumerable<CliTypeIdentity> runtimeTypes)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(runtimeTypes);
        var fallback = Fallback(reference, runtimeTypes);
        return fallback is not null && Receiver(reference) is { } receiver
            ? fallback with { AnsweringType = Answering(receiver, runtimeTypes) }
            : fallback;
    }

    private AttributeQueryFallback? Fallback(AttributeQueryReference reference, IEnumerable<CliTypeIdentity> runtimeTypes)
    {
        switch (reference.Instruction.Operation)
        {
            case CilOperation.LoadFunction or CilOperation.LoadVirtualFunction:
                return new(AttributeQueryFallbackKind.Indirect);
            case CilOperation.LoadMethodToken:
                return new(AttributeQueryFallbackKind.MethodHandle);
        }
        if (reference.Call is not { } call)
        {
            return new(AttributeQueryFallbackKind.UnsupportedOverload);
        }
        if (reference.Stack is not { } stack)
        {
            return null;
        }
        var start = stack.Length - call.ArgumentCount;
        var target = stack[start];
        if (target.Kind == AttributeValueKind.Null)
        {
            return null;
        }
        if (resolver.Resolve(call, stack) is { } query)
        {
            // The lowering keeps a query it resolves only when an attribute its broad filter selects cannot be built.
            try
            {
                planner.Plan(query);
            }
            catch (CompilerException exception)
            {
                return new(AttributeQueryFallbackKind.UnbuildableAttribute, exception.Diagnostic.Message);
            }
            return new(AttributeQueryFallbackKind.Unresolved);
        }
        AttributeValueProof? filterProof = call.FilterArgument is { } index ? stack[start + index] : null;
        var filter = call.GenericFilter ??
            (filterProof is { } proof
                ? proof is { Kind: AttributeValueKind.Type, Type: { } exact } ? exact : null
                : call.ImplicitFilter);
        if (call.Api != AttributeQueryApiKind.Instance && ChecksFilter(reference.Method, target) &&
            (filterProof is { Kind: AttributeValueKind.Null } || filter is not null && !IsAttributeFilter(filter)))
        {
            return null;
        }
        switch (target.Kind)
        {
            case AttributeValueKind.RuntimeType:
                return runtimeTypeQueries.Decide(call, stack, runtimeTypes) switch
                {
                    RuntimeTypeAttributeQueryDecision.Unbuildable unbuildable =>
                        new(AttributeQueryFallbackKind.UnbuildableRunTimeAttribute, unbuildable.Reason),
                    RuntimeTypeAttributeQueryDecision.Unresolved when filter is not null && TakesEverything(filter) =>
                        new(AttributeQueryFallbackKind.EveryAttribute),
                    _ => FromFilter(call, filter),
                };
            case AttributeValueKind.Type when target.Type is { } type &&
                AttributeTypeFilter.IsOpenGenericDefinition(type, definitions):
                return new(AttributeQueryFallbackKind.OpenTarget);
            case AttributeValueKind.Type or AttributeValueKind.Assembly or AttributeValueKind.Module:
                return FromFilter(call, filter);
            default:
                var targetType = call.Api == AttributeQueryApiKind.Instance
                    ? reference.Method.DeclaringType
                    : reference.Method.Signature.ParameterSignatureTypes[0];
                return new(targetType.Equals(assemblyType) ? AttributeQueryFallbackKind.RunTimeAssembly
                    : targetType.Equals(moduleType) ? AttributeQueryFallbackKind.RunTimeModule
                    : targetType.FullName == "System.Reflection.ICustomAttributeProvider"
                        ? AttributeQueryFallbackKind.ProviderTarget
                    : AttributeQueryFallbackKind.MemberTarget);
        }
    }

    // The static type of the object the query dispatches on, where the compile cannot name that object: the target of a
    // target's own query, or of Attribute's Assembly and Module statics; null where the path throws whatever the object
    // is (Attribute's member statics and the extensions accept only a Type, whose query throws).
    private CliTypeIdentity? Receiver(AttributeQueryReference reference)
    {
        var method = reference.Method;
        if (reference is { Call: { } call, Stack: { } stack } &&
            stack[stack.Length - call.ArgumentCount].Kind is not (AttributeValueKind.Unknown or AttributeValueKind.RuntimeType))
        {
            return null;
        }
        if (!method.Definition.IsStatic)
        {
            return method.DeclaringType;
        }
        return method.Signature.ParameterSignatureTypes is [var first, ..] &&
            (first.Equals(assemblyType) || first.Equals(moduleType))
            ? first
            : null;
    }

    // A class the program has that the receiver may be and that answers the query itself: one outside the runtime's
    // assembly, or a ParameterInfo (whose queries answer empty). The report names one, a class of the program's own
    // before a ParameterInfo a library defines.
    private CliTypeIdentity? Answering(CliTypeIdentity receiver, IEnumerable<CliTypeIdentity> runtimeTypes) => runtimeTypes
        .Where(type => type.Shape is CliTypeShape.Named or CliTypeShape.GenericInstantiation && !type.ContainsGenericParameters &&
            relationships.Classify(type, receiver).IsAssignmentCompatible &&
            (type.Assembly != attributeType.Assembly || IsParameter(type)))
        .OrderBy(IsParameter)
        .ThenBy(static type => type.CanonicalName, StringComparer.Ordinal)
        .FirstOrDefault();

    private bool IsParameter(CliTypeIdentity type) =>
        parameterInfoType is not null && relationships.Classify(type, parameterInfoType).IsAssignmentCompatible;

    private AttributeQueryFallback FromFilter(AttributeQueryCall call, CliTypeIdentity? filter) =>
        filter is null ? new(AttributeQueryFallbackKind.RunTimeFilter)
        : !IsAttributeFilter(filter) && !(call.Api == AttributeQueryApiKind.Instance && TakesEverything(filter))
            ? new(AttributeQueryFallbackKind.NotAnAttributeFilter)
            : new(AttributeQueryFallbackKind.Unresolved);

    // Attribute's statics and the extensions check the filter once the target passes: a Type, Assembly or Module target
    // (their Assembly and Module overloads check the filter before they ask the target).
    private bool ChecksFilter(MethodInstanceModel method, AttributeValueProof target) =>
        target.Kind is AttributeValueKind.Type or AttributeValueKind.RuntimeType or AttributeValueKind.Assembly or
            AttributeValueKind.Module ||
        method.Signature.ParameterSignatureTypes is [var first, ..] && (first.Equals(assemblyType) || first.Equals(moduleType));

    private bool IsAttributeFilter(CliTypeIdentity filter) =>
        AttributeTypeFilter.IsOpenGenericDefinition(filter, definitions) ||
        relationships.Classify(filter, attributeType).IsAssignmentCompatible;

    private bool TakesEverything(CliTypeIdentity filter) =>
        filter.Equals(objectType) || filter.Equals(attributeType) ||
        filter.Shape is CliTypeShape.Named or CliTypeShape.GenericInstantiation &&
        definitions.ResolveTypeIdentity(filter).IsInterface;
}
