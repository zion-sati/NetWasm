// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

// Array.Copy converts primitive elements of one type to another (int to long, an enum to its
// widened underlying type), which takes code to read element types and widen. A Copy whose two
// arrays have one static one-dimensional type calls Array.CopyOfOneType with the same parameters
// instead, which never converts: such arrays hold one element type, or two the runtime treats as
// one (signed and unsigned integers of one size, an enum and its underlying type), which desktop
// .NET copies as they are. So does a Copy between one-dimensional arrays whose static element types
// are both reference types (string[] to object[]): such an array holds references whatever its
// run-time type, and the runtime checks each element it assigns. A program then carries the
// conversion only where arrays of value elements of two static types, or typed Array, meet in a Copy.
internal sealed class ArrayCopyCallRewriter(
    ICalledMethodResolver calledMethods,
    ITypeDefinitionResolver typeDefinitions,
    IMethodRepository methods,
    IMethodInstanceResolver methodInstances,
    ISymbolFormatter symbols,
    IControlFlowGraphBuilder graphs,
    ITypedStackValidator stacks,
    IStaticReferenceTypeAnalyzer staticTypes) : IMethodRewriteRule
{
    private const string OneTypeCopy = "CopyOfOneType";

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!body.Instructions.Any(instruction => Copy(instruction) is not null))
        {
            return body;
        }
        var types = staticTypes.Analyze(stacks.Validate(graphs.Build(body)));
        var instructions = body.Instructions.ToBuilder();
        var changed = false;
        for (var index = 0; index < instructions.Count; index++)
        {
            var instruction = body.Instructions[index];
            if (Copy(instruction) is not { } copy ||
                !types.TryGetValue(instruction.Offset, out var stack) ||
                !CopiesWithoutConversion(copy, stack))
            {
                continue;
            }
            instructions[index] = instruction with { Operand = new CilOperand.MethodInstance(OneTypeOverload(copy)) };
            changed = true;
        }
        return changed ? body with { Instructions = instructions.ToImmutable() } : body;
    }

    // Copy(Array, Array, int or long) and Copy(Array, int or long, Array, int or long, int or long).
    private MethodInstanceModel? Copy(CilInstruction instruction) =>
        instruction.Operation == CilOperation.Call &&
        calledMethods.Resolve(instruction) is
        {
            Definition: { Name: "Copy", IsStatic: true, GenericArity: 0 } definition,
        } copy &&
        definition.Signature.ParameterSignatureTypes.Length is 3 or 5 &&
        symbols.Format(definition.DeclaringType) == "System.Array"
            ? copy
            : null;

    // The source is the first argument and the destination the second or third, each typed by what
    // produced it; both must be one-dimensional arrays, of one static type or both of reference elements.
    // The validated stack holds the call's arguments.
    private static bool CopiesWithoutConversion(MethodInstanceModel copy, ImmutableArray<CliTypeIdentity?> stack)
    {
        var arity = copy.Definition.Signature.ParameterSignatureTypes.Length;
        var source = stack[^arity];
        var destination = arity == 3 ? stack[^2] : stack[^3];
        return source is { Shape: CliTypeShape.SzArray } && destination is { Shape: CliTypeShape.SzArray } &&
            (Equals(source, destination) || HoldsReferences(source) && HoldsReferences(destination));
    }

    private static bool HoldsReferences(CliTypeIdentity array) =>
        array.ElementType is { IsValueType: false, StackKind: CliValueKind.ManagedReference, ContainsGenericParameters: false };

    // CoreLib's Array declares a CopyOfOneType for each Copy overload, and a program has one System.Array (NW1004).
    private MethodInstanceModel OneTypeOverload(MethodInstanceModel copy)
    {
        var definition = copy.Definition;
        var overload = typeDefinitions
            .ResolveTypeIdentity(CliTypeIdentity.Named(definition.Key.Assembly, "System", "Array", false))
            .Methods
            .Select(methods.GetMethod)
            .Single(method => method.Name == OneTypeCopy &&
                method.Signature.ParameterSignatureTypes.SequenceEqual(definition.Signature.ParameterSignatureTypes));
        return methodInstances.ResolveMethodInstance(
            overload.Key.Assembly,
            overload.Key.MetadataToken,
            symbols.Format(overload),
            0,
            new CliGenericContext([], []));
    }
}
