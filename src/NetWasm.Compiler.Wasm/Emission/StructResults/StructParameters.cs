// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.StructResults;

// Which parameters a method takes by their fields (a struct the classifier accepts), where each one's Wasm parameters
// start, and which parameter a call's argument fills.
internal static class StructParameters
{
    // Each declared parameter's first Wasm parameter, counting from first, and its shape when it is passed by its
    // fields (then it takes a Wasm parameter per field).
    public static ImmutableArray<(int First, StructResultShape? Shape)> Layout(IStructResultClassifier? classifier,
        ImmutableArray<CliTypeIdentity> parameters, int first)
    {
        var layout = ImmutableArray.CreateBuilder<(int, StructResultShape?)>(parameters.Length);
        foreach (var parameter in parameters)
        {
            var shape = Shape(classifier, parameter);
            layout.Add((first, shape));
            first += shape?.Results.Length ?? 1;
        }
        return layout.MoveToImmutable();
    }

    public static StructResultShape? Shape(IStructResultClassifier? classifier, CliTypeIdentity? parameter) =>
        parameter is { StackKind: CliValueKind.ValueType, ContainsGenericParameters: false } ? classifier?.Classify(parameter) : null;

    // The parameter type a call's argument at the index fills (index 0 is the receiver of an instance method).
    public static CliTypeIdentity? Parameter(MethodSignatureModel signature, bool isStatic, int index)
    {
        var parameter = index - (isStatic ? 0 : 1);
        return parameter >= 0 && parameter < signature.ParameterSignatureTypes.Length ? signature.ParameterSignatureTypes[parameter] : null;
    }
}
