// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata.RuntimeProvidedMembers.RuntimeAsync;

namespace NetWasm.Compiler.Metadata.Tests.RuntimeAsync;

// The stack depth before each instruction, which the runtime async lowering checks at its awaits: every operation it
// follows, the handler and filter starts, leave emptying the stack, and the bodies it gives up on.
public sealed class CilStackDepthsTests
{
    private static readonly AssemblyIdentity Assembly = new("stack-depths");
    private static readonly MethodDefinitionModel Definition = new(new(Assembly, 0x06000001), new(Assembly, 0x02000002), "M",
        true, new MethodSignatureModel(CliValueKind.Void, []), 1);

    // Each operation with the values it takes and gives; the generated body feeds it and drops what it gives.
    private static readonly (CilOperation Operation, int Pops, int Pushes)[] Effects =
    [
        (CilOperation.Nop, 0, 0), (CilOperation.Constrained, 0, 0), (CilOperation.Readonly, 0, 0), (CilOperation.Volatile, 0, 0),
        (CilOperation.Unaligned, 0, 0), (CilOperation.Break, 0, 0),
        (CilOperation.LoadArgument, 0, 1), (CilOperation.LoadArgumentAddress, 0, 1), (CilOperation.LoadLocal, 0, 1),
        (CilOperation.LoadLocalAddress, 0, 1), (CilOperation.LoadInt32, 0, 1), (CilOperation.LoadInt64, 0, 1),
        (CilOperation.LoadFloat32, 0, 1), (CilOperation.LoadFloat64, 0, 1), (CilOperation.LoadNull, 0, 1),
        (CilOperation.LoadString, 0, 1), (CilOperation.LoadStaticField, 0, 1), (CilOperation.LoadStaticFieldAddress, 0, 1),
        (CilOperation.SizeOf, 0, 1), (CilOperation.LoadTypeToken, 0, 1), (CilOperation.LoadFieldToken, 0, 1),
        (CilOperation.LoadMethodToken, 0, 1), (CilOperation.LoadFunction, 0, 1),
        (CilOperation.StoreArgument, 1, 0), (CilOperation.StoreLocal, 1, 0), (CilOperation.Pop, 1, 0),
        (CilOperation.StoreStaticField, 1, 0), (CilOperation.InitializeObject, 1, 0), (CilOperation.BranchIfTrue, 1, 0),
        (CilOperation.BranchIfFalse, 1, 0), (CilOperation.Switch, 1, 0),
        (CilOperation.Duplicate, 1, 2),
        (CilOperation.LoadField, 1, 1), (CilOperation.LoadFieldAddress, 1, 1), (CilOperation.LoadObject, 1, 1),
        (CilOperation.Box, 1, 1), (CilOperation.Unbox, 1, 1), (CilOperation.UnboxAny, 1, 1), (CilOperation.CastClass, 1, 1),
        (CilOperation.IsInstance, 1, 1), (CilOperation.LoadArrayLength, 1, 1), (CilOperation.NewArray, 1, 1),
        (CilOperation.Negate, 1, 1), (CilOperation.OnesComplement, 1, 1), (CilOperation.ConvertInt32, 1, 1),
        (CilOperation.ConvertInt32Unsigned, 1, 1), (CilOperation.ConvertInt64, 1, 1), (CilOperation.ConvertInt64Unsigned, 1, 1),
        (CilOperation.ConvertNativeInt, 1, 1), (CilOperation.ConvertNativeUInt, 1, 1), (CilOperation.ConvertFloat32, 1, 1),
        (CilOperation.ConvertFloat64, 1, 1), (CilOperation.ConvertFloatUnsigned, 1, 1), (CilOperation.ConvertNumeric, 1, 1),
        (CilOperation.CheckFinite, 1, 1), (CilOperation.LoadVirtualFunction, 1, 1), (CilOperation.LocalAllocate, 1, 1),
        (CilOperation.StoreField, 2, 0), (CilOperation.StoreObject, 2, 0), (CilOperation.CopyObject, 2, 0),
        (CilOperation.BranchIfEqual, 2, 0), (CilOperation.BranchIfNotEqual, 2, 0), (CilOperation.BranchIfGreaterThanSigned, 2, 0),
        (CilOperation.BranchIfGreaterThanUnsigned, 2, 0), (CilOperation.BranchIfGreaterThanOrEqualSigned, 2, 0),
        (CilOperation.BranchIfGreaterThanOrEqualUnsigned, 2, 0), (CilOperation.BranchIfLessThanSigned, 2, 0),
        (CilOperation.BranchIfLessThanUnsigned, 2, 0), (CilOperation.BranchIfLessThanOrEqualSigned, 2, 0),
        (CilOperation.BranchIfLessThanOrEqualUnsigned, 2, 0),
        (CilOperation.Add, 2, 1), (CilOperation.Subtract, 2, 1), (CilOperation.Multiply, 2, 1), (CilOperation.Divide, 2, 1),
        (CilOperation.DivideUnsigned, 2, 1), (CilOperation.Remainder, 2, 1), (CilOperation.RemainderUnsigned, 2, 1),
        (CilOperation.BitwiseAnd, 2, 1), (CilOperation.BitwiseOr, 2, 1), (CilOperation.BitwiseXor, 2, 1),
        (CilOperation.ShiftLeft, 2, 1), (CilOperation.ShiftRightSigned, 2, 1), (CilOperation.ShiftRightUnsigned, 2, 1),
        (CilOperation.AddChecked, 2, 1), (CilOperation.AddCheckedUnsigned, 2, 1), (CilOperation.SubtractChecked, 2, 1),
        (CilOperation.SubtractCheckedUnsigned, 2, 1), (CilOperation.MultiplyChecked, 2, 1),
        (CilOperation.MultiplyCheckedUnsigned, 2, 1), (CilOperation.CompareEqual, 2, 1),
        (CilOperation.CompareGreaterThanSigned, 2, 1), (CilOperation.CompareGreaterThanUnsigned, 2, 1),
        (CilOperation.CompareLessThanSigned, 2, 1), (CilOperation.CompareLessThanUnsigned, 2, 1),
        (CilOperation.LoadArrayElement, 2, 1), (CilOperation.LoadArrayElementAddress, 2, 1),
        (CilOperation.LoadArrayElementReference, 2, 1),
        (CilOperation.StoreArrayElement, 3, 0), (CilOperation.StoreArrayElementReference, 3, 0), (CilOperation.CopyBlock, 3, 0),
        (CilOperation.InitializeBlock, 3, 0),
        (CilOperation.Call, 2, 1), (CilOperation.CallVirtual, 3, 0), (CilOperation.NewObject, 1, 1),
    ];

    [Fact]
    public void FollowsEveryOperationItKnows()
    {
        var instructions = new List<(CilOperation, CilOperand)>();
        var expected = new List<int>();
        foreach (var (operation, pops, pushes) in Effects)
        {
            for (var index = 0; index < pops; index++) Add(CilOperation.LoadInt32, new CilOperand.ConstantI4(index), index);
            var next = instructions.Count + 1;
            CilOperand operand = operation switch
            {
                CilOperation.Call => Method(isStatic: true, parameters: 2, returns: true),
                CilOperation.CallVirtual => Method(isStatic: false, parameters: 2, returns: false),
                CilOperation.NewObject => Method(isStatic: false, parameters: 1, returns: false),
                CilOperation.Switch => new CilOperand.SwitchTargets([next]),
                _ when operation.ToString().StartsWith("Branch", StringComparison.Ordinal) => new CilOperand.BranchTarget(next),
                _ => new CilOperand.None(),
            };
            Add(operation, operand, pops);
            for (var index = pushes; index > 0; index--) Add(CilOperation.Pop, new CilOperand.None(), index);
        }
        Add(CilOperation.Branch, new CilOperand.BranchTarget(instructions.Count + 1), 0);
        Add(CilOperation.Return, new CilOperand.None(), 0);

        var depths = CilStackDepths.Compute(Body([.. instructions.Select((item, offset) => Instruction(offset, item.Item1, item.Item2))]));

        Assert.NotNull(depths);
        Assert.Equal(expected, Enumerable.Range(0, expected.Count).Select(offset => depths[offset]));

        void Add(CilOperation operation, CilOperand operand, int depth)
        {
            instructions.Add((operation, operand));
            expected.Add(depth);
        }
    }

    [Fact]
    public void HandlersAndFiltersStartWithTheirStackAndLeaveEmptiesIt()
    {
        var body = Body([
            Instruction(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            Instruction(1, CilOperation.Leave, new CilOperand.BranchTarget(8)),
            Instruction(2, CilOperation.Pop),
            Instruction(3, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            Instruction(4, CilOperation.EndFilter),
            Instruction(5, CilOperation.Pop),
            Instruction(6, CilOperation.Rethrow),
            Instruction(7, CilOperation.EndFinally),
            Instruction(8, CilOperation.Return),
        ]) with
        {
            ExceptionRegions = [
                new(CilExceptionRegionKind.Filter, 0, 2, 5, 2, null, 2),
                new(CilExceptionRegionKind.Finally, 0, 2, 7, 1, null, null),
            ],
        };

        var depths = CilStackDepths.Compute(body);

        Assert.Equal([(0, 0), (1, 1), (2, 1), (3, 0), (4, 1), (5, 1), (6, 0), (7, 0), (8, 0)],
            depths!.OrderBy(pair => pair.Key).Select(pair => (pair.Key, pair.Value)));
        Assert.Empty(CilStackDepths.Compute(Body([]))!);
    }

    public static TheoryData<string> Unfollowed => new()
    {
        "unknown", "underflow", "joins", "branch joins", "leave joins", "switch joins", "handler joins", "filter joins",
        "throw", "falls off", "handler start", "filter start", "branch back", "switch back",
    };

    [Theory]
    [MemberData(nameof(Unfollowed))]
    public void GivesUpOnABodyItCannotFollow(string problem)
    {
        CilInstruction Load(int offset) => Instruction(offset, CilOperation.LoadInt32, new CilOperand.ConstantI4(1));
        var body = problem switch
        {
            "unknown" => Body([Instruction(0, CilOperation.CallIndirect), Instruction(1, CilOperation.Return)]),
            "underflow" => Body([Instruction(0, CilOperation.Pop), Instruction(1, CilOperation.Return)]),
            "joins" => Body([Load(0), Instruction(1, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(3)), Load(2),
                Instruction(3, CilOperation.Return)]),
            "branch joins" => Body([Load(0), Instruction(1, CilOperation.Branch, new CilOperand.BranchTarget(0))]),
            "leave joins" => Body([Load(0), Instruction(1, CilOperation.Leave, new CilOperand.BranchTarget(1))]),
            "switch joins" => Body([Load(0), Load(1), Instruction(2, CilOperation.Switch, new CilOperand.SwitchTargets([4])),
                Instruction(3, CilOperation.Pop), Instruction(4, CilOperation.Return)]),
            "handler joins" => Body([Load(0), Instruction(1, CilOperation.Pop), Instruction(2, CilOperation.Return)]) with
            {
                ExceptionRegions = [new(CilExceptionRegionKind.Finally, 0, 1, 1, 1, null, null)],
            },
            "filter joins" => Body([Load(0), Instruction(1, CilOperation.Pop), Instruction(2, CilOperation.Return)]) with
            {
                ExceptionRegions = [new(CilExceptionRegionKind.Filter, 0, 1, 2, 1, null, 2)],
            },
            "falls off" => Body([Instruction(0, CilOperation.Nop)]),
            "handler start" => Body([Instruction(0, CilOperation.Return)]) with
            {
                ExceptionRegions = [new(CilExceptionRegionKind.Catch, 0, 1, 0, 1, null, null)],
            },
            "filter start" => Body([Instruction(0, CilOperation.Return), Instruction(1, CilOperation.Pop),
                Instruction(2, CilOperation.Return)]) with
            {
                ExceptionRegions = [new(CilExceptionRegionKind.Filter, 0, 1, 1, 1, null, 0)],
            },
            "branch back" => Body([Load(0), Load(1), Instruction(2, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(0))]),
            "switch back" => Body([Load(0), Load(1), Instruction(2, CilOperation.Switch, new CilOperand.SwitchTargets([0]))]),
            _ => Body([Instruction(0, CilOperation.Throw), Instruction(1, CilOperation.Return)]),
        };

        Assert.Null(CilStackDepths.Compute(body));
    }

    private static CilOperand.MethodInstance Method(bool isStatic, int parameters, bool returns)
    {
        var signature = new MethodSignatureModel(returns ? CliValueKind.I4 : CliValueKind.Void,
            [.. Enumerable.Repeat(CliValueKind.I4, parameters)]);
        var definition = new MethodDefinitionModel(new(Assembly, 0x06000002), new(Assembly, 0x02000002), "Callee", isStatic,
            signature, 1);
        return new CilOperand.MethodInstance(new(definition, CliTypeIdentity.Named(Assembly, "", "T", false), [], signature));
    }

    private static CilInstruction Instruction(int offset, CilOperation operation, CilOperand? operand = null) =>
        new(offset, offset + 1, operation, operand ?? new CilOperand.None());

    private static CilMethodBody Body(ImmutableArray<CilInstruction> instructions) => new(Definition, 8, [], instructions);
}
