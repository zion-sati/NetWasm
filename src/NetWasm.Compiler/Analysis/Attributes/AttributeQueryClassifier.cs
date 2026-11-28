// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Attributes;

// Strategy: classify the bound public API, before its shared implementation
// loses the caller's target and filter provenance.
internal sealed class AttributeQueryClassifier(
    ImmutableDictionary<EntityKey, AttributeQueryApiKind> declaringTypes,
    CliTypeIdentity memberInfoType,
    CliTypeIdentity typeType,
    CliTypeIdentity? assemblyType = null,
    CliTypeIdentity? objectType = null,
    CliTypeIdentity? attributeType = null,
    CliTypeIdentity? moduleType = null) : IAttributeQueryClassifier
{
    private static readonly ImmutableDictionary<string, AttributeQueryOperation> Operations =
        ImmutableDictionary<string, AttributeQueryOperation>.Empty
            .Add("IsDefined", AttributeQueryOperation.IsDefined)
            .Add("GetCustomAttribute", AttributeQueryOperation.GetOne)
            .Add("GetCustomAttributes", AttributeQueryOperation.GetMany);

    public AttributeQueryCall? Classify(MethodInstanceModel method)
    {
        ArgumentNullException.ThrowIfNull(method);
        if (!declaringTypes.TryGetValue(method.Definition.DeclaringType, out var api))
        {
            return null;
        }
        if (!Operations.TryGetValue(method.Definition.Name, out var operation))
        {
            return null;
        }

        var parameters = method.Signature.ParameterSignatureTypes;
        var generic = method.MethodArguments.Length == 1;
        var targetParameters = api == AttributeQueryApiKind.Instance ? 0 : 1;
        if (Unfiltered(method, api, operation, parameters, generic, targetParameters) is { } unfiltered)
        {
            return unfiltered;
        }
        var filteredParameters = targetParameters + (generic ? 0 : 1);
        var hasInherit = parameters.Length == filteredParameters + 1;
        if (api == AttributeQueryApiKind.Instance &&
            (method.Definition.IsStatic || generic || operation == AttributeQueryOperation.GetOne || !hasInherit) ||
            api != AttributeQueryApiKind.Instance &&
            (!method.Definition.IsStatic || parameters.IsEmpty || !Target(parameters[0])) ||
            generic && (api != AttributeQueryApiKind.Extension || operation == AttributeQueryOperation.IsDefined) ||
            method.MethodArguments.Length > 1 ||
            parameters.Length != filteredParameters && !hasInherit ||
            !generic && (parameters.Length <= targetParameters || !parameters[targetParameters].Equals(typeType)) ||
            hasInherit && parameters[^1].StackKind != CliValueKind.I4)
        {
            // Unlowered overloads retain their managed implementation, including
            // catchable platform fallbacks. This classifier selects bounded queries.
            return null;
        }

        return new(operation,
            parameters.Length + (api == AttributeQueryApiKind.Instance ? 1 : 0),
            generic ? null : 1,
            generic ? method.MethodArguments[0] : null,
            hasInherit ? parameters.Length - (api == AttributeQueryApiKind.Instance ? 0 : 1) : null)
        {
            Api = api,
        };
    }

    // The targets Attribute's statics and the extensions take: a member (a type), an assembly, a module.
    private bool Target(CliTypeIdentity parameter) =>
        parameter.Equals(memberInfoType) || parameter.Equals(assemblyType) || parameter.Equals(moduleType);

    // GetCustomAttributes with no filter: a target's own GetCustomAttributes(bool), and Attribute's statics and the
    // extensions taking only the target, or the target and an inheritance choice.
    private AttributeQueryCall? Unfiltered(MethodInstanceModel method, AttributeQueryApiKind api,
        AttributeQueryOperation operation, ImmutableArray<CliTypeIdentity> parameters, bool generic, int targetParameters)
    {
        if (operation != AttributeQueryOperation.GetMany || generic || objectType is null || attributeType is null ||
            parameters.Length is var count && count != targetParameters && count != targetParameters + 1 ||
            count > targetParameters && parameters[^1].StackKind != CliValueKind.I4 ||
            api == AttributeQueryApiKind.Instance && (method.Definition.IsStatic || count == 0) ||
            api != AttributeQueryApiKind.Instance && (!method.Definition.IsStatic || !Target(parameters[0])))
        {
            return null;
        }
        // The arguments are the target (this, for a target's own method), then the choice.
        return new(operation, count + (api == AttributeQueryApiKind.Instance ? 1 : 0), null, null,
            count > targetParameters ? count - (api == AttributeQueryApiKind.Instance ? 0 : 1) : null)
        {
            Api = api,
            ImplicitFilter = api == AttributeQueryApiKind.Instance ? objectType : attributeType,
        };
    }
}
