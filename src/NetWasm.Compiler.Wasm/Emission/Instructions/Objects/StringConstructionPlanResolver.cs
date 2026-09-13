using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Objects;

internal sealed class StringConstructionPlanResolver :
    IStringConstructionPlanResolver
{
    private static readonly FrozenDictionary<SignatureKey, StringConstructionPlan>
        Plans = new Dictionary<SignatureKey, StringConstructionPlan>
        {
            [new SignatureKey(
                2,
                ParameterKind.Integer,
                ParameterKind.Integer,
                ParameterKind.None)] = new(
                    StringConstructionSourceKind.RepeatedCharacter,
                    null,
                    1),
            [new SignatureKey(
                1,
                ParameterKind.CharacterArray,
                ParameterKind.None,
                ParameterKind.None)] = new(
                    StringConstructionSourceKind.CharacterArray,
                    null,
                    null),
            [new SignatureKey(
                3,
                ParameterKind.CharacterArray,
                ParameterKind.Integer,
                ParameterKind.Integer)] = new(
                    StringConstructionSourceKind.CharacterArray,
                    1,
                    2),
        }.ToFrozenDictionary();

    public StringConstructionPlan Resolve(
        MethodSignatureModel signature,
        int instructionOffset)
    {
        ArgumentNullException.ThrowIfNull(signature);

        var key = new SignatureKey(
            signature.ParameterTypes.Length,
            Classify(signature, 0),
            Classify(signature, 1),
            Classify(signature, 2));
        if (Plans.TryGetValue(key, out var plan))
        {
            return plan;
        }

        throw new CompilerException(new CompilerDiagnostic(
            DiagnosticCode.UnsupportedMetadata,
            "the System.String constructor signature is not supported",
            "System.String",
            instructionOffset));
    }

    private static ParameterKind Classify(
        MethodSignatureModel signature,
        int index)
    {
        if (index >= signature.ParameterTypes.Length)
        {
            return ParameterKind.None;
        }
        if (signature.ParameterTypes[index] == CliValueKind.I4)
        {
            return ParameterKind.Integer;
        }
        if (IsCharacterArray(signature.ParameterSignatureTypes[index]))
        {
            return ParameterKind.CharacterArray;
        }
        return ParameterKind.Other;
    }

    private static bool IsCharacterArray(CliTypeIdentity type) =>
        type.Shape == CliTypeShape.SzArray &&
        type.ElementType!.CanonicalName == "primitive:char";

    private enum ParameterKind
    {
        None,
        Integer,
        CharacterArray,
        Other,
    }

    private readonly record struct SignatureKey(
        int Count,
        ParameterKind First,
        ParameterKind Second,
        ParameterKind Third);
}
