// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis.Constants;

// Ordinal string equality and byte-span SequenceEqual over constant operands,
// as .NET 11's JIT folds them. Only the core library's methods qualify.
internal sealed class ConstantComparisonEvaluator : IConstantComparisonEvaluator
{
    private static readonly CliTypeIdentity StringType = CliTypeIdentity.Primitive("string", CliValueKind.ManagedReference, isValueType: false);
    private readonly Lazy<AssemblyIdentity> _coreLibrary;

    public ConstantComparisonEvaluator(ITypeDefinitionResolver typeDefinitions)
    {
        ArgumentNullException.ThrowIfNull(typeDefinitions);
        _coreLibrary = new(() => typeDefinitions.ResolveTypeIdentity(StringType).Key.Assembly);
    }

    public ScalarConstant? Evaluate(MethodInstanceModel method, IReadOnlyList<ScalarConstant> arguments)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(arguments);
        var definition = method.Definition;
        if (!definition.Key.Assembly.Equals(_coreLibrary.Value)) return null;
        var parameters = method.Signature.ParameterSignatureTypes;
        bool? equal = (method.DeclaringType.CanonicalName, definition.Name, definition.IsStatic) switch
        {
            ("primitive:string", "op_Equality" or "Equals", true) when AreStrings(parameters, 2) => StringsEqual(arguments),
            ("primitive:string", "op_Inequality", true) when AreStrings(parameters, 2) => !StringsEqual(arguments),
            // A null receiver throws, so only a literal receiver folds.
            ("primitive:string", "Equals", false) when AreStrings(parameters, 1) && arguments is [ScalarConstant.String, _] =>
                StringsEqual(arguments),
            (_, "SequenceEqual", true) when method.DeclaringType.FullName == "System.MemoryExtensions" &&
                parameters.Length == 2 && parameters.All(parameter => IsByteSpan(parameter, method.MethodArguments)) =>
                arguments is [ScalarConstant.Bytes left, ScalarConstant.Bytes right] ? left.Value.SequenceEqual(right.Value) : null,
            _ => null,
        };
        return equal is { } value ? new ScalarConstant.Int32(value ? 1 : 0) : null;
    }

    private static bool AreStrings(ImmutableArray<CliTypeIdentity> parameters, int count) =>
        parameters.Length == count && parameters.All(parameter => parameter.CanonicalName == "primitive:string");

    private static bool? StringsEqual(IReadOnlyList<ScalarConstant> arguments) => arguments switch
    {
        [ScalarConstant.Null, ScalarConstant.Null] => true,
        [ScalarConstant.String left, ScalarConstant.String right] => string.Equals(left.Value, right.Value, StringComparison.Ordinal),
        [ScalarConstant.String, ScalarConstant.Null] or [ScalarConstant.Null, ScalarConstant.String] => false,
        _ => null,
    };

    private static bool IsByteSpan(CliTypeIdentity parameter, ImmutableArray<CliTypeIdentity> methodArguments)
    {
        if (!ReadOnlySpanDataResolver.TryGetReadOnlySpanElement(parameter, out var element)) return false;
        if (element.Shape == CliTypeShape.GenericMethodParameter)
        {
            if ((uint)element.GenericParameterIndex >= (uint)methodArguments.Length) return false;
            element = methodArguments[element.GenericParameterIndex];
        }
        return element.CanonicalName == "primitive:u1";
    }
}
