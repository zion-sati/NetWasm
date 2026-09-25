using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Correctness;

internal sealed class GeneratedCilValidator : IGeneratedCilValidator
{
    private static readonly HashSet<CilOperation> BinaryOperations =
    [
        CilOperation.Add,
        CilOperation.Subtract,
        CilOperation.Multiply,
        CilOperation.AddChecked,
        CilOperation.AddCheckedUnsigned,
        CilOperation.SubtractChecked,
        CilOperation.SubtractCheckedUnsigned,
        CilOperation.MultiplyChecked,
        CilOperation.MultiplyCheckedUnsigned,
        CilOperation.BitwiseAnd,
        CilOperation.BitwiseOr,
        CilOperation.BitwiseXor,
        CilOperation.ShiftLeft,
        CilOperation.ShiftRightSigned,
        CilOperation.ShiftRightUnsigned,
        CilOperation.Divide,
        CilOperation.DivideUnsigned,
        CilOperation.Remainder,
        CilOperation.RemainderUnsigned,
        CilOperation.CompareEqual,
        CilOperation.CompareGreaterThanSigned,
        CilOperation.CompareGreaterThanUnsigned,
        CilOperation.CompareLessThanSigned,
        CilOperation.CompareLessThanUnsigned,
    ];

    private static readonly HashSet<CilOperation> RelationalBranches =
    [
        CilOperation.BranchIfEqual,
        CilOperation.BranchIfNotEqual,
        CilOperation.BranchIfGreaterThanSigned,
        CilOperation.BranchIfGreaterThanUnsigned,
        CilOperation.BranchIfGreaterThanOrEqualSigned,
        CilOperation.BranchIfGreaterThanOrEqualUnsigned,
        CilOperation.BranchIfLessThanSigned,
        CilOperation.BranchIfLessThanUnsigned,
        CilOperation.BranchIfLessThanOrEqualSigned,
        CilOperation.BranchIfLessThanOrEqualUnsigned,
    ];

    private static readonly HashSet<CilOperation> Terminators =
    [
        CilOperation.Branch,
        CilOperation.BranchIfTrue,
        CilOperation.BranchIfFalse,
        .. RelationalBranches,
        CilOperation.Switch,
        CilOperation.Return,
        CilOperation.Throw,
        CilOperation.Rethrow,
        CilOperation.Leave,
        CilOperation.EndFilter,
        CilOperation.EndFinally,
    ];

    public void Validate(GeneratedCilProgram program) =>
        _ = MeasureMaximumStackDepth(program);

    public int MeasureMaximumStackDepth(GeneratedCilProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);
        if (program.ReturnType != CliValueKind.I4 ||
            !program.Arguments.AsSpan().SequenceEqual([CliValueKind.I4]) ||
            program.Locals.Any(kind => kind == CliValueKind.Void) ||
            program.Blocks.IsDefaultOrEmpty)
        {
            throw new InvalidDataException("generated CIL signature is unsupported");
        }

        var byId = new Dictionary<int, GeneratedCilBlock>();
        foreach (var block in program.Blocks)
        {
            if (!byId.TryAdd(block.Id, block))
            {
                throw new InvalidDataException($"generated block {block.Id} is duplicated");
            }
            if (block.EntryStack.IsDefault || block.ExceptionRegionPath.IsDefault ||
                block.Instructions.IsDefaultOrEmpty)
            {
                throw new InvalidDataException(
                    $"generated block {block.Id} has an invalid entry contract");
            }
        }

        ValidateExceptionRegions(program, byId);

        var entryStacks = new Dictionary<int, ImmutableArray<CliValueKind>>();
        var pending = new Queue<(int BlockId, ImmutableArray<CliValueKind> Stack)>();
        var maximumStackDepth = 0;
        pending.Enqueue((program.Blocks[0].Id, []));
        foreach (var region in program.ExceptionRegions)
        {
            if (region.FilterStartBlock is int filterStart)
            {
                pending.Enqueue((filterStart, [CliValueKind.ManagedReference]));
            }
            pending.Enqueue((
                region.HandlerStartBlock,
                region.Kind is CilExceptionRegionKind.Catch or
                    CilExceptionRegionKind.Filter
                    ? [CliValueKind.ManagedReference]
                    : []));
        }
        while (pending.TryDequeue(out var work))
        {
            if (entryStacks.TryGetValue(work.BlockId, out var existing))
            {
                if (!existing.AsSpan().SequenceEqual(work.Stack.AsSpan()))
                {
                    throw new InvalidDataException(
                        $"generated block {work.BlockId} has incompatible entry stacks");
                }
                continue;
            }
            if (!byId.TryGetValue(work.BlockId, out var block))
            {
                throw new InvalidDataException(
                    $"branch targets missing generated block {work.BlockId}");
            }
            if (!block.EntryStack.AsSpan().SequenceEqual(work.Stack.AsSpan()))
            {
                throw new InvalidDataException(
                    $"generated block {block.Id} declares an incompatible entry stack");
            }
            entryStacks.Add(block.Id, work.Stack);
            maximumStackDepth = Math.Max(
                maximumStackDepth,
                ValidateBlock(program, block, pending));
        }
        if (entryStacks.Count != program.Blocks.Length)
        {
            throw new InvalidDataException("generated CIL contains an unreachable block");
        }

        return maximumStackDepth;
    }

    private static int ValidateBlock(
        GeneratedCilProgram program,
        GeneratedCilBlock block,
        Queue<(int BlockId, ImmutableArray<CliValueKind> Stack)> successors)
    {
        var stack = new Stack<CliValueKind>(block.EntryStack);
        var maximumStackDepth = stack.Count;
        for (var index = 0; index < block.Instructions.Length; index++)
        {
            var instruction = block.Instructions[index];
            var terminal = index == block.Instructions.Length - 1;
            switch (instruction.Operation)
            {
                case CilOperation.Nop:
                    _ = Require<GeneratedCilOperand.None>(instruction);
                    break;
                case CilOperation.LoadArgument:
                    RequireIndex(instruction, 0);
                    stack.Push(CliValueKind.I4);
                    break;
                case CilOperation.LoadArgumentAddress:
                    RequireIndex(instruction, 0);
                    stack.Push(CliValueKind.ManagedAddress);
                    break;
                case CilOperation.LoadLocal:
                    stack.Push(LocalType(program, instruction));
                    break;
                case CilOperation.LoadLocalAddress:
                    _ = LocalType(program, instruction);
                    stack.Push(CliValueKind.ManagedAddress);
                    break;
                case CilOperation.StoreLocal:
                    RequireKind(
                        Pop(stack, block.Id),
                        LocalType(program, instruction),
                        block.Id);
                    break;
                case CilOperation.LoadInt32:
                    _ = Require<GeneratedCilOperand.Int32>(instruction);
                    stack.Push(CliValueKind.I4);
                    break;
                case CilOperation.LoadInt64:
                    _ = Require<GeneratedCilOperand.Int64>(instruction);
                    stack.Push(CliValueKind.I8);
                    break;
                case CilOperation.LoadFloat32:
                    _ = Require<GeneratedCilOperand.Float32>(instruction);
                    stack.Push(CliValueKind.F4);
                    break;
                case CilOperation.LoadFloat64:
                    _ = Require<GeneratedCilOperand.Float64>(instruction);
                    stack.Push(CliValueKind.F8);
                    break;
                case CilOperation.LoadNull:
                    _ = Require<GeneratedCilOperand.None>(instruction);
                    stack.Push(CliValueKind.ManagedReference);
                    break;
                case CilOperation.LoadString:
                    _ = RequireToken(instruction);
                    stack.Push(CliValueKind.ManagedReference);
                    break;
                case CilOperation.Pop:
                    _ = Require<GeneratedCilOperand.None>(instruction);
                    _ = Pop(stack, block.Id);
                    break;
                case CilOperation.Duplicate:
                    _ = Require<GeneratedCilOperand.None>(instruction);
                    stack.Push(Peek(stack, block.Id));
                    break;
                case CilOperation.StoreArgument:
                    RequireIndex(instruction, 0);
                    RequireKind(Pop(stack, block.Id), CliValueKind.I4, block.Id);
                    break;
                case CilOperation.LoadField:
                    var loadedField = RequireToken(instruction);
                    RequireKind(Pop(stack, block.Id), CliValueKind.ManagedReference,
                        block.Id);
                    stack.Push(loadedField.StackKind);
                    break;
                case CilOperation.LoadFieldAddress:
                    _ = RequireToken(instruction);
                    RequireKind(Pop(stack, block.Id), CliValueKind.ManagedReference,
                        block.Id);
                    stack.Push(CliValueKind.ManagedAddress);
                    break;
                case CilOperation.StoreField:
                    var storedField = RequireToken(instruction);
                    RequireKind(Pop(stack, block.Id), storedField.StackKind, block.Id);
                    RequireKind(Pop(stack, block.Id), CliValueKind.ManagedReference,
                        block.Id);
                    break;
                case CilOperation.LoadStaticField:
                    stack.Push(RequireToken(instruction).StackKind);
                    break;
                case CilOperation.LoadStaticFieldAddress:
                    _ = RequireToken(instruction);
                    stack.Push(CliValueKind.ManagedAddress);
                    break;
                case CilOperation.StoreStaticField:
                    var staticField = RequireToken(instruction);
                    RequireKind(Pop(stack, block.Id), staticField.StackKind, block.Id);
                    break;
                case CilOperation.LoadObject:
                    var loadedObject = RequireToken(instruction);
                    RequireKind(Pop(stack, block.Id), CliValueKind.ManagedAddress,
                        block.Id);
                    stack.Push(loadedObject.StackKind);
                    break;
                case CilOperation.StoreObject:
                    var storedObject = RequireToken(instruction);
                    RequireKind(Pop(stack, block.Id), storedObject.StackKind, block.Id);
                    RequireKind(Pop(stack, block.Id), CliValueKind.ManagedAddress,
                        block.Id);
                    break;
                case CilOperation.CopyObject:
                    _ = RequireToken(instruction);
                    RequireKind(Pop(stack, block.Id), CliValueKind.ManagedAddress,
                        block.Id);
                    RequireKind(Pop(stack, block.Id), CliValueKind.ManagedAddress,
                        block.Id);
                    break;
                case CilOperation.InitializeObject:
                    _ = RequireToken(instruction);
                    RequireKind(Pop(stack, block.Id), CliValueKind.ManagedAddress,
                        block.Id);
                    break;
                case CilOperation.Call or CilOperation.CallVirtual:
                    ApplyCall(
                        instruction,
                        stack,
                        block.Id,
                        index > 0 && block.Instructions[index - 1].Operation ==
                            CilOperation.Constrained);
                    break;
                case CilOperation.Constrained:
                    _ = RequireToken(instruction);
                    if (index == block.Instructions.Length - 1 ||
                        block.Instructions[index + 1].Operation is not (
                            CilOperation.Call or CilOperation.CallVirtual))
                    {
                        throw new InvalidDataException(
                            "generated constrained prefix does not precede call or callvirt");
                    }
                    break;
                case CilOperation.Unaligned:
                    if (instruction.Operand is not GeneratedCilOperand.Index
                        { Value: 1 or 2 or 4 } ||
                        index == block.Instructions.Length - 1 ||
                        block.Instructions[index + 1].Operation is not (
                            CilOperation.LoadField or CilOperation.StoreField or
                            CilOperation.LoadObject or CilOperation.StoreObject or
                            CilOperation.CopyBlock or CilOperation.InitializeBlock))
                    {
                        throw new InvalidDataException(
                            "generated unaligned prefix has invalid alignment or placement");
                    }
                    break;
                case CilOperation.Break:
                    break;
                case CilOperation.ConvertFloatUnsigned:
                    _ = PopInteger(stack, block.Id);
                    stack.Push(CliValueKind.F8);
                    break;
                case CilOperation.CheckFinite:
                    var finite = Pop(stack, block.Id);
                    if (finite is not (CliValueKind.F4 or CliValueKind.F8))
                    {
                        throw new InvalidDataException(
                            "generated ckfinite requires a floating value");
                    }
                    stack.Push(finite);
                    break;
                case CilOperation.LoadFunction:
                    _ = RequireToken(instruction);
                    stack.Push(CliValueKind.NativeInt);
                    break;
                case CilOperation.LoadVirtualFunction:
                    var virtualMethod = RequireToken(instruction);
                    if (virtualMethod.IsStatic || !virtualMethod.IsVirtual)
                    {
                        throw new InvalidDataException(
                            "generated ldvirtftn token is not a virtual instance method");
                    }
                    RequireKind(Pop(stack, block.Id), CliValueKind.ManagedReference,
                        block.Id);
                    stack.Push(CliValueKind.NativeInt);
                    break;
                case CilOperation.CallIndirect:
                    var callSite = RequireToken(instruction);
                    RequireKind(Pop(stack, block.Id), CliValueKind.NativeInt, block.Id);
                    PopParameters(stack, callSite.ParameterTypes, block.Id);
                    if (callSite.ReturnType != CliValueKind.Void)
                    {
                        stack.Push(callSite.ReturnType);
                    }
                    break;
                case CilOperation.NewObject:
                    var constructor = RequireToken(instruction);
                    if (!constructor.IsConstructor)
                    {
                        throw new InvalidDataException(
                            "generated newobj token is not a constructor");
                    }
                    PopParameters(stack, constructor.ParameterTypes, block.Id);
                    stack.Push(CliValueKind.ManagedReference);
                    break;
                case CilOperation.NewArray:
                    _ = RequireToken(instruction);
                    RequireKind(Pop(stack, block.Id), CliValueKind.I4, block.Id);
                    stack.Push(CliValueKind.ManagedReference);
                    break;
                case CilOperation.LoadArrayLength:
                    _ = Require<GeneratedCilOperand.None>(instruction);
                    RequireKind(Pop(stack, block.Id), CliValueKind.ManagedReference,
                        block.Id);
                    stack.Push(CliValueKind.I4);
                    break;
                case CilOperation.LoadArrayElement:
                    var loadedElement = RequireToken(instruction);
                    RequireKind(Pop(stack, block.Id), CliValueKind.I4, block.Id);
                    RequireKind(Pop(stack, block.Id), CliValueKind.ManagedReference,
                        block.Id);
                    stack.Push(loadedElement.StackKind);
                    break;
                case CilOperation.LoadArrayElementReference:
                    _ = Require<GeneratedCilOperand.None>(instruction);
                    RequireKind(Pop(stack, block.Id), CliValueKind.I4, block.Id);
                    RequireKind(Pop(stack, block.Id), CliValueKind.ManagedReference,
                        block.Id);
                    stack.Push(CliValueKind.ManagedReference);
                    break;
                case CilOperation.StoreArrayElementReference:
                    _ = Require<GeneratedCilOperand.None>(instruction);
                    RequireKind(Pop(stack, block.Id), CliValueKind.ManagedReference,
                        block.Id);
                    RequireKind(Pop(stack, block.Id), CliValueKind.I4, block.Id);
                    RequireKind(Pop(stack, block.Id), CliValueKind.ManagedReference,
                        block.Id);
                    break;
                case CilOperation.LoadArrayElementAddress:
                    _ = RequireToken(instruction);
                    RequireKind(Pop(stack, block.Id), CliValueKind.I4, block.Id);
                    RequireKind(Pop(stack, block.Id), CliValueKind.ManagedReference,
                        block.Id);
                    stack.Push(CliValueKind.ManagedAddress);
                    break;
                case CilOperation.StoreArrayElement:
                    var storedElement = RequireToken(instruction);
                    RequireKind(Pop(stack, block.Id), storedElement.StackKind, block.Id);
                    RequireKind(Pop(stack, block.Id), CliValueKind.I4, block.Id);
                    RequireKind(Pop(stack, block.Id), CliValueKind.ManagedReference,
                        block.Id);
                    break;
                case CilOperation.Box:
                    var boxed = RequireToken(instruction);
                    RequireKind(Pop(stack, block.Id), boxed.StackKind, block.Id);
                    stack.Push(CliValueKind.ManagedReference);
                    break;
                case CilOperation.UnboxAny:
                    var unboxed = RequireToken(instruction);
                    RequireKind(Pop(stack, block.Id), CliValueKind.ManagedReference,
                        block.Id);
                    stack.Push(unboxed.StackKind);
                    break;
                case CilOperation.Unbox:
                    _ = RequireToken(instruction);
                    RequireKind(Pop(stack, block.Id), CliValueKind.ManagedReference,
                        block.Id);
                    stack.Push(CliValueKind.ManagedAddress);
                    break;
                case CilOperation.CastClass or CilOperation.IsInstance:
                    _ = RequireToken(instruction);
                    RequireKind(Pop(stack, block.Id), CliValueKind.ManagedReference,
                        block.Id);
                    stack.Push(CliValueKind.ManagedReference);
                    break;
                case CilOperation.LoadTypeToken or CilOperation.LoadFieldToken or
                    CilOperation.SizeOf:
                    _ = RequireToken(instruction);
                    stack.Push(CliValueKind.I4);
                    break;
                case CilOperation.LocalAllocate:
                    _ = Require<GeneratedCilOperand.None>(instruction);
                    RequireKind(Pop(stack, block.Id), CliValueKind.I4, block.Id);
                    if (stack.Count != 0)
                    {
                        throw new InvalidDataException(
                            $"generated block {block.Id} emits localloc with a non-empty " +
                            "evaluation stack");
                    }
                    stack.Push(CliValueKind.NativeInt);
                    break;
                case CilOperation.CopyBlock:
                    _ = Require<GeneratedCilOperand.None>(instruction);
                    RequireKind(Pop(stack, block.Id), CliValueKind.I4, block.Id);
                    RequireAddress(Pop(stack, block.Id), block.Id);
                    RequireAddress(Pop(stack, block.Id), block.Id);
                    break;
                case CilOperation.InitializeBlock:
                    _ = Require<GeneratedCilOperand.None>(instruction);
                    RequireKind(Pop(stack, block.Id), CliValueKind.I4, block.Id);
                    RequireKind(Pop(stack, block.Id), CliValueKind.I4, block.Id);
                    RequireAddress(Pop(stack, block.Id), block.Id);
                    break;
                case CilOperation.Volatile:
                case CilOperation.Readonly:
                    _ = Require<GeneratedCilOperand.None>(instruction);
                    break;
                case CilOperation.Throw:
                    RequireTerminal(terminal, instruction);
                    RequireKind(
                        Pop(stack, block.Id),
                        CliValueKind.ManagedReference,
                        block.Id);
                    RequireEmpty(stack, block.Id, instruction.Operation);
                    break;
                case CilOperation.Rethrow:
                    RequireTerminal(terminal, instruction);
                    RequireEmpty(stack, block.Id, instruction.Operation);
                    RequireMembership(
                        block,
                        GeneratedExceptionRegionPart.Handler,
                        program,
                        CilExceptionRegionKind.Catch,
                        CilExceptionRegionKind.Filter);
                    break;
                case CilOperation.EndFilter:
                    RequireTerminal(terminal, instruction);
                    RequireKind(Pop(stack, block.Id), CliValueKind.I4, block.Id);
                    RequireEmpty(stack, block.Id, instruction.Operation);
                    RequireMembership(
                        block,
                        GeneratedExceptionRegionPart.Filter,
                        program,
                        CilExceptionRegionKind.Filter);
                    break;
                case CilOperation.EndFinally:
                    RequireTerminal(terminal, instruction);
                    RequireEmpty(stack, block.Id, instruction.Operation);
                    RequireMembership(
                        block,
                        GeneratedExceptionRegionPart.Handler,
                        program,
                        CilExceptionRegionKind.Finally,
                        CilExceptionRegionKind.Fault);
                    break;
                case var operation when BinaryOperations.Contains(operation):
                    RequireKind(Pop(stack, block.Id), CliValueKind.I4, block.Id);
                    RequireKind(Pop(stack, block.Id), CliValueKind.I4, block.Id);
                    stack.Push(CliValueKind.I4);
                    break;
                case CilOperation.Negate or CilOperation.OnesComplement:
                    RequireKind(Pop(stack, block.Id), CliValueKind.I4, block.Id);
                    stack.Push(CliValueKind.I4);
                    break;
                case CilOperation.ConvertInt32 or CilOperation.ConvertInt32Unsigned:
                    RequireNumeric(Pop(stack, block.Id), block.Id);
                    stack.Push(CliValueKind.I4);
                    break;
                case CilOperation.ConvertInt64 or CilOperation.ConvertInt64Unsigned:
                    RequireNumeric(Pop(stack, block.Id), block.Id);
                    stack.Push(CliValueKind.I8);
                    break;
                case CilOperation.ConvertNativeInt or CilOperation.ConvertNativeUInt:
                    RequireNumeric(Pop(stack, block.Id), block.Id);
                    stack.Push(CliValueKind.NativeInt);
                    break;
                case CilOperation.ConvertFloat32:
                    RequireNumeric(Pop(stack, block.Id), block.Id);
                    stack.Push(CliValueKind.F4);
                    break;
                case CilOperation.ConvertFloat64:
                    RequireNumeric(Pop(stack, block.Id), block.Id);
                    stack.Push(CliValueKind.F8);
                    break;
                case CilOperation.ConvertNumeric:
                    RequireNumeric(Pop(stack, block.Id), block.Id);
                    var conversion =
                        Require<GeneratedCilOperand.NumericConversion>(instruction);
                    stack.Push(conversion.Native
                        ? CliValueKind.NativeInt
                        : conversion.BitWidth <= 32
                            ? CliValueKind.I4
                            : CliValueKind.I8);
                    break;
                case CilOperation.Branch:
                    RequireTerminal(terminal, instruction);
                    EnqueueSuccessor(program, block, successors, instruction, stack);
                    break;
                case CilOperation.Leave:
                    RequireTerminal(terminal, instruction);
                    RequireEmpty(stack, block.Id, instruction.Operation);
                    EnqueueSuccessor(
                        program,
                        block,
                        successors,
                        instruction,
                        stack,
                        leave: true);
                    break;
                case CilOperation.BranchIfFalse or CilOperation.BranchIfTrue:
                    RequireTerminal(terminal, instruction);
                    RequireKind(Pop(stack, block.Id), CliValueKind.I4, block.Id);
                    EnqueueSuccessor(program, block, successors, instruction, stack);
                    EnqueueSuccessor(
                        program,
                        block,
                        successors,
                        NextBlock(program, block),
                        stack);
                    break;
                case var operation when RelationalBranches.Contains(operation):
                    RequireTerminal(terminal, instruction);
                    RequireKind(Pop(stack, block.Id), CliValueKind.I4, block.Id);
                    RequireKind(Pop(stack, block.Id), CliValueKind.I4, block.Id);
                    EnqueueSuccessor(program, block, successors, instruction, stack);
                    EnqueueSuccessor(
                        program,
                        block,
                        successors,
                        NextBlock(program, block),
                        stack);
                    break;
                case CilOperation.Switch:
                    RequireTerminal(terminal, instruction);
                    RequireKind(Pop(stack, block.Id), CliValueKind.I4, block.Id);
                    foreach (var target in
                             Require<GeneratedCilOperand.BlockTargets>(instruction).BlockIds)
                    {
                        EnqueueSuccessor(
                            program,
                            block,
                            successors,
                            target,
                            stack);
                    }
                    EnqueueSuccessor(
                        program,
                        block,
                        successors,
                        NextBlock(program, block),
                        stack);
                    break;
                case CilOperation.Return:
                    RequireTerminal(terminal, instruction);
                    if (!block.ExceptionRegionPath.IsEmpty)
                    {
                        throw new InvalidDataException(
                            $"generated block {block.Id} returns from an exception region");
                    }
                    RequireKind(Pop(stack, block.Id), program.ReturnType, block.Id);
                    break;
                default:
                    throw new InvalidDataException(
                        $"random generator emitted unsupported operation " +
                        $"{instruction.Operation}");
            }

            maximumStackDepth = Math.Max(maximumStackDepth, stack.Count);
        }
        if (!Terminators.Contains(block.Instructions[^1].Operation))
        {
            throw new InvalidDataException(
                $"generated block {block.Id} does not end with a terminator");
        }
        if (block.Instructions[^1].Operation == CilOperation.Return && stack.Count != 0)
        {
            throw new InvalidDataException(
                $"generated block {block.Id} exits with stack depth {stack.Count}");
        }

        return maximumStackDepth;
    }

    private static CliValueKind LocalType(
        GeneratedCilProgram program,
        GeneratedCilInstruction instruction)
    {
        var index = Require<GeneratedCilOperand.Index>(instruction).Value;
        if ((uint)index >= (uint)program.Locals.Length)
        {
            throw new InvalidDataException($"generated CIL has invalid local index {index}");
        }
        return program.Locals[index];
    }

    private static void ValidateExceptionRegions(
        GeneratedCilProgram program,
        Dictionary<int, GeneratedCilBlock> blocks)
    {
        var regions = new Dictionary<int, GeneratedCilExceptionRegion>();
        foreach (var region in program.ExceptionRegions)
        {
            if (!regions.TryAdd(region.Id, region))
            {
                throw new InvalidDataException(
                    $"generated exception region {region.Id} is duplicated");
            }
            var tryStart = BlockIndex(region.TryStartBlock);
            var tryEnd = BlockIndex(region.TryEndBlock);
            var handlerStart = BlockIndex(region.HandlerStartBlock);
            var handlerEnd = BlockIndex(region.HandlerEndBlock);
            if (tryStart >= tryEnd || handlerStart >= handlerEnd)
            {
                throw new InvalidDataException(
                    $"generated exception region {region.Id} has an empty range");
            }
            if (region.Kind == CilExceptionRegionKind.Filter)
            {
                if (region.FilterStartBlock is not int filterStartBlock ||
                    BlockIndex(filterStartBlock) >= handlerStart ||
                    region.CatchTypeToken is not null)
                {
                    throw new InvalidDataException(
                        $"generated filter region {region.Id} has invalid metadata");
                }
            }
            else if (region.FilterStartBlock is not null ||
                     (region.Kind == CilExceptionRegionKind.Catch) !=
                     (region.CatchTypeToken is not null))
            {
                throw new InvalidDataException(
                    $"generated exception region {region.Id} has invalid metadata");
            }
        }

        foreach (var block in program.Blocks)
        {
            var blockIndex = program.Blocks.IndexOf(block);
            var expected = program.ExceptionRegions
                .SelectMany(region => MembershipsAt(region, blockIndex))
                .OrderByDescending(item => item.Span)
                .ThenBy(item => item.Member.RegionId)
                .Select(item => item.Member)
                .ToImmutableArray();
            if (!block.ExceptionRegionPath.AsSpan().SequenceEqual(expected.AsSpan()))
            {
                throw new InvalidDataException(
                    $"generated block {block.Id} has an incorrect exception-region path");
            }
        }
        return;

        int BlockIndex(int blockId)
        {
            if (!blocks.TryGetValue(blockId, out var block))
            {
                throw new InvalidDataException(
                    $"generated exception region references missing block {blockId}");
            }
            return program.Blocks.IndexOf(block);
        }

        IEnumerable<(GeneratedExceptionRegionMembership Member, int Span)> MembershipsAt(
            GeneratedCilExceptionRegion region,
            int blockIndex)
        {
            var tryStart = BlockIndex(region.TryStartBlock);
            var tryEnd = BlockIndex(region.TryEndBlock);
            if (blockIndex >= tryStart && blockIndex < tryEnd)
            {
                yield return (
                    new(region.Id, GeneratedExceptionRegionPart.Try),
                    tryEnd - tryStart);
            }
            if (region.FilterStartBlock is int filterStartBlock)
            {
                var filterStart = BlockIndex(filterStartBlock);
                var filterEnd = BlockIndex(region.HandlerStartBlock);
                if (blockIndex >= filterStart && blockIndex < filterEnd)
                {
                    yield return (
                        new(region.Id, GeneratedExceptionRegionPart.Filter),
                        filterEnd - filterStart);
                }
            }
            var handlerStart = BlockIndex(region.HandlerStartBlock);
            var handlerEnd = BlockIndex(region.HandlerEndBlock);
            if (blockIndex >= handlerStart && blockIndex < handlerEnd)
            {
                yield return (
                    new(region.Id, GeneratedExceptionRegionPart.Handler),
                    handlerEnd - handlerStart);
            }
        }
    }

    private static void RequireEmpty(
        Stack<CliValueKind> stack,
        int blockId,
        CilOperation operation)
    {
        if (stack.Count != 0)
        {
            throw new InvalidDataException(
                $"generated block {blockId} has a non-empty stack at {operation}");
        }
    }

    private static void RequireMembership(
        GeneratedCilBlock block,
        GeneratedExceptionRegionPart part,
        GeneratedCilProgram program,
        params CilExceptionRegionKind[] kinds)
    {
        var matches = block.ExceptionRegionPath.Any(member =>
            member.Part == part && kinds.Contains(program.ExceptionRegions.Single(
                region => region.Id == member.RegionId).Kind));
        if (!matches)
        {
            throw new InvalidDataException(
                $"generated block {block.Id} has {block.Instructions[^1].Operation} " +
                "outside its required exception region");
        }
    }

    private static void EnqueueSuccessor(
        GeneratedCilProgram program,
        GeneratedCilBlock source,
        Queue<(int BlockId, ImmutableArray<CliValueKind> Stack)> successors,
        GeneratedCilInstruction instruction,
        Stack<CliValueKind> stack,
        bool leave = false) => EnqueueSuccessor(
        program,
        source,
        successors,
        Require<GeneratedCilOperand.BlockTarget>(instruction).BlockId,
        stack,
        leave);

    private static void EnqueueSuccessor(
        GeneratedCilProgram program,
        GeneratedCilBlock source,
        Queue<(int BlockId, ImmutableArray<CliValueKind> Stack)> successors,
        int blockId,
        Stack<CliValueKind> stack,
        bool leave = false)
    {
        var target = program.Blocks.SingleOrDefault(block => block.Id == blockId)
            ?? throw new InvalidDataException(
                $"branch targets missing generated block {blockId}");
        if (leave)
        {
            if (source.ExceptionRegionPath.Any(member =>
                    member.Part == GeneratedExceptionRegionPart.Filter ||
                    member.Part == GeneratedExceptionRegionPart.Handler &&
                    program.ExceptionRegions.Single(region =>
                        region.Id == member.RegionId).Kind is
                        CilExceptionRegionKind.Finally or
                        CilExceptionRegionKind.Fault) ||
                target.ExceptionRegionPath.Length >= source.ExceptionRegionPath.Length ||
                target.ExceptionRegionPath.Any(member =>
                    !source.ExceptionRegionPath.Contains(member)))
            {
                throw new InvalidDataException(
                    $"generated block {source.Id} has an illegal leave target");
            }
        }
        else if (!source.ExceptionRegionPath.AsSpan().SequenceEqual(
                     target.ExceptionRegionPath.AsSpan()))
        {
            throw new InvalidDataException(
                $"generated block {source.Id} crosses an exception region without leave");
        }
        successors.Enqueue((blockId, [.. stack.Reverse()]));
    }

    private static int NextBlock(GeneratedCilProgram program, GeneratedCilBlock block)
    {
        var index = program.Blocks.IndexOf(block);
        if (index < 0 || index == program.Blocks.Length - 1)
        {
            throw new InvalidDataException(
                $"generated block {block.Id} has no fallthrough successor");
        }
        return program.Blocks[index + 1].Id;
    }

    private static CliValueKind Pop(Stack<CliValueKind> stack, int blockId)
    {
        if (!stack.TryPop(out var kind))
        {
            throw new InvalidDataException(
                $"generated block {blockId} underflows its evaluation stack");
        }
        return kind;
    }

    private static CliValueKind Peek(Stack<CliValueKind> stack, int blockId)
    {
        if (!stack.TryPeek(out var kind))
        {
            throw new InvalidDataException(
                $"generated block {blockId} underflows its evaluation stack");
        }
        return kind;
    }

    private static void RequireKind(
        CliValueKind actual,
        CliValueKind expected,
        int blockId)
    {
        if (actual != expected)
        {
            throw new InvalidDataException(
                $"generated block {blockId} expected {expected} but found {actual}");
        }
    }

    private static void RequireNumeric(CliValueKind kind, int blockId)
    {
        if (kind is not (CliValueKind.I4 or CliValueKind.I8 or CliValueKind.NativeInt or
            CliValueKind.F4 or CliValueKind.F8))
        {
            throw new InvalidDataException(
                $"generated block {blockId} expected a numeric stack value but found {kind}");
        }
    }

    private static CliValueKind PopInteger(Stack<CliValueKind> stack, int blockId)
    {
        var kind = Pop(stack, blockId);
        if (kind is not (CliValueKind.I4 or CliValueKind.I8 or CliValueKind.NativeInt))
        {
            throw new InvalidDataException(
                $"generated block {blockId} expected an integer stack value but found {kind}");
        }
        return kind;
    }

    private static void RequireAddress(CliValueKind kind, int blockId)
    {
        if (kind is not (CliValueKind.ManagedAddress or CliValueKind.NativeInt))
        {
            throw new InvalidDataException(
                $"generated block {blockId} expected an address but found {kind}");
        }
    }

    private static void ApplyCall(
        GeneratedCilInstruction instruction,
        Stack<CliValueKind> stack,
        int blockId,
        bool constrained)
    {
        var method = RequireToken(instruction);
        PopParameters(stack, method.ParameterTypes, blockId);
        if (!method.IsStatic)
        {
            RequireKind(
                Pop(stack, blockId),
                constrained ? CliValueKind.ManagedAddress : CliValueKind.ManagedReference,
                blockId);
        }
        if (instruction.Operation == CilOperation.CallVirtual && method.IsStatic)
        {
            throw new InvalidDataException("generated callvirt token is static");
        }
        if (method.ReturnType != CliValueKind.Void)
        {
            stack.Push(method.ReturnType);
        }
    }

    private static void PopParameters(
        Stack<CliValueKind> stack,
        ImmutableArray<CliValueKind> parameters,
        int blockId)
    {
        for (var index = parameters.Length - 1; index >= 0; index--)
        {
            RequireKind(Pop(stack, blockId), parameters[index], blockId);
        }
    }

    private static GeneratedCilOperand.MetadataToken RequireToken(
        GeneratedCilInstruction instruction) =>
        Require<GeneratedCilOperand.MetadataToken>(instruction);

    private static void RequireTerminal(
        bool terminal,
        GeneratedCilInstruction instruction)
    {
        if (!terminal)
        {
            throw new InvalidDataException(
                $"{instruction.Operation} is not the generated block terminator");
        }
    }

    private static void RequireIndex(GeneratedCilInstruction instruction, int value)
    {
        if (Require<GeneratedCilOperand.Index>(instruction).Value != value)
        {
            throw new InvalidDataException(
                $"{instruction.Operation} uses an invalid generated index");
        }
    }

    private static T Require<T>(GeneratedCilInstruction instruction)
        where T : GeneratedCilOperand => instruction.Operand as T ??
            throw new InvalidDataException(
                $"{instruction.Operation} has operand {instruction.Operand.GetType().Name}, " +
                $"expected {typeof(T).Name}");
}
