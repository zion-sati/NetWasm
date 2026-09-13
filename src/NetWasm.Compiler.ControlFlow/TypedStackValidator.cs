using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;

namespace NetWasm.Compiler.ControlFlow;

public sealed record ValidatedControlFlowGraph(
    ControlFlowGraph Graph,
    ImmutableDictionary<int, ImmutableArray<CliValueKind>> EntryStacks,
    ImmutableDictionary<int, ImmutableArray<CliValueKind>> InstructionEntryStacks);

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
                instructionEntryStacks.Add(instruction.Offset, [.. stack]);
                Apply(graph.MethodBody, instruction, stack);
                if (stack.Count > graph.MethodBody.MaxStack)
                {
                    throw Invalid(graph.MethodBody, instruction, "evaluation stack exceeds maxstack");
                }
            }

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
                    throw Invalid(
                        graph.MethodBody,
                        block.Terminator,
                        $"incompatible evaluation stack at merge into IL_{graph.GetBlock(successor).StartOffset:x4}");
                }
            }
        }

        return new ValidatedControlFlowGraph(
            graph,
            entryStacks.ToImmutableDictionary(),
            instructionEntryStacks.ToImmutableDictionary());

        void Seed(int offset, ImmutableArray<CliValueKind> stack)
        {
            var block = graph.GetBlockAtOffset(offset).Index;
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

    private void Apply(
        CilMethodBody body,
        CilInstruction instruction,
        List<CliValueKind> stack)
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
                PopExpected(body, instruction, stack, GetArgumentType(body, instruction));
                return;
            case CilOperation.LoadLocal:
                stack.Add(GetLocalType(body, instruction));
                return;
            case CilOperation.StoreLocal:
                PopExpected(body, instruction, stack, GetLocalType(body, instruction));
                return;
            case CilOperation.LoadInt32:
            case CilOperation.LoadTypeToken:
                stack.Add(CliValueKind.I4);
                return;
            case CilOperation.LoadFieldToken:
                _ = GetEntity(instruction);
                stack.Add(CliValueKind.I4);
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
                PopNumeric(body, instruction, stack);
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
                    PopExpected(body, instruction, stack, GetFieldType(instruction));
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
                PopExpected(
                    body,
                    instruction,
                    stack,
                    GetFieldType(instruction));
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
                if (instruction.Operand is not CilOperand.ByteData { Value.IsDefaultOrEmpty: false })
                {
                    throw Invalid(body, instruction, "array initializer data is empty");
                }
                PopExpected(body, instruction, stack, CliValueKind.ManagedReference);
                return;
            case CilOperation.DefaultValue:
                stack.Add(GetSignatureType(instruction).StackKind);
                return;
            case CilOperation.Add:
                stack.Add(PopAddPair(body, instruction, stack));
                return;
            case CilOperation.Subtract:
                stack.Add(PopSubtractPair(body, instruction, stack));
                return;
            case CilOperation.Multiply:
            case CilOperation.AddChecked:
            case CilOperation.AddCheckedUnsigned:
            case CilOperation.SubtractChecked:
            case CilOperation.SubtractCheckedUnsigned:
            case CilOperation.MultiplyChecked:
            case CilOperation.MultiplyCheckedUnsigned:
            case CilOperation.Divide:
            case CilOperation.DivideUnsigned:
            case CilOperation.Remainder:
            case CilOperation.RemainderUnsigned:
                stack.Add(PopNumericPair(body, instruction, stack));
                return;
            case CilOperation.Negate:
                stack.Add(PopNumeric(body, instruction, stack));
                return;
            case CilOperation.OnesComplement:
                stack.Add(PopInteger(body, instruction, stack));
                return;
            case CilOperation.BitwiseAnd:
            case CilOperation.BitwiseOr:
            case CilOperation.BitwiseXor:
                stack.Add(PopIntegerPair(body, instruction, stack));
                return;
            case CilOperation.ShiftLeft:
            case CilOperation.ShiftRightSigned:
            case CilOperation.ShiftRightUnsigned:
                stack.Add(PopShiftPair(body, instruction, stack));
                return;
            case CilOperation.CompareGreaterThanSigned:
            case CilOperation.CompareLessThanSigned:
                PopNumericPair(body, instruction, stack);
                stack.Add(CliValueKind.I4);
                return;
            case CilOperation.CompareEqual:
            case CilOperation.CompareGreaterThanUnsigned:
            case CilOperation.CompareLessThanUnsigned:
                PopComparablePair(body, instruction, stack);
                stack.Add(CliValueKind.I4);
                return;
            case CilOperation.BranchIfTrue:
            case CilOperation.BranchIfFalse:
                PopCondition(body, instruction, stack);
                return;
            case CilOperation.BranchIfEqual:
            case CilOperation.BranchIfNotEqual:
                PopComparablePair(body, instruction, stack);
                return;
            case CilOperation.BranchIfGreaterThanSigned:
            case CilOperation.BranchIfGreaterThanUnsigned:
            case CilOperation.BranchIfGreaterThanOrEqualSigned:
            case CilOperation.BranchIfGreaterThanOrEqualUnsigned:
            case CilOperation.BranchIfLessThanSigned:
            case CilOperation.BranchIfLessThanUnsigned:
            case CilOperation.BranchIfLessThanOrEqualSigned:
            case CilOperation.BranchIfLessThanOrEqualUnsigned:
                PopNumericPair(body, instruction, stack);
                return;
            case CilOperation.LoadFunction:
                stack.Add(CliValueKind.NativeInt);
                return;
            case CilOperation.LoadVirtualFunction:
                ApplyLoadVirtualFunction(body, instruction, stack);
                return;
            case CilOperation.DelegateCombine:
            case CilOperation.DelegateRemove:
                PopExpected(body, instruction, stack, CliValueKind.ManagedReference);
                PopExpected(body, instruction, stack, CliValueKind.ManagedReference);
                stack.Add(CliValueKind.ManagedReference);
                return;
            case CilOperation.DelegateEqual:
            case CilOperation.DelegateNotEqual:
                PopExpected(body, instruction, stack, CliValueKind.ManagedReference);
                PopExpected(body, instruction, stack, CliValueKind.ManagedReference);
                stack.Add(CliValueKind.I4);
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
                ApplyCall(body, instruction, stack);
                return;
            case CilOperation.CallIndirect:
                ApplyCallIndirect(body, instruction, stack);
                return;
            case CilOperation.NewObject:
                ApplyNewObject(body, instruction, stack);
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
            case CilOperation.Rethrow:
            case CilOperation.EndFinally:
                RequireEmpty(body, instruction, stack);
                return;
            case CilOperation.Leave:
                RequireEmpty(body, instruction, stack);
                return;
            case CilOperation.EndFilter:
                PopExpected(body, instruction, stack, CliValueKind.I4);
                RequireEmpty(body, instruction, stack);
                return;
            case CilOperation.Return:
                ApplyReturn(body, instruction, stack);
                return;
            default:
                throw new InvalidOperationException(
                    $"Unhandled supported CIL operation {instruction.Operation}.");
        }
    }

    private void ApplyCallIndirect(
        CilMethodBody body,
        CilInstruction instruction,
        List<CliValueKind> stack)
    {
        var signature = instruction.Operand is CilOperand.CallSite callSite
            ? callSite.Signature
            : throw Invalid(body, instruction, "calli requires a call-site signature");
        PopExpected(body, instruction, stack, CliValueKind.NativeInt);
        for (var index = signature.ParameterTypes.Length - 1; index >= 0; index--)
        {
            PopExpected(body, instruction, stack, signature.ParameterTypes[index]);
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
        List<CliValueKind> stack)
    {
        (var target, var signature) =
            GetMethodTarget(instruction);
        PopParameters(body, instruction, stack, signature.ParameterTypes);
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
        List<CliValueKind> stack)
    {
        (var constructor, var signature) =
            GetMethodTarget(instruction);
        if (constructor.IsStatic || constructor.Name != ".ctor")
        {
            throw Invalid(body, instruction, "newobj does not target an instance constructor");
        }
        PopParameters(body, instruction, stack, signature.ParameterTypes);
        stack.Add(GetConstructedStackKind(instruction, constructor));
    }

    private void ApplyReturn(
        CilMethodBody body,
        CilInstruction instruction,
        List<CliValueKind> stack)
    {
        var signature = body.MethodInstance?.Signature ?? body.Method.Signature;
        if (signature.ReturnType != CliValueKind.Void)
        {
            PopExpected(body, instruction, stack, signature.ReturnType);
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
                CliValueKind.NativeInt or CliValueKind.ValueType))
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
        var index = body.Instructions.IndexOf(instruction);
        if (index + 1 >= body.Instructions.Length ||
            body.Instructions[index + 1].Operation is not (
                CilOperation.Call or CilOperation.CallVirtual or
                CilOperation.GetObjectType))
        {
            throw Invalid(
                body,
                instruction,
                "constrained. must immediately precede call or callvirt");
        }
    }

    private static void ValidateVolatilePrefix(
        CilMethodBody body,
        CilInstruction instruction)
    {
        var index = body.Instructions.IndexOf(instruction);
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
        var index = body.Instructions.IndexOf(instruction);
        if (body.Instructions[index + 1].Operation != CilOperation.LoadArrayElementAddress)
        {
            throw Invalid(
                body,
                instruction,
                "readonly. must immediately precede ldelema");
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
        var index = body.Instructions.IndexOf(instruction);
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

    private static bool IsConstrainedCall(
        CilMethodBody body,
        CilInstruction instruction)
    {
        var index = body.Instructions.IndexOf(instruction);
        // A non-static call has a receiver on the evaluation stack, so a
        // validated call instruction cannot be the method's first instruction.
        return body.Instructions[index - 1].Operation == CilOperation.Constrained;
    }

    private static EntityKey GetEntity(CilInstruction instruction) =>
        ((CilOperand.Entity)instruction.Operand).Key;

    private void PopParameters(
        CilMethodBody body,
        CilInstruction instruction,
        List<CliValueKind> stack,
        ImmutableArray<CliValueKind> parameterTypes)
    {
        for (var index = parameterTypes.Length - 1; index >= 0; index--)
        {
            PopExpected(body, instruction, stack, parameterTypes[index]);
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

    private void PopComparablePair(
        CilMethodBody body,
        CilInstruction instruction,
        List<CliValueKind> stack)
    {
        var right = Pop(body, instruction, stack);
        var left = Pop(body, instruction, stack);
        var compatible = _stackTypeCompatibility.Accepts(left, right) ||
            _stackTypeCompatibility.Accepts(right, left);
        if (!compatible || left is not (
                CliValueKind.I4 or
                CliValueKind.I8 or
                CliValueKind.NativeInt or
                CliValueKind.F4 or
                CliValueKind.F8 or
                CliValueKind.ManagedReference or
                CliValueKind.ManagedAddress))
        {
            throw Invalid(
                body,
                instruction,
                $"comparison operands are incompatible: {left} and {right}");
        }
    }

    private static CliValueKind PopNumericPair(
        CilMethodBody body,
        CilInstruction instruction,
        List<CliValueKind> stack)
    {
        var right = PopNumeric(body, instruction, stack);
        var left = PopNumeric(body, instruction, stack);
        if (left == right)
        {
            return left;
        }
        return (left, right) switch
        {
            (CliValueKind.NativeInt, CliValueKind.I4) or
                (CliValueKind.I4, CliValueKind.NativeInt) => CliValueKind.NativeInt,
            _ => throw Invalid(
                body,
                instruction,
                $"numeric operands are incompatible: {left} and {right}"),
        };
    }

    private static CliValueKind PopAddPair(
        CilMethodBody body,
        CilInstruction instruction,
        List<CliValueKind> stack)
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
        return PopNumericPair(body, instruction, stack);
    }

    private static CliValueKind PopSubtractPair(
        CilMethodBody body,
        CilInstruction instruction,
        List<CliValueKind> stack)
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
        return PopNumericPair(body, instruction, stack);
    }

    private static bool IsAddressOffset(CliValueKind value) =>
        value is CliValueKind.I4 or CliValueKind.NativeInt;

    private static CliValueKind PopIntegerPair(
        CilMethodBody body,
        CilInstruction instruction,
        List<CliValueKind> stack)
    {
        var type = PopNumericPair(body, instruction, stack);
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
        List<CliValueKind> stack)
    {
        var shift = PopInteger(body, instruction, stack);
        if (shift is not (CliValueKind.I4 or CliValueKind.NativeInt))
        {
            throw Invalid(body, instruction, "shift count requires an int32 or native integer");
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
        if (value is not (CliValueKind.I4 or CliValueKind.I8 or
                CliValueKind.NativeInt or CliValueKind.F4 or CliValueKind.F8 or
                CliValueKind.ManagedAddress))
        {
            throw Invalid(
                body,
                instruction,
                "operation requires a numeric or managed-address operand");
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
