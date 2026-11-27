// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;

namespace NetWasm.Compiler.ControlFlow;

public sealed record ValidatedControlFlowGraph(
    ControlFlowGraph Graph,
    ImmutableDictionary<int, ImmutableArray<CliValueKind>> EntryStacks,
    ImmutableDictionary<int, ImmutableArray<CliValueKind>> InstructionEntryStacks)
{
    // The deepest evaluation stack any instruction leaves.
    public int MaximumStackDepth { get; init; }

    // Each block's stack after its last instruction, before it meets its successors' other predecessors: a native int
    // that a join takes as a managed pointer (Merge) is still a native int here.
    public ImmutableDictionary<int, ImmutableArray<CliValueKind>> ExitStacks { get; init; } =
        ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty;

    // Where a value met a slot of the other of int32 and native int, which ECMA-335 III.1.6 lets
    // an argument, a store to a local, argument or field, or a return coerce implicitly.
    public ImmutableArray<ImplicitCoercion> ImplicitCoercions { get; init; } = [];
}

// The value at a stack index (from the bottom of the instruction's entry stack) coerced from
// one kind to the other as the instruction consumes it.
public sealed record ImplicitCoercion(int Offset, int StackIndex, CliValueKind From, CliValueKind To);

public sealed class TypedStackValidator(
    ITypeRepository types,
    IFieldRepository fields,
    IMethodRepository methods,
    IStackTypeCompatibilityValidator stackTypeCompatibility) : ITypedStackValidator
{
    private readonly ITypeRepository _types =
        types ?? throw new ArgumentNullException(nameof(types));
    private readonly IFieldRepository _fields =
        fields ?? throw new ArgumentNullException(nameof(fields));
    private readonly IMethodRepository _methods =
        methods ?? throw new ArgumentNullException(nameof(methods));
    private readonly IStackTypeCompatibilityValidator _stackTypeCompatibility =
        stackTypeCompatibility ??
        throw new ArgumentNullException(nameof(stackTypeCompatibility));

    public ValidatedControlFlowGraph Validate(ControlFlowGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var entryStacks = new Dictionary<int, ImmutableArray<CliValueKind>>
        {
            [graph.Entry.Index] = [],
        };
        var pending = new Queue<int>();
        var instructionEntryStacks = new Dictionary<int, ImmutableArray<CliValueKind>>();
        var exitStacks = new Dictionary<int, ImmutableArray<CliValueKind>>();
        var coercions = new List<ImplicitCoercion>();
        var maximumDepth = 0;
        pending.Enqueue(graph.Entry.Index);
        foreach (var region in graph.MethodBody.ExceptionRegions)
        {
            Seed(region.HandlerOffset, HandlerStack(region));
            if (region.FilterOffset is int filterOffset)
            {
                Seed(filterOffset, [CliValueKind.ManagedReference]);
            }
        }
        while (pending.TryDequeue(out var blockIndex))
        {
            var block = graph.GetBlock(blockIndex);
            var stack = new List<CliValueKind>(entryStacks[blockIndex]);
            foreach (var instruction in block.Instructions)
            {
                instructionEntryStacks[instruction.Offset] = [.. stack];
                Apply(graph.MethodBody, instruction, stack, coercions);
                maximumDepth = Math.Max(maximumDepth, stack.Count);
                if (stack.Count > graph.MethodBody.MaxStack)
                {
                    throw Invalid(graph.MethodBody, instruction, "evaluation stack exceeds maxstack");
                }
            }

            exitStacks[blockIndex] = [.. stack];
            foreach (var successor in graph.Successors[blockIndex])
            {
                var outgoing = stack.ToImmutableArray();
                if (!entryStacks.TryGetValue(successor, out var existing))
                {
                    entryStacks.Add(successor, outgoing);
                    pending.Enqueue(successor);
                }
                else if (!existing.AsSpan().SequenceEqual(outgoing.AsSpan()))
                {
                    var merged = Merge(existing, outgoing) ?? throw Invalid(
                        graph.MethodBody,
                        block.Terminator,
                        $"incompatible evaluation stack at merge into IL_{graph.GetBlock(successor).StartOffset:x4}");
                    if (!merged.AsSpan().SequenceEqual(existing.AsSpan()))
                    {
                        entryStacks[successor] = merged;
                        pending.Enqueue(successor);
                    }
                }
            }
        }

        return new ValidatedControlFlowGraph(
            graph,
            entryStacks.ToImmutableDictionary(),
            instructionEntryStacks.ToImmutableDictionary())
        {
            MaximumStackDepth = maximumDepth,
            ExitStacks = exitStacks.ToImmutableDictionary(),
            ImplicitCoercions = [.. coercions.Distinct()],
        };

        void Seed(int offset, ImmutableArray<CliValueKind> stack)
        {
            var block = graph.GetBlockAtOffset(offset).Index;
            if (!graph.ReachableBlocks.Contains(block))
            {
                return;
            }
            if (entryStacks.TryGetValue(block, out var existing))
            {
                if (!existing.AsSpan().SequenceEqual(stack.AsSpan()))
                {
                    throw new CompilerException(new CompilerDiagnostic(
                        DiagnosticCode.InvalidCil,
                        $"incompatible exception entry stack at IL_{offset:x4}",
                        graph.MethodBody.Method.Name,
                        offset));
                }
                return;
            }
            entryStacks.Add(block, stack);
            pending.Enqueue(block);
        }
    }

    // Stacks that meet at a join must agree, except that a native int and a managed pointer may meet at one depth, as
    // C# emits for `ref *(T*)null` against a real reference; desktop .NET's importer takes the pair as a managed
    // pointer. Both are pointer-sized and share their slot's local, and a managed pointer outside the heap is no root.
    private static ImmutableArray<CliValueKind>? Merge(ImmutableArray<CliValueKind> existing, ImmutableArray<CliValueKind> incoming)
    {
        if (existing.Length != incoming.Length)
        {
            return null;
        }
        var merged = ImmutableArray.CreateBuilder<CliValueKind>(existing.Length);
        for (var index = 0; index < existing.Length; index++)
        {
            var (left, right) = (existing[index], incoming[index]);
            if (left == right)
            {
                merged.Add(left);
            }
            else if (left is CliValueKind.NativeInt or CliValueKind.ManagedAddress &&
                     right is CliValueKind.NativeInt or CliValueKind.ManagedAddress)
            {
                merged.Add(CliValueKind.ManagedAddress);
            }
            else
            {
                return null;
            }
        }
        return merged.MoveToImmutable();
    }

    private void Apply(
        CilMethodBody body,
        CilInstruction instruction,
        List<CliValueKind> stack,
        List<ImplicitCoercion> coercions)
    {
        switch (instruction.Operation)
        {
            case CilOperation.Nop:
            case CilOperation.Branch:
                return;
            case CilOperation.LoadArgument:
                stack.Add(GetArgumentType(body, instruction));
                return;
            case CilOperation.LoadArgumentAddress:
            case CilOperation.LoadLocalAddress:
                stack.Add(CliValueKind.ManagedAddress);
                return;
            case CilOperation.StoreArgument:
                PopCoerced(body, instruction, stack, GetArgumentType(body, instruction), coercions);
                return;
            case CilOperation.LoadLocal:
                stack.Add(GetLocalType(body, instruction));
                return;
            case CilOperation.StoreLocal:
                PopCoerced(body, instruction, stack, GetLocalType(body, instruction), coercions);
                return;
            case CilOperation.LoadInt32:
            case CilOperation.LoadTypeToken:
                stack.Add(CliValueKind.I4);
                return;
            case CilOperation.LoadFieldToken:
                if (instruction.Operand is not CilOperand.FieldInstance)
                {
                    throw Invalid(body, instruction, "field token has no field operand");
                }
                stack.Add(CliValueKind.NativeInt);
                return;
            case CilOperation.LoadMethodToken:
                if (instruction.Operand is not CilOperand.MethodInstance)
                {
                    throw Invalid(body, instruction, "method token has no method operand");
                }
                stack.Add(CliValueKind.NativeInt);
                return;
            case CilOperation.LoadInt64:
                stack.Add(CliValueKind.I8);
                return;
            case CilOperation.LoadFloat32:
                stack.Add(CliValueKind.F4);
                return;
            case CilOperation.LoadFloat64:
                stack.Add(CliValueKind.F8);
                return;
            case CilOperation.LoadNull:
            case CilOperation.LoadString:
                stack.Add(CliValueKind.ManagedReference);
                return;
            case CilOperation.ConvertInt32:
            case CilOperation.ConvertInt32Unsigned:
                PopNumeric(body, instruction, stack);
                stack.Add(CliValueKind.I4);
                return;
            case CilOperation.ConvertInt64:
            case CilOperation.ConvertInt64Unsigned:
                // ECMA-335 Table III.8: an address converts to a native or 64-bit integer, which stops its tracking.
                if (Pop(body, instruction, stack) is not (CliValueKind.I4 or CliValueKind.I8 or CliValueKind.NativeInt or
                    CliValueKind.F4 or CliValueKind.F8 or CliValueKind.ManagedAddress))
                {
                    throw Invalid(body, instruction, "conversion to int64 requires a numeric or managed-address operand");
                }
                stack.Add(CliValueKind.I8);
                return;
            case CilOperation.ConvertNativeInt:
            case CilOperation.ConvertNativeUInt:
                PopNumericOrManagedAddress(body, instruction, stack);
                stack.Add(CliValueKind.NativeInt);
                return;
            case CilOperation.ConvertFloat32:
                PopNumeric(body, instruction, stack);
                stack.Add(CliValueKind.F4);
                return;
            case CilOperation.ConvertFloat64:
                PopNumeric(body, instruction, stack);
                stack.Add(CliValueKind.F8);
                return;
            case CilOperation.ConvertFloatUnsigned:
                PopInteger(body, instruction, stack);
                stack.Add(CliValueKind.F8);
                return;
            case CilOperation.CheckFinite:
                var finite = Pop(body, instruction, stack);
                if (finite is not (CliValueKind.F4 or CliValueKind.F8))
                {
                    throw Invalid(body, instruction, "ckfinite requires a floating value");
                }
                stack.Add(finite);
                return;
            case CilOperation.ConvertNumeric:
                PopNumeric(body, instruction, stack);
                var conversion = instruction.Operand as CilOperand.NumericConversion
                    ?? throw Invalid(body, instruction, "numeric conversion metadata is missing");
                stack.Add(conversion.Native
                    ? CliValueKind.NativeInt
                    : conversion.BitWidth <= 32 ? CliValueKind.I4 : CliValueKind.I8);
                return;
            case CilOperation.Duplicate:
                stack.Add(Peek(body, instruction, stack));
                return;
            case CilOperation.Pop:
                Pop(body, instruction, stack);
                return;
            case CilOperation.LoadField:
                {
                    var fieldType = GetFieldType(instruction);
                    PopFieldReceiver(body, instruction, stack);
                    stack.Add(fieldType);
                    return;
                }
            case CilOperation.LoadFieldAddress:
                PopFieldReceiver(body, instruction, stack);
                stack.Add(CliValueKind.ManagedAddress);
                return;
            case CilOperation.StoreField:
                {
                    PopCoerced(body, instruction, stack, GetFieldType(instruction), coercions);
                    PopFieldReceiver(body, instruction, stack);
                    return;
                }
            case CilOperation.LoadStaticField:
                stack.Add(GetFieldType(instruction));
                return;
            case CilOperation.LoadStaticFieldAddress:
                stack.Add(CliValueKind.ManagedAddress);
                return;
            case CilOperation.StoreStaticField:
                PopCoerced(body, instruction, stack, GetFieldType(instruction), coercions);
                return;
            case CilOperation.LoadObject:
                PopAddress(body, instruction, stack);
                stack.Add(GetSignatureType(instruction).StackKind);
                return;
            case CilOperation.StoreObject:
                PopExpected(body, instruction, stack, GetSignatureType(instruction).StackKind);
                PopAddress(body, instruction, stack);
                return;
            case CilOperation.CopyObject:
                PopAddress(body, instruction, stack);
                PopAddress(body, instruction, stack);
                return;
            case CilOperation.InitializeObject:
                PopAddress(body, instruction, stack);
                return;
            case CilOperation.SizeOf:
                stack.Add(CliValueKind.I4);
                return;
            case CilOperation.LocalAllocate:
                var allocationSize = Pop(body, instruction, stack);
                if (allocationSize is not (CliValueKind.I4 or CliValueKind.NativeInt))
                {
                    throw Invalid(
                        body,
                        instruction,
                        "localloc requires an int32 or native-integer size");
                }
                stack.Add(CliValueKind.NativeInt);
                return;
            case CilOperation.CopyBlock:
                PopBlockSize(body, instruction, stack);
                PopAddress(body, instruction, stack);
                PopAddress(body, instruction, stack);
                return;
            case CilOperation.InitializeBlock:
                PopBlockSize(body, instruction, stack);
                PopExpected(body, instruction, stack, CliValueKind.I4);
                PopAddress(body, instruction, stack);
                return;
            case CilOperation.InitializeArrayData:
                if (instruction.Operand is not CilOperand.ArrayInitializerData { Value.IsDefaultOrEmpty: false })
                {
                    throw Invalid(body, instruction, "array initializer data is empty");
                }
                PopExpected(body, instruction, stack, CliValueKind.ManagedReference);
                return;
            case CilOperation.CreateReadOnlySpanFromRva:
                if (instruction.Operand is not CilOperand.ReadOnlySpanData)
                {
                    throw Invalid(body, instruction, "RVA span creation metadata is missing");
                }
                stack.Add(CliValueKind.ValueType);
                return;
            case CilOperation.LoadStaticDataElement:
                var staticData = instruction.Operand as CilOperand.ReadOnlySpanData
                    ?? throw Invalid(
                        body,
                        instruction,
                        "static data element metadata is missing");
                PopExpected(body, instruction, stack, CliValueKind.I4);
                stack.Add(staticData.ElementType.StackKind);
                return;
            case CilOperation.DefaultValue:
                stack.Add(GetSignatureType(instruction).StackKind);
                return;
            case CilOperation.Add:
                stack.Add(PopAddPair(body, instruction, stack, coercions));
                return;
            case CilOperation.Subtract:
                stack.Add(PopSubtractPair(body, instruction, stack, coercions));
                return;
            case CilOperation.Multiply:
            case CilOperation.Divide:
            case CilOperation.Remainder:
                stack.Add(PopNumericPair(body, instruction, stack, coercions));
                return;
            case CilOperation.DivideUnsigned:
            case CilOperation.RemainderUnsigned:
                stack.Add(PopIntegerPair(body, instruction, stack, coercions));
                return;
            case CilOperation.AddCheckedUnsigned:
                stack.Add(PopOverflowPair(body, instruction, stack, addressOffset: true, addressDifference: false));
                return;
            case CilOperation.SubtractCheckedUnsigned:
                stack.Add(PopOverflowPair(body, instruction, stack, addressOffset: false, addressDifference: true));
                return;
            case CilOperation.AddChecked:
            case CilOperation.SubtractChecked:
            case CilOperation.MultiplyChecked:
            case CilOperation.MultiplyCheckedUnsigned:
                // The checked arithmetic's emitter widens an int32 by the check's signedness (see PopOverflowPair).
                stack.Add(PopIntegerPair(body, instruction, stack, coercions: null));
                return;
            case CilOperation.Negate:
                stack.Add(PopNumeric(body, instruction, stack));
                return;
            case CilOperation.OnesComplement:
                stack.Add(PopInteger(body, instruction, stack));
                return;
            case CilOperation.Select:
                stack.Add(PopSelect(body, instruction, stack));
                return;
            case CilOperation.BitwiseAnd:
            case CilOperation.BitwiseOr:
            case CilOperation.BitwiseXor:
                stack.Add(PopIntegerPair(body, instruction, stack, coercions));
                return;
            case CilOperation.ShiftLeft:
            case CilOperation.ShiftRightSigned:
            case CilOperation.ShiftRightUnsigned:
                stack.Add(PopShiftPair(body, instruction, stack, coercions));
                return;
            case CilOperation.CompareGreaterThanSigned:
            case CilOperation.CompareLessThanSigned:
            case CilOperation.CompareLessThanUnsigned:
                PopComparablePair(body, instruction, stack, coercions, objects: false);
                stack.Add(CliValueKind.I4);
                return;
            case CilOperation.CompareEqual:
            case CilOperation.CompareGreaterThanUnsigned:
                PopComparablePair(body, instruction, stack, coercions, objects: true);
                stack.Add(CliValueKind.I4);
                return;
            case CilOperation.BranchIfTrue:
            case CilOperation.BranchIfFalse:
                PopCondition(body, instruction, stack);
                return;
            case CilOperation.BranchIfEqual:
            case CilOperation.BranchIfNotEqual:
                PopComparablePair(body, instruction, stack, coercions, objects: true);
                return;
            case CilOperation.BranchIfGreaterThanSigned:
            case CilOperation.BranchIfGreaterThanUnsigned:
            case CilOperation.BranchIfGreaterThanOrEqualSigned:
            case CilOperation.BranchIfGreaterThanOrEqualUnsigned:
            case CilOperation.BranchIfLessThanSigned:
            case CilOperation.BranchIfLessThanUnsigned:
            case CilOperation.BranchIfLessThanOrEqualSigned:
            case CilOperation.BranchIfLessThanOrEqualUnsigned:
                PopComparablePair(body, instruction, stack, coercions, objects: false);
                return;
            case CilOperation.LoadFunction:
                stack.Add(CliValueKind.NativeInt);
                return;
            case CilOperation.LoadVirtualFunction:
                ApplyLoadVirtualFunction(body, instruction, stack);
                return;
            case CilOperation.CompareExchange:
                PopExpected(body, instruction, stack, GetSignatureType(instruction).StackKind);
                PopExpected(body, instruction, stack, GetSignatureType(instruction).StackKind);
                PopExpected(body, instruction, stack, CliValueKind.ManagedAddress);
                stack.Add(GetSignatureType(instruction).StackKind);
                return;
            case CilOperation.MaterializeType:
                PopExpected(body, instruction, stack, CliValueKind.I4);
                stack.Add(CliValueKind.ManagedReference);
                return;
            case CilOperation.GetTypeFacts:
                stack.Add(CliValueKind.NativeInt);
                return;
            case CilOperation.MaterializeMethod:
            case CilOperation.MaterializeField:
                if (instruction.Operand is not CilOperand.Index count)
                {
                    throw Invalid(
                        body,
                        instruction,
                        "member materialization has no arity operand");
                }
                var handleCount = count.Value;
                if (handleCount == 2)
                {
                    PopExpected(body, instruction, stack, CliValueKind.I4);
                }
                else if (handleCount != 1)
                {
                    throw Invalid(body, instruction, "member materialization arity is invalid");
                }
                PopExpected(body, instruction, stack, CliValueKind.NativeInt);
                stack.Add(CliValueKind.ManagedReference);
                return;
            case CilOperation.GetObjectType:
                PopExpected(
                    body,
                    instruction,
                    stack,
                    instruction.Operand is CilOperand.TypeIdentity
                        ? CliValueKind.ManagedAddress
                        : CliValueKind.ManagedReference);
                stack.Add(CliValueKind.ManagedReference);
                return;
            case CilOperation.Call:
            case CilOperation.CallVirtual:
                ApplyCall(body, instruction, stack, coercions);
                return;
            case CilOperation.CallIndirect:
                ApplyCallIndirect(body, instruction, stack, coercions);
                return;
            case CilOperation.NewObject:
                ApplyNewObject(body, instruction, stack, coercions);
                return;
            case CilOperation.Box:
                PopExpected(body, instruction, stack, GetSignatureType(instruction).StackKind);
                stack.Add(CliValueKind.ManagedReference);
                return;
            case CilOperation.Unbox:
                PopExpected(body, instruction, stack, CliValueKind.ManagedReference);
                stack.Add(CliValueKind.ManagedAddress);
                return;
            case CilOperation.UnboxAny:
                PopExpected(body, instruction, stack, CliValueKind.ManagedReference);
                stack.Add(GetSignatureType(instruction).StackKind);
                return;
            case CilOperation.Constrained:
                ValidateConstrainedPrefix(body, instruction);
                return;
            case CilOperation.Volatile:
                ValidateVolatilePrefix(body, instruction);
                return;
            case CilOperation.Readonly:
                ValidateReadonlyPrefix(body, instruction);
                return;
            case CilOperation.Unaligned:
                ValidateUnalignedPrefix(body, instruction);
                return;
            case CilOperation.Break:
                return;
            case CilOperation.NewArray:
                PopArrayLength(body, instruction, stack);
                stack.Add(CliValueKind.ManagedReference);
                return;
            case CilOperation.NewRectangularArray:
            case CilOperation.NewBoundedRectangularArray:
                if (instruction.Operation == CilOperation.NewBoundedRectangularArray)
                {
                    PopRectangularIndices(body, instruction, stack);
                }
                PopRectangularIndices(body, instruction, stack);
                stack.Add(CliValueKind.ManagedReference);
                return;
            case CilOperation.LoadArrayLength:
                PopExpected(body, instruction, stack, CliValueKind.ManagedReference);
                stack.Add(CliValueKind.I4);
                return;
            case CilOperation.LoadArrayElementReference:
                PopArrayIndex(body, instruction, stack);
                PopExpected(body, instruction, stack, CliValueKind.ManagedReference);
                stack.Add(CliValueKind.ManagedReference);
                return;
            case CilOperation.StoreArrayElementReference:
                PopExpected(body, instruction, stack, CliValueKind.ManagedReference);
                PopArrayIndex(body, instruction, stack);
                PopExpected(body, instruction, stack, CliValueKind.ManagedReference);
                return;
            case CilOperation.LoadArrayElement:
                PopArrayIndex(body, instruction, stack);
                PopExpected(body, instruction, stack, CliValueKind.ManagedReference);
                stack.Add(GetSignatureType(instruction).StackKind);
                return;
            case CilOperation.LoadArrayElementAddress:
                PopArrayIndex(body, instruction, stack);
                PopExpected(body, instruction, stack, CliValueKind.ManagedReference);
                stack.Add(CliValueKind.ManagedAddress);
                return;
            case CilOperation.StoreArrayElement:
                PopExpected(body, instruction, stack, GetSignatureType(instruction).StackKind);
                PopArrayIndex(body, instruction, stack);
                PopExpected(body, instruction, stack, CliValueKind.ManagedReference);
                return;
            case CilOperation.LoadRectangularArrayElement:
                PopRectangularIndices(body, instruction, stack);
                PopExpected(body, instruction, stack, CliValueKind.ManagedReference);
                stack.Add(GetRectangularArrayType(instruction).ElementType!.StackKind);
                return;
            case CilOperation.StoreRectangularArrayElement:
                PopExpected(
                    body,
                    instruction,
                    stack,
                    GetRectangularArrayType(instruction).ElementType!.StackKind);
                PopRectangularIndices(body, instruction, stack);
                PopExpected(body, instruction, stack, CliValueKind.ManagedReference);
                return;
            case CilOperation.LoadRectangularArrayElementAddress:
                PopRectangularIndices(body, instruction, stack);
                PopExpected(body, instruction, stack, CliValueKind.ManagedReference);
                stack.Add(CliValueKind.ManagedAddress);
                return;
            case CilOperation.CastClass:
            case CilOperation.IsInstance:
                PopExpected(body, instruction, stack, CliValueKind.ManagedReference);
                stack.Add(CliValueKind.ManagedReference);
                return;
            case CilOperation.Throw:
                PopExpected(body, instruction, stack, CliValueKind.ManagedReference);
                stack.Clear();
                return;
            // rethrow leaves the handler like throw and takes nothing from the stack (ECMA-335
            // III.4.24), so values on it are discarded: Roslyn keeps a value for the branch that
            // does not rethrow, as in `x ?? throw` inside a catch (dup; brtrue; rethrow).
            case CilOperation.Rethrow:
                stack.Clear();
                return;
            case CilOperation.EndFinally:
                RequireEmpty(body, instruction, stack);
                return;
            case CilOperation.Leave:
                // leave empties the evaluation stack (ECMA-335 III.3.46).
                stack.Clear();
                return;
            case CilOperation.EndFilter:
                PopExpected(body, instruction, stack, CliValueKind.I4);
                RequireEmpty(body, instruction, stack);
                return;
            case CilOperation.Return:
                ApplyReturn(body, instruction, stack, coercions);
                return;
            default:
                throw new InvalidOperationException(
                    $"Unhandled supported CIL operation {instruction.Operation}.");
        }
    }

    private void ApplyCallIndirect(
        CilMethodBody body,
        CilInstruction instruction,
        List<CliValueKind> stack,
        List<ImplicitCoercion> coercions)
    {
        var signature = instruction.Operand is CilOperand.CallSite callSite
            ? callSite.Signature
            : throw Invalid(body, instruction, "calli requires a call-site signature");
        PopExpected(body, instruction, stack, CliValueKind.NativeInt);
        for (var index = signature.ParameterTypes.Length - 1; index >= 0; index--)
        {
            PopCoerced(body, instruction, stack, signature.ParameterTypes[index], coercions);
        }
        if (signature.ReturnType != CliValueKind.Void)
        {
            stack.Add(signature.ReturnType);
        }
    }

    private void ApplyLoadVirtualFunction(
        CilMethodBody body,
        CilInstruction instruction,
        List<CliValueKind> stack)
    {
        var (target, _) = GetMethodTarget(instruction);
        if (target.IsStatic || !target.IsVirtual)
        {
            throw Invalid(body, instruction, "ldvirtftn requires a virtual instance method");
        }
        PopExpected(body, instruction, stack, CliValueKind.ManagedReference);
        stack.Add(CliValueKind.NativeInt);
    }

    private void ApplyCall(
        CilMethodBody body,
        CilInstruction instruction,
        List<CliValueKind> stack,
        List<ImplicitCoercion> coercions)
    {
        (var target, var signature) =
            GetMethodTarget(instruction);
        PopParameters(body, instruction, stack, signature.ParameterTypes, coercions);
        if (!target.IsStatic)
        {
            var receiver = IsConstrainedCall(body, instruction)
                ? CliValueKind.ManagedAddress
                : instruction.Operand is CilOperand.MethodInstance instance
                    ? instance.Value.DeclaringType.IsValueType
                        ? CliValueKind.ManagedAddress
                        : CliValueKind.ManagedReference
                    : _types.GetTypeDefinition(target.DeclaringType).IsValueType
                    ? CliValueKind.ManagedAddress
                    : CliValueKind.ManagedReference;
            PopExpected(body, instruction, stack, receiver);
        }
        if (instruction.Operation == CilOperation.CallVirtual && target.IsStatic)
        {
            throw Invalid(body, instruction, "callvirt targets a static method");
        }
        if (signature.ReturnType != CliValueKind.Void)
        {
            stack.Add(signature.ReturnType);
        }
    }

    private void ApplyNewObject(
        CilMethodBody body,
        CilInstruction instruction,
        List<CliValueKind> stack,
        List<ImplicitCoercion> coercions)
    {
        (var constructor, var signature) =
            GetMethodTarget(instruction);
        if (constructor.IsStatic || constructor.Name != ".ctor")
        {
            throw Invalid(body, instruction, "newobj does not target an instance constructor");
        }
        PopParameters(body, instruction, stack, signature.ParameterTypes, coercions);
        stack.Add(GetConstructedStackKind(instruction, constructor));
    }

    private void ApplyReturn(
        CilMethodBody body,
        CilInstruction instruction,
        List<CliValueKind> stack,
        List<ImplicitCoercion> coercions)
    {
        var signature = body.MethodInstance?.Signature ?? body.Method.Signature;
        if (signature.ReturnType != CliValueKind.Void)
        {
            PopCoerced(body, instruction, stack, signature.ReturnType, coercions);
        }
        if (stack.Count != 0)
        {
            throw Invalid(body, instruction, "evaluation stack is not empty at return");
        }
    }

    private CliValueKind GetArgumentType(
        CilMethodBody body,
        CilInstruction instruction)
    {
        var index = GetIndex(instruction);
        var method = body.Method;
        var signature = body.MethodInstance?.Signature ?? method.Signature;
        if (!method.IsStatic)
        {
            if (index == 0)
            {
                return _types.GetTypeDefinition(method.DeclaringType).IsValueType
                    ? CliValueKind.ManagedAddress
                    : CliValueKind.ManagedReference;
            }
            index--;
        }
        if ((uint)index >= (uint)signature.ParameterTypes.Length)
        {
            throw Invalid(body, instruction, $"invalid argument index {index}");
        }
        return signature.ParameterTypes[index];
    }

    private static CliValueKind GetLocalType(CilMethodBody body, CilInstruction instruction)
    {
        var index = GetIndex(instruction);
        if ((uint)index >= (uint)body.Locals.Length)
        {
            throw Invalid(body, instruction, $"invalid local index {index}");
        }
        return body.Locals[index];
    }

    private static int GetIndex(CilInstruction instruction) =>
        ((CilOperand.Index)instruction.Operand).Value;

    private CliValueKind GetFieldType(CilInstruction instruction) =>
        instruction.Operand switch
        {
            CilOperand.Entity entity => _fields.GetField(entity.Key).FieldType,
            CilOperand.FieldInstance instance => instance.Value.FieldType.StackKind,
            _ => throw new InvalidOperationException("field instruction has no field operand"),
        };

    private (MethodDefinitionModel Definition, MethodSignatureModel Signature)
        GetMethodTarget(CilInstruction instruction) => instruction.Operand switch
        {
            CilOperand.Entity entity =>
                (_methods.GetMethod(entity.Key), _methods.GetMethod(entity.Key).Signature),
            CilOperand.MethodInstance instance =>
                (instance.Value.Definition, instance.Value.Signature),
            _ => throw new InvalidOperationException("call instruction has no method operand"),
        };

    private CliValueKind GetConstructedStackKind(
        CilInstruction instruction,
        MethodDefinitionModel constructor)
    {
        var type = instruction.Operand is CilOperand.MethodInstance instance
            ? instance.Value.DeclaringType
            : ToIdentity(_types.GetTypeDefinition(constructor.DeclaringType));
        return type.StackKind;
    }

    private static CliTypeIdentity ToIdentity(TypeDefinitionModel type) =>
        CliTypeIdentity.Named(
            type.Key.Assembly,
            type.Namespace,
            type.Name,
            type.IsValueType);

    private static CliTypeIdentity GetSignatureType(CilInstruction instruction) =>
        instruction.Operand is CilOperand.TypeIdentity type
            ? type.Value
            : throw new InvalidOperationException("instruction has no signature type operand");

    private static CliTypeIdentity GetRectangularArrayType(CilInstruction instruction)
    {
        var type = GetSignatureType(instruction);
        return type.Shape == CliTypeShape.Array
            ? type
            : throw new InvalidOperationException(
                "rectangular-array instruction has no array type operand");
    }

    private void PopRectangularIndices(
        CilMethodBody body,
        CilInstruction instruction,
        List<CliValueKind> stack)
    {
        var rank = GetRectangularArrayType(instruction).ArrayRank;
        for (var dimension = 0; dimension < rank; dimension++)
        {
            PopExpected(body, instruction, stack, CliValueKind.I4);
        }
    }

    private static void PopFieldReceiver(
        CilMethodBody body,
        CilInstruction instruction,
        List<CliValueKind> stack)
    {
        var actual = Pop(body, instruction, stack);
        if (actual is not (CliValueKind.ManagedReference or CliValueKind.ManagedAddress or
                CliValueKind.NativeInt or CliValueKind.ValueType or CliValueKind.V128))
        {
            throw Invalid(
                body,
                instruction,
                $"expected managed receiver but found {actual}");
        }
    }

    private static void ValidateConstrainedPrefix(
        CilMethodBody body,
        CilInstruction instruction)
    {
        // ECMA-335 III.2.1 as augmented for static virtual methods: call, callvirt or ldftn. A GetObjectType the
        // compiler made of a constrained GetType carries the constrained type itself, without the prefix, which the
        // call after it would otherwise take.
        var index = IndexOf(body, instruction);
        if (index + 1 >= body.Instructions.Length ||
            body.Instructions[index + 1].Operation is not (
                CilOperation.Call or CilOperation.CallVirtual or CilOperation.LoadFunction))
        {
            throw Invalid(
                body,
                instruction,
                "constrained. must immediately precede call, callvirt or ldftn");
        }
    }

    private static void ValidateVolatilePrefix(
        CilMethodBody body,
        CilInstruction instruction)
    {
        var index = IndexOf(body, instruction);
        if (index + 1 >= body.Instructions.Length ||
            body.Instructions[index + 1].Operation is not (
                CilOperation.LoadField or CilOperation.StoreField or
                CilOperation.LoadStaticField or CilOperation.StoreStaticField or
                CilOperation.LoadObject or CilOperation.StoreObject or
                CilOperation.CopyBlock or CilOperation.InitializeBlock))
        {
            throw Invalid(
                body,
                instruction,
                "volatile. must immediately precede a supported memory access");
        }
    }

    private static void ValidateReadonlyPrefix(
        CilMethodBody body,
        CilInstruction instruction)
    {
        var index = IndexOf(body, instruction);
        var next = body.Instructions[index + 1];
        // ECMA-335 III.2.3 also permits the runtime-provided array Address
        // call before it is lowered to our rectangular-address operation.
        var arrayAddressCall = next.Operation == CilOperation.Call &&
            next.Operand is CilOperand.MethodInstance method &&
            method.Value.DeclaringType.Shape == CliTypeShape.Array &&
            method.Value.Definition.Name == "Address" &&
            !method.Value.Definition.IsStatic;
        if (next.Operation is not (
                CilOperation.LoadArrayElementAddress or
                CilOperation.LoadRectangularArrayElementAddress) && !arrayAddressCall)
        {
            throw Invalid(
                body,
                instruction,
                "readonly. must immediately precede an array element address load");
        }
    }

    private static void ValidateUnalignedPrefix(
        CilMethodBody body,
        CilInstruction instruction)
    {
        if (instruction.Operand is not CilOperand.Index { Value: 1 or 2 or 4 })
        {
            throw Invalid(body, instruction, "unaligned. alignment must be 1, 2, or 4");
        }
        var index = IndexOf(body, instruction);
        if (index + 1 >= body.Instructions.Length ||
            body.Instructions[index + 1].Operation is not (
                CilOperation.LoadField or CilOperation.LoadFieldAddress or
                CilOperation.StoreField or CilOperation.LoadStaticField or
                CilOperation.LoadStaticFieldAddress or CilOperation.StoreStaticField or
                CilOperation.LoadObject or CilOperation.StoreObject or
                CilOperation.CopyBlock or CilOperation.InitializeBlock))
        {
            throw Invalid(
                body,
                instruction,
                "unaligned. must immediately precede a supported memory access");
        }
    }

    // An instruction's index, or -1, by a binary search: a body's instructions are in increasing offset order and an
    // offset is part of an instruction's identity. A scan for each prefix and call made a method of N calls cost N
    // squared.
    private static int IndexOf(CilMethodBody body, CilInstruction instruction)
    {
        var instructions = body.Instructions;
        var low = 0;
        var high = instructions.Length - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) >> 1);
            var at = instructions[middle].Offset;
            if (at == instruction.Offset) return instructions[middle] == instruction ? middle : -1;
            if (at < instruction.Offset) low = middle + 1;
            else high = middle - 1;
        }
        return -1;
    }

    private static bool IsConstrainedCall(
        CilMethodBody body,
        CilInstruction instruction)
    {
        var index = IndexOf(body, instruction);
        // A non-static call has a receiver on the evaluation stack, so a
        // validated call instruction cannot be the method's first instruction.
        return body.Instructions[index - 1].Operation == CilOperation.Constrained;
    }

    private void PopParameters(
        CilMethodBody body,
        CilInstruction instruction,
        List<CliValueKind> stack,
        ImmutableArray<CliValueKind> parameterTypes,
        List<ImplicitCoercion> coercions)
    {
        for (var index = parameterTypes.Length - 1; index >= 0; index--)
        {
            PopCoerced(body, instruction, stack, parameterTypes[index], coercions);
        }
    }

    private static void PopCondition(
        CilMethodBody body,
        CilInstruction instruction,
        List<CliValueKind> stack)
    {
        var value = Pop(body, instruction, stack);
        if (value is not (
                CliValueKind.I4 or CliValueKind.I8 or CliValueKind.NativeInt or
                CliValueKind.ManagedReference or CliValueKind.ManagedAddress))
        {
            throw Invalid(body, instruction, "branch condition is not an integer or reference");
        }
    }

    // ECMA-335 Table III.4: numbers that combine in arithmetic (an int32 with a native int, a float with a float),
    // an address with an address or a native int, and for equality and cgt.un (a reference against null) two object
    // references.
    private static void PopComparablePair(
        CilMethodBody body,
        CilInstruction instruction,
        List<CliValueKind> stack,
        List<ImplicitCoercion> coercions,
        bool objects)
    {
        var rightIndex = stack.Count - 1;
        var right = Pop(body, instruction, stack);
        var left = Pop(body, instruction, stack);
        var compatible = (left, right) switch
        {
            (CliValueKind.ManagedAddress, CliValueKind.ManagedAddress or CliValueKind.NativeInt) or
                (CliValueKind.NativeInt, CliValueKind.ManagedAddress) => true,
            (CliValueKind.ManagedReference, CliValueKind.ManagedReference) => objects,
            _ => IsNumeric(left) && IsNumeric(right) && CombinedNumeric(left, right) is { } combined &&
                Coerced(instruction, rightIndex, left, right, combined, coercions),
        };
        if (!compatible)
        {
            throw Invalid(
                body,
                instruction,
                $"comparison operands are incompatible: {left} and {right}");
        }
    }

    // Records the operand of a pair that is not of the kind the pair combines to (an int32 beside a native int in a
    // comparison, a float32 beside a float64), so the rewrite converts it and later passes see operands of one kind.
    // True.
    private static bool Coerced(
        CilInstruction instruction,
        int rightIndex,
        CliValueKind left,
        CliValueKind right,
        CliValueKind combined,
        List<ImplicitCoercion>? coercions)
    {
        if (left != combined) coercions?.Add(new(instruction.Offset, rightIndex - 1, left, combined));
        if (right != combined) coercions?.Add(new(instruction.Offset, rightIndex, right, combined));
        return true;
    }

    private static bool IsNumeric(CliValueKind value) =>
        value is CliValueKind.I4 or CliValueKind.I8 or CliValueKind.NativeInt or CliValueKind.F4 or CliValueKind.F8;

    // ECMA-335 Table III.2's numbers: equal kinds, an int32 with a native int (native int), and a float with a float,
    // float32 and float64 both F (float64 where they differ).
    private static CliValueKind? CombinedNumeric(CliValueKind left, CliValueKind right) => (left, right) switch
    {
        _ when left == right => left,
        (CliValueKind.NativeInt, CliValueKind.I4) or (CliValueKind.I4, CliValueKind.NativeInt) => CliValueKind.NativeInt,
        (CliValueKind.F4, CliValueKind.F8) or (CliValueKind.F8, CliValueKind.F4) => CliValueKind.F8,
        _ => null,
    };

    // ECMA-335 Table III.7: integers as Table III.5 combines them; add.ovf.un also offsets an address by an int32 or a
    // native int (either order), sub.ovf.un subtracts one from an address or an address from an address.
    private static CliValueKind PopOverflowPair(
        CilMethodBody body,
        CilInstruction instruction,
        List<CliValueKind> stack,
        bool addressOffset,
        bool addressDifference)
    {
        var right = Pop(body, instruction, stack);
        var left = Pop(body, instruction, stack);
        if (addressOffset && (left == CliValueKind.ManagedAddress && IsAddressOffset(right) ||
            right == CliValueKind.ManagedAddress && IsAddressOffset(left)) ||
            addressDifference && left == CliValueKind.ManagedAddress && IsAddressOffset(right))
        {
            return CliValueKind.ManagedAddress;
        }
        if (addressDifference && left == CliValueKind.ManagedAddress && right == CliValueKind.ManagedAddress)
        {
            return CliValueKind.NativeInt;
        }
        stack.Add(left);
        stack.Add(right);
        // An int32 beside a native int widens as the check's signedness does, zero-extended for the unsigned forms as
        // desktop's JIT widens it, which the checked arithmetic's emitter does; no conversion is recorded.
        return PopIntegerPair(body, instruction, stack, coercions: null);
    }

    private static CliValueKind PopNumericPair(
        CilMethodBody body,
        CilInstruction instruction,
        List<CliValueKind> stack,
        List<ImplicitCoercion>? coercions)
    {
        var rightIndex = stack.Count - 1;
        var right = PopNumeric(body, instruction, stack);
        var left = PopNumeric(body, instruction, stack);
        var combined = CombinedNumeric(left, right) ?? throw Invalid(
            body,
            instruction,
            $"numeric operands are incompatible: {left} and {right}");
        // Arithmetic widens an int32 beside a native int itself (Roslyn's `nint + 1` is ldc.i4.1; add); only a float32
        // beside a float64 is converted.
        if (combined == CliValueKind.F8)
        {
            Coerced(instruction, rightIndex, left, right, combined, coercions);
        }
        return combined;
    }

    // An int32 condition beneath it, two numbers of one kind: the result has that kind.
    private static CliValueKind PopSelect(
        CilMethodBody body,
        CilInstruction instruction,
        List<CliValueKind> stack)
    {
        var condition = Pop(body, instruction, stack);
        var whenFalse = PopNumeric(body, instruction, stack);
        var whenTrue = PopNumeric(body, instruction, stack);
        if (condition != CliValueKind.I4 || whenTrue != whenFalse)
        {
            throw Invalid(body, instruction,
                $"select takes two numbers of one kind and an int32 condition: {whenTrue}, {whenFalse}, {condition}");
        }
        return whenTrue;
    }

    private static CliValueKind PopAddPair(
        CilMethodBody body,
        CilInstruction instruction,
        List<CliValueKind> stack,
        List<ImplicitCoercion> coercions)
    {
        var right = Pop(body, instruction, stack);
        var left = Pop(body, instruction, stack);
        if (left == CliValueKind.ManagedAddress && IsAddressOffset(right) ||
            right == CliValueKind.ManagedAddress && IsAddressOffset(left))
        {
            return CliValueKind.ManagedAddress;
        }
        stack.Add(left);
        stack.Add(right);
        return PopNumericPair(body, instruction, stack, coercions);
    }

    private static CliValueKind PopSubtractPair(
        CilMethodBody body,
        CilInstruction instruction,
        List<CliValueKind> stack,
        List<ImplicitCoercion> coercions)
    {
        var right = Pop(body, instruction, stack);
        var left = Pop(body, instruction, stack);
        if (left == CliValueKind.ManagedAddress && IsAddressOffset(right))
        {
            return CliValueKind.ManagedAddress;
        }
        if (left == CliValueKind.ManagedAddress && right == CliValueKind.ManagedAddress)
        {
            return CliValueKind.NativeInt;
        }
        stack.Add(left);
        stack.Add(right);
        return PopNumericPair(body, instruction, stack, coercions);
    }

    private static bool IsAddressOffset(CliValueKind value) =>
        value is CliValueKind.I4 or CliValueKind.NativeInt;

    private static CliValueKind PopIntegerPair(
        CilMethodBody body,
        CilInstruction instruction,
        List<CliValueKind> stack,
        List<ImplicitCoercion>? coercions)
    {
        var type = PopNumericPair(body, instruction, stack, coercions);
        if (type is not (CliValueKind.I4 or CliValueKind.I8 or CliValueKind.NativeInt))
        {
            throw Invalid(body, instruction, "bitwise operation requires an integer operand");
        }
        return type;
    }

    private static CliValueKind PopInteger(
        CilMethodBody body,
        CilInstruction instruction,
        List<CliValueKind> stack)
    {
        var type = PopNumeric(body, instruction, stack);
        if (type is not (CliValueKind.I4 or CliValueKind.I8 or CliValueKind.NativeInt))
        {
            throw Invalid(body, instruction, "operation requires an integer operand");
        }
        return type;
    }

    private static CliValueKind PopShiftPair(
        CilMethodBody body,
        CilInstruction instruction,
        List<CliValueKind> stack,
        List<ImplicitCoercion> coercions)
    {
        var shiftIndex = stack.Count - 1;
        var shift = PopInteger(body, instruction, stack);
        if (shift is not (CliValueKind.I4 or CliValueKind.NativeInt))
        {
            throw Invalid(body, instruction, "shift count requires an int32 or native integer");
        }
        // A native int count narrows to the int32 every shift takes; a count past the value's width is unspecified.
        if (shift == CliValueKind.NativeInt)
        {
            coercions.Add(new(instruction.Offset, shiftIndex, shift, CliValueKind.I4));
        }
        return PopInteger(body, instruction, stack);
    }

    private static CliValueKind PopNumeric(
        CilMethodBody body,
        CilInstruction instruction,
        List<CliValueKind> stack)
    {
        var value = Pop(body, instruction, stack);
        if (value is not (CliValueKind.I4 or CliValueKind.I8 or CliValueKind.NativeInt or
                CliValueKind.F4 or CliValueKind.F8))
        {
            throw Invalid(body, instruction, "operation requires a numeric operand");
        }
        return value;
    }

    private static CliValueKind PopNumericOrManagedAddress(
        CilMethodBody body,
        CilInstruction instruction,
        List<CliValueKind> stack)
    {
        var value = Pop(body, instruction, stack);
        // An object reference converts too: compilers before C# 7.3 lower fixed (char* p = text) to a pinned string
        // local, conv.u and RuntimeHelpers.OffsetToStringData, as CoreCLR accepts.
        if (value is not (CliValueKind.I4 or CliValueKind.I8 or
                CliValueKind.NativeInt or CliValueKind.F4 or CliValueKind.F8 or
                CliValueKind.ManagedAddress or CliValueKind.ManagedReference))
        {
            throw Invalid(
                body,
                instruction,
                "operation requires a numeric, managed-address or object operand");
        }
        return value;
    }

    private static void PopAddress(
        CilMethodBody body,
        CilInstruction instruction,
        List<CliValueKind> stack)
    {
        var value = Pop(body, instruction, stack);
        if (value is not (CliValueKind.ManagedAddress or CliValueKind.NativeInt))
        {
            throw Invalid(body, instruction, "operation requires an address operand");
        }
    }

    private static void PopBlockSize(
        CilMethodBody body,
        CilInstruction instruction,
        List<CliValueKind> stack)
    {
        var value = Pop(body, instruction, stack);
        if (value is not (CliValueKind.I4 or CliValueKind.NativeInt))
        {
            throw Invalid(body, instruction, "block size must be int32 or native integer");
        }
    }

    private static void PopArrayLength(
            CilMethodBody body,
            CilInstruction instruction,
            List<CliValueKind> stack) =>
        PopArrayNativeInteger(body, instruction, stack, "array length");

    private static void PopArrayIndex(
            CilMethodBody body,
            CilInstruction instruction,
            List<CliValueKind> stack) =>
        PopArrayNativeInteger(body, instruction, stack, "array index");

    private static void PopArrayNativeInteger(
            CilMethodBody body,
            CilInstruction instruction,
            List<CliValueKind> stack, string role)
    {
        var value = Pop(body, instruction, stack);
        if (value is not (CliValueKind.I4 or CliValueKind.NativeInt))
        {
            throw Invalid(
                body,
                instruction,
                $"{role} must be int32 or native integer");
        }
    }

    // A value an argument, a store to a local, argument or field, or a return takes: one of int32
    // and native int coerces to the other (ECMA-335 III.1.6), which is recorded for the rewrite
    // that makes it explicit.
    private void PopCoerced(
        CilMethodBody body,
        CilInstruction instruction,
        List<CliValueKind> stack,
        CliValueKind expected,
        List<ImplicitCoercion> coercions)
    {
        var actual = Peek(body, instruction, stack);
        if ((expected, actual) is (CliValueKind.I4, CliValueKind.NativeInt) or (CliValueKind.NativeInt, CliValueKind.I4))
        {
            stack.RemoveAt(stack.Count - 1);
            coercions.Add(new(instruction.Offset, stack.Count, actual, expected));
            return;
        }
        PopExpected(body, instruction, stack, expected);
    }

    private void PopExpected(
        CilMethodBody body,
        CilInstruction instruction,
        List<CliValueKind> stack,
        CliValueKind expected)
    {
        var actual = Pop(body, instruction, stack);
        if (!_stackTypeCompatibility.Accepts(expected, actual))
        {
            throw Invalid(
                body,
                instruction,
                $"evaluation stack type mismatch: expected {expected}, found {actual}");
        }
    }

    private static CliValueKind Pop(
        CilMethodBody body,
        CilInstruction instruction,
        List<CliValueKind> stack)
    {
        if (stack.Count == 0)
        {
            throw Invalid(body, instruction, "evaluation stack underflow");
        }
        var value = stack[^1];
        stack.RemoveAt(stack.Count - 1);
        return value;
    }

    private static CliValueKind Peek(
        CilMethodBody body,
        CilInstruction instruction,
        List<CliValueKind> stack)
    {
        if (stack.Count == 0)
        {
            throw Invalid(body, instruction, "evaluation stack underflow");
        }
        return stack[^1];
    }

    private static void RequireEmpty(
        CilMethodBody body,
        CilInstruction instruction,
        List<CliValueKind> stack)
    {
        if (stack.Count != 0)
        {
            throw Invalid(body, instruction, "evaluation stack is not empty at control transfer");
        }
    }

    private static ImmutableArray<CliValueKind> HandlerStack(CilExceptionRegion region) =>
        region.Kind is CilExceptionRegionKind.Catch or CilExceptionRegionKind.Filter
            ? [CliValueKind.ManagedReference]
            : [];

    private static CompilerException Invalid(
        CilMethodBody body,
        CilInstruction instruction,
        string message) => new(
        new CompilerDiagnostic(
            DiagnosticCode.InvalidCil,
            message,
            body.Method.Name,
            instruction.Offset));
}
