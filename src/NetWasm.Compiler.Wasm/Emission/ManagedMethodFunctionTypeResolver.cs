// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.StructResults;

namespace NetWasm.Compiler.Wasm.Emission;

// A value type the classifier returns in results is the function's results, in field order; any other is written to a
// buffer the caller passes first.
internal sealed class ManagedMethodFunctionTypeResolver(IStructResultClassifier? structResults = null) :
    IManagedMethodFunctionTypeResolver
{
    public WasmFunctionType Resolve(MethodDefinitionModel method)
    {
        ArgumentNullException.ThrowIfNull(method);

        return Create(FieldParameters(NormalizeParameterTypes(method.WasmParameterTypes), method.IsStatic, method.Signature),
            method.Signature);
    }

    public WasmFunctionType Resolve(MethodInstanceModel method)
    {
        ArgumentNullException.ThrowIfNull(method);

        return Create(FieldParameters(NormalizeParameterTypes(GetWasmParameterTypes(method)), method.Definition.IsStatic,
            method.Signature), method.Signature);
    }

    // A struct parameter the classifier passes by its fields takes a parameter per field.
    private ImmutableArray<CliValueKind> FieldParameters(ImmutableArray<CliValueKind> parameters, bool isStatic,
        MethodSignatureModel signature)
    {
        if (structResults is null || signature.ParameterSignatureTypes.Length + (isStatic ? 0 : 1) != parameters.Length)
        {
            return parameters;
        }
        var builder = ImmutableArray.CreateBuilder<CliValueKind>(parameters.Length);
        var first = isStatic ? 0 : 1;
        for (var index = 0; index < parameters.Length; index++)
        {
            if (index >= first && FieldArgument(signature.ParameterSignatureTypes[index - first]) is { } shape)
            {
                builder.AddRange(shape.Results);
            }
            else
            {
                builder.Add(parameters[index]);
            }
        }
        return builder.ToImmutable();
    }

    // Whether a parameter of the type is passed by its fields.
    private StructResultShape? FieldArgument(CliTypeIdentity type) =>
        type.StackKind == CliValueKind.ValueType && !type.ContainsGenericParameters ? structResults?.Classify(type) : null;

    private WasmFunctionType Create(ImmutableArray<CliValueKind> parameters, MethodSignatureModel signature)
    {
        if (signature.ReturnSignatureType.StackKind != CliValueKind.ValueType) return new(parameters, signature.ReturnType);
        return structResults?.Classify(signature.ReturnSignatureType) is { } shape
            ? new(parameters, shape.Results[0]) { MoreResults = shape.Results[1..] }
            : new(parameters.Insert(0, CliValueKind.ManagedAddress), CliValueKind.Void);
    }

    private static ImmutableArray<CliValueKind> GetWasmParameterTypes(
        MethodInstanceModel method) => method.Definition.IsStatic
        ? method.Signature.ParameterTypes
        : method.Signature.ParameterTypes.Insert(
            0,
            method.DeclaringType.IsValueType
                ? CliValueKind.ManagedAddress
                : CliValueKind.ManagedReference);

    private static ImmutableArray<CliValueKind> NormalizeParameterTypes(
        ImmutableArray<CliValueKind> parameters) =>
        [.. parameters.Select(static parameter => parameter == CliValueKind.ValueType
            ? CliValueKind.ManagedAddress
            : parameter)];
}
