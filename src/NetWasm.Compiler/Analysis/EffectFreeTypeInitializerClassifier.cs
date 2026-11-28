// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

// Whether a type initializer changes nothing: it only stores default values (zero, null, a default struct) into its own
// type's static fields, which start as default, and returns. Roslyn emits such an initializer for `= default` and
// `= null` field initializers; running it, or checking that it ran, is observably the same as not having it.
internal interface IEffectFreeTypeInitializerClassifier
{
    bool Classify(MethodDefinitionModel initializer);
}

internal sealed class EffectFreeTypeInitializerClassifier(
    ICalleeMethodBodyReader bodies,
    IFieldRepository fields) : IEffectFreeTypeInitializerClassifier
{
    private readonly ICalleeMethodBodyReader _bodies = bodies ?? throw new ArgumentNullException(nameof(bodies));
    private readonly IFieldRepository _fields = fields ?? throw new ArgumentNullException(nameof(fields));

    public bool Classify(MethodDefinitionModel initializer)
    {
        ArgumentNullException.ThrowIfNull(initializer);
        if (!_bodies.TryReadMethodBody(initializer, out var body) || !body.ExceptionRegions.IsEmpty)
        {
            return false;
        }
        var instructions = body.Instructions.Where(static instruction => instruction.Operation != CilOperation.Nop)
            .ToArray();
        if (instructions.Length == 0 || instructions[^1].Operation != CilOperation.Return)
        {
            return false;
        }
        // Each store is a default value and then its store, with nothing in between that could branch or throw.
        var index = 0;
        while (index < instructions.Length - 1)
        {
            var value = instructions[index];
            if (value.Operation == CilOperation.LoadStaticFieldAddress && index + 1 < instructions.Length - 1 &&
                instructions[index + 1].Operation == CilOperation.InitializeObject && OwnStatic(value, initializer))
            {
                index += 2;
                continue;
            }
            if (!IsDefaultValue(value))
            {
                return false;
            }
            index++;
            while (index < instructions.Length - 1 && IsConversion(instructions[index]))
            {
                index++;
            }
            if (index >= instructions.Length - 1 || instructions[index].Operation != CilOperation.StoreStaticField ||
                !OwnStatic(instructions[index], initializer))
            {
                return false;
            }
            index++;
        }
        return true;
    }

    // A zero or null constant, whose bits are the field's default (-0.0 is not).
    private static bool IsDefaultValue(CilInstruction instruction) => instruction switch
    {
        { Operation: CilOperation.LoadNull } => true,
        { Operation: CilOperation.LoadInt32, Operand: CilOperand.ConstantI4 { Value: 0 } } => true,
        { Operation: CilOperation.LoadInt64, Operand: CilOperand.ConstantI8 { Value: 0 } } => true,
        { Operation: CilOperation.LoadFloat32, Operand: CilOperand.ConstantF4 constant } =>
            BitConverter.SingleToInt32Bits(constant.Value) == 0,
        { Operation: CilOperation.LoadFloat64, Operand: CilOperand.ConstantF8 constant } =>
            BitConverter.DoubleToInt64Bits(constant.Value) == 0,
        _ => false,
    };

    // A conversion of zero is zero in every width.
    private static bool IsConversion(CilInstruction instruction) => instruction.Operation is
        CilOperation.ConvertInt32 or CilOperation.ConvertInt32Unsigned or CilOperation.ConvertInt64 or
        CilOperation.ConvertInt64Unsigned or CilOperation.ConvertNativeInt or CilOperation.ConvertNativeUInt or
        CilOperation.ConvertFloat32 or CilOperation.ConvertFloat64 or CilOperation.ConvertNumeric;

    // A static field the initializer's own type declares.
    private bool OwnStatic(CilInstruction instruction, MethodDefinitionModel initializer)
    {
        var field = instruction.Operand switch
        {
            CilOperand.Entity entity => _fields.GetField(entity.Key),
            CilOperand.FieldInstance instance => instance.Value.Definition,
            _ => null,
        };
        return field is { IsStatic: true } && field.DeclaringType == initializer.DeclaringType;
    }
}
