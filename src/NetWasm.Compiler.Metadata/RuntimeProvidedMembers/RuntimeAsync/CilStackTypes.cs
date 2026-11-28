// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers.RuntimeAsync;

// The type of each value on the evaluation stack before each instruction of a body, bottom first, for the values the
// runtime async lowering spills across an await: what the instruction that pushed it says (a local's, an argument's, a
// field's or a call's type, a constant's, a cast's), a primitive's stack type after arithmetic or a conversion, and null
// where this does not follow the producer. Where paths meet, a slot two producers fill with different types takes what
// both are (C#'s `flag ? null : box`, `flag ? aByte : 0` or `flag ? Color.Red : color` under an await): two references
// are objects, two numbers of one stack type (primitives or enums) that stack type; anything else is unknown. A field the decoder names by its definition (any field of
// a type that is not generic) takes the type the field's signature gives. Bodies whose depths it cannot follow have
// none.
internal static class CilStackTypes
{
    private static readonly CliTypeIdentity Object = CliTypeIdentity.FromStackKind(CliValueKind.ManagedReference);

    public static IReadOnlyDictionary<int, ImmutableArray<CliTypeIdentity?>>? Compute(MethodInstanceModel method,
        CilMethodBody body, IFieldRepository? fields = null)
    {
        if (CilStackDepths.Compute(body) is null) return null;
        var positions = body.Instructions.Select((instruction, index) => (instruction.Offset, index))
            .ToDictionary(item => item.Offset, item => item.index);
        var stacks = new Dictionary<int, ImmutableArray<CliTypeIdentity?>>();
        var work = new Stack<int>();
        void Reach(int offset, ImmutableArray<CliTypeIdentity?> stack)
        {
            if (stacks.TryGetValue(offset, out var known))
            {
                var merged = known.Zip(stack, Join).ToImmutableArray();
                if (merged.SequenceEqual(known)) return;
                stack = merged;
            }
            stacks[offset] = stack;
            work.Push(offset);
        }
        if (body.Instructions.IsEmpty) return stacks;
        Reach(body.Instructions[0].Offset, []);
        foreach (var region in body.ExceptionRegions)
        {
            ImmutableArray<CliTypeIdentity?> exception = region.Kind is CilExceptionRegionKind.Catch or CilExceptionRegionKind.Filter
                ? [region.CatchTypeIdentity ?? Object]
                : [];
            Reach(region.HandlerOffset, exception);
            if (region.FilterOffset is { } filter) Reach(filter, [Object]);
        }
        while (work.Count != 0)
        {
            var offset = work.Pop();
            var instruction = body.Instructions[positions[offset]];
            var before = stacks[offset];
            var after = Apply(method, body, fields, instruction, before);
            switch (instruction.Operation)
            {
                case CilOperation.Leave:
                    Reach(((CilOperand.BranchTarget)instruction.Operand).Offset, []);
                    continue;
                case CilOperation.Branch:
                    Reach(((CilOperand.BranchTarget)instruction.Operand).Offset, after);
                    continue;
                case CilOperation.Return or CilOperation.Throw or CilOperation.Rethrow or CilOperation.EndFinally or
                    CilOperation.EndFilter:
                    continue;
            }
            switch (instruction.Operand)
            {
                case CilOperand.BranchTarget target:
                    Reach(target.Offset, after);
                    break;
                case CilOperand.SwitchTargets targets:
                    foreach (var target in targets.Offsets) Reach(target, after);
                    break;
            }
            Reach(instruction.NextOffset, after);
        }
        return stacks;
    }

    // The stack after the instruction: its pops removed and its pushes typed.
    private static ImmutableArray<CliTypeIdentity?> Apply(MethodInstanceModel method, CilMethodBody body,
        IFieldRepository? fields, CilInstruction instruction, ImmutableArray<CliTypeIdentity?> before)
    {
        var (pops, pushes) = Effect(instruction, before.Length);
        var popped = before[(before.Length - pops)..];
        var kept = before[..(before.Length - pops)];
        if (pushes == 0) return kept;
        if (instruction.Operation == CilOperation.Duplicate) return [.. kept, popped[0], popped[0]];
        return [.. kept, Pushed(method, body, fields, instruction, popped)];
    }

    // The depth pass has already accepted the body, so it knows every operation here.
    private static (int Pops, int Pushes) Effect(CilInstruction instruction, int depth) =>
        CilStackDepths.Effect(instruction, depth)!.Value;

    private static CliTypeIdentity? Pushed(MethodInstanceModel method, CilMethodBody body, IFieldRepository? fields,
        CilInstruction instruction, ImmutableArray<CliTypeIdentity?> popped) => (instruction.Operation, instruction.Operand) switch
        {
            (CilOperation.LoadArgument, CilOperand.Index index) => Argument(method, index.Value),
            (CilOperation.LoadArgumentAddress, CilOperand.Index index) => Address(Argument(method, index.Value)),
            (CilOperation.LoadLocal, CilOperand.Index index) => body.LocalSignatureTypes[index.Value],
            (CilOperation.LoadLocalAddress, CilOperand.Index index) => Address(body.LocalSignatureTypes[index.Value]),
            (CilOperation.LoadInt32 or CilOperation.CompareEqual or CilOperation.CompareGreaterThanSigned or
                CilOperation.CompareGreaterThanUnsigned or CilOperation.CompareLessThanSigned or
                CilOperation.CompareLessThanUnsigned or CilOperation.SizeOf or CilOperation.ConvertInt32 or
                CilOperation.ConvertInt32Unsigned, _) => CliTypeIdentity.FromStackKind(CliValueKind.I4),
            (CilOperation.LoadInt64 or CilOperation.ConvertInt64 or CilOperation.ConvertInt64Unsigned, _) =>
                CliTypeIdentity.FromStackKind(CliValueKind.I8),
            (CilOperation.LoadFloat32 or CilOperation.ConvertFloat32, _) => CliTypeIdentity.FromStackKind(CliValueKind.F4),
            (CilOperation.LoadFloat64 or CilOperation.ConvertFloat64 or CilOperation.ConvertFloatUnsigned, _) =>
                CliTypeIdentity.FromStackKind(CliValueKind.F8),
            (CilOperation.ConvertNativeInt or CilOperation.ConvertNativeUInt or CilOperation.LoadArrayLength or
                CilOperation.LoadFunction or CilOperation.LoadVirtualFunction or CilOperation.LocalAllocate, _) =>
                CliTypeIdentity.FromStackKind(CliValueKind.NativeInt),
            (CilOperation.LoadNull or CilOperation.LoadString or CilOperation.Box or CilOperation.LoadArrayElementReference, _) =>
                Object,
            (CilOperation.LoadField or CilOperation.LoadStaticField, CilOperand.FieldInstance field) => field.Value.FieldType,
            (CilOperation.LoadFieldAddress or CilOperation.LoadStaticFieldAddress, CilOperand.FieldInstance field) =>
                Address(field.Value.FieldType),
            (CilOperation.LoadField or CilOperation.LoadStaticField, CilOperand.Entity field) when fields is not null =>
                fields.GetField(field.Key).SignatureType,
            (CilOperation.LoadFieldAddress or CilOperation.LoadStaticFieldAddress, CilOperand.Entity field)
                when fields is not null => Address(fields.GetField(field.Key).SignatureType),
            (CilOperation.ConvertNumeric, CilOperand.NumericConversion conversion) => CliTypeIdentity.FromStackKind(
                conversion.Native ? CliValueKind.NativeInt : conversion.BitWidth == 64 ? CliValueKind.I8 : CliValueKind.I4),
            (CilOperation.Call or CilOperation.CallVirtual, CilOperand.MethodInstance callee) =>
                callee.Value.Signature.ReturnSignatureType,
            (CilOperation.CallIndirect, CilOperand.CallSite site) => site.Signature.ReturnSignatureType,
            (CilOperation.NewObject, CilOperand.MethodInstance constructor) => constructor.Value.DeclaringType,
            (CilOperation.CastClass or CilOperation.UnboxAny or CilOperation.LoadObject or CilOperation.LoadArrayElement,
                CilOperand.TypeIdentity type) => type.Value,
            (CilOperation.IsInstance, CilOperand.TypeIdentity type) => type.Value.IsValueType ? Object : type.Value,
            (CilOperation.Unbox or CilOperation.LoadArrayElementAddress, CilOperand.TypeIdentity type) => Address(type.Value),
            (CilOperation.NewArray, CilOperand.TypeIdentity type) => CliTypeIdentity.SzArray(type.Value),
            (CilOperation.Negate or CilOperation.OnesComplement or CilOperation.CheckFinite or CilOperation.Add or
                CilOperation.Subtract or CilOperation.Multiply or CilOperation.Divide or CilOperation.DivideUnsigned or
                CilOperation.Remainder or CilOperation.RemainderUnsigned or CilOperation.BitwiseAnd or CilOperation.BitwiseOr or
                CilOperation.BitwiseXor or CilOperation.ShiftLeft or CilOperation.ShiftRightSigned or
                CilOperation.ShiftRightUnsigned or CilOperation.AddChecked or CilOperation.AddCheckedUnsigned or
                CilOperation.SubtractChecked or CilOperation.SubtractCheckedUnsigned or CilOperation.MultiplyChecked or
                CilOperation.MultiplyCheckedUnsigned, _) => Primitive(popped[0]),
            _ => null,
        };

    // What a slot two paths fill holds: one type, or what both types are.
    private static CliTypeIdentity? Join(CliTypeIdentity? left, CliTypeIdentity? right)
    {
        if (Equals(left, right)) return left;
        if (left is null || right is null) return null;
        if (IsReference(left) && IsReference(right)) return Object;
        // Two numbers of one stack type: primitives, or an enum (which has its underlying type's) and a primitive.
        return left.StackKind == right.StackKind && left.Shape != CliTypeShape.ManagedByReference &&
            left.StackKind is CliValueKind.I4 or CliValueKind.I8 or CliValueKind.F4 or CliValueKind.F8 or CliValueKind.NativeInt
            ? CliTypeIdentity.FromStackKind(left.StackKind)
            : null;
    }

    // A class, interface, array or string: not a value type, a pointer, or a generic parameter that may be one.
    private static bool IsReference(CliTypeIdentity type) =>
        type.StackKind == CliValueKind.ManagedReference && !type.IsValueType &&
        type.Shape is CliTypeShape.Primitive or CliTypeShape.Named or CliTypeShape.SzArray or CliTypeShape.Array or
            CliTypeShape.GenericInstantiation;

    // A primitive's stack type: what arithmetic on it gives.
    private static CliTypeIdentity? Primitive(CliTypeIdentity? operand) =>
        operand is { StackKind: CliValueKind.I4 or CliValueKind.I8 or CliValueKind.F4 or CliValueKind.F8 or CliValueKind.NativeInt } &&
        operand.Shape == CliTypeShape.Primitive
            ? CliTypeIdentity.FromStackKind(operand.StackKind)
            : null;

    private static CliTypeIdentity Argument(MethodInstanceModel method, int index)
    {
        if (method.Definition.IsStatic) return method.Signature.ParameterSignatureTypes[index];
        if (index == 0)
            return method.DeclaringType.IsValueType ? CliTypeIdentity.ManagedByReference(method.DeclaringType) : method.DeclaringType;
        return method.Signature.ParameterSignatureTypes[index - 1];
    }

    private static CliTypeIdentity Address(CliTypeIdentity type) => CliTypeIdentity.ManagedByReference(type);
}
