using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Correctness;

internal sealed class RandomCilGenerator : IRandomCilGenerator
{
    private static readonly ImmutableArray<CilOperation> BinaryOperations =
    [
        CilOperation.Add,
        CilOperation.Subtract,
        CilOperation.Multiply,
        CilOperation.BitwiseAnd,
        CilOperation.BitwiseOr,
        CilOperation.BitwiseXor,
        CilOperation.ShiftLeft,
        CilOperation.ShiftRightSigned,
        CilOperation.ShiftRightUnsigned,
        CilOperation.AddChecked,
        CilOperation.AddCheckedUnsigned,
        CilOperation.SubtractChecked,
        CilOperation.SubtractCheckedUnsigned,
        CilOperation.MultiplyChecked,
        CilOperation.MultiplyCheckedUnsigned,
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

    private static readonly ImmutableArray<CilOperation> ConditionalBranches =
    [
        CilOperation.BranchIfTrue,
        CilOperation.BranchIfFalse,
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

    public GeneratedCilProgram Generate(int seed)
    {
        var random = new Random(seed);
        var blocks = ImmutableArray.CreateBuilder<GeneratedCilBlock>();
        var nextId = 0;
        var entryId = nextId++;
        var firstCondition = nextId++;
        blocks.Add(Block(entryId,
            Instruction(CilOperation.Nop),
            Instruction(CilOperation.Break),
            Instruction(CilOperation.LoadInt64,
                new GeneratedCilOperand.Int64(random.NextInt64())),
            Instruction(CilOperation.Pop),
            Instruction(CilOperation.LoadFloat32,
                new GeneratedCilOperand.Float32((float)random.NextDouble())),
            Instruction(CilOperation.Pop),
            Instruction(CilOperation.LoadFloat64,
                new GeneratedCilOperand.Float64(random.NextDouble())),
            Instruction(CilOperation.Pop),
            Instruction(CilOperation.LoadNull),
            Instruction(CilOperation.Pop),
            Instruction(CilOperation.LoadArgument, new GeneratedCilOperand.Index(0)),
            Instruction(CilOperation.StoreLocal, new GeneratedCilOperand.Index(0)),
            Instruction(CilOperation.LoadLocal, new GeneratedCilOperand.Index(0)),
            Instruction(CilOperation.Duplicate),
            Instruction(CilOperation.Pop),
            Instruction(CilOperation.ConvertInt64),
            Instruction(CilOperation.ConvertInt32),
            Instruction(CilOperation.ConvertInt32Unsigned),
            Instruction(CilOperation.ConvertInt64Unsigned),
            Instruction(CilOperation.ConvertInt32),
            Instruction(CilOperation.ConvertNativeInt),
            Instruction(CilOperation.ConvertInt32),
            Instruction(CilOperation.ConvertNativeUInt),
            Instruction(CilOperation.ConvertInt32),
            Instruction(CilOperation.ConvertFloat32),
            Instruction(CilOperation.ConvertFloat64),
            Instruction(CilOperation.Pop),
            Instruction(CilOperation.LoadArgument,
                new GeneratedCilOperand.Index(0)),
            Instruction(CilOperation.ConvertFloatUnsigned),
            Instruction(CilOperation.CheckFinite),
            Instruction(CilOperation.Pop),
            Instruction(CilOperation.LoadArgument,
                new GeneratedCilOperand.Index(0)),
            Instruction(CilOperation.ConvertInt32),
            Instruction(CilOperation.StoreArgument, new GeneratedCilOperand.Index(0)),
            Instruction(CilOperation.Branch,
                new GeneratedCilOperand.BlockTarget(firstCondition))));

        var conditionId = firstCondition;
        for (var index = 0; index < 4; index++)
        {
            var trueId = nextId++;
            var falseId = nextId++;
            var joinId = nextId++;
            blocks.Add(ConditionBlock(
                random,
                conditionId,
                falseId,
                consumesIncomingValue: index > 0));
            blocks.Add(MutationBlock(random, trueId, joinId));
            blocks.Add(MutationBlock(random, falseId, joinId));
            conditionId = joinId;
        }

        var defaultId = nextId++;
        var caseIds = Enumerable.Range(0, 4).Select(_ => nextId++).ToImmutableArray();
        var returnId = nextId++;
        blocks.Add(BlockWithEntry(conditionId, [CliValueKind.I4],
            Instruction(CilOperation.StoreArgument, new GeneratedCilOperand.Index(0)),
            Instruction(CilOperation.LoadArgument, new GeneratedCilOperand.Index(0)),
            Instruction(CilOperation.LoadInt32, new GeneratedCilOperand.Int32(3)),
            Instruction(CilOperation.BitwiseAnd),
            Instruction(CilOperation.Switch,
                new GeneratedCilOperand.BlockTargets(caseIds))));
        blocks.Add(MutationBlock(random, defaultId, returnId));
        foreach (var caseId in caseIds)
        {
            blocks.Add(MutationBlock(random, caseId, returnId));
        }
        blocks.Add(BlockWithEntry(returnId, [CliValueKind.I4],
            Instruction(CilOperation.Return)));

        return new(
            seed,
            CliValueKind.I4,
            [CliValueKind.I4],
            [CliValueKind.I4],
            blocks.ToImmutable());
    }

    public GeneratedCilProgram GenerateMetadata(
        int seed,
        RandomCilMetadataTokens tokens)
    {
        var scalarType = TypeToken(tokens.Int32Type, CliValueKind.I4);
        var boxType = TypeToken(tokens.BoxType, CliValueKind.ManagedReference);
        var userString = TypeToken(tokens.UserString, CliValueKind.ManagedReference);
        var constructor = MethodToken(
            tokens.BoxConstructor,
            [],
            CliValueKind.ManagedReference,
            isStatic: false,
            isConstructor: true);
        var addMethod = MethodToken(
            tokens.BoxAddMethod,
            [CliValueKind.I4],
            CliValueKind.I4,
            isStatic: false,
            isVirtual: true);
        var helperMethod = MethodToken(
            tokens.HelperMethod,
            [CliValueKind.I4, CliValueKind.I4],
            CliValueKind.I4,
            isStatic: true);
        var unaryMethod = MethodToken(
            tokens.HelperUnaryMethod,
            [CliValueKind.I4],
            CliValueKind.I4,
            isStatic: true);
        var unaryCallSite = MethodToken(
            tokens.UnaryCallSite,
            [CliValueKind.I4],
            CliValueKind.I4,
            isStatic: true);
        var getHashCode = MethodToken(
            tokens.GetHashCodeMethod,
            [],
            CliValueKind.I4,
            isStatic: false);
        var valueField = FieldToken(tokens.BoxValueField, CliValueKind.I4, false);
        var staticField = FieldToken(tokens.StaticValueField, CliValueKind.I4, true);
        var constant = unchecked((seed * 17) + 3);
        return new(
            seed,
            CliValueKind.I4,
            [CliValueKind.I4],
            [CliValueKind.I4, CliValueKind.ManagedReference],
            [
                Block(0,
                    Instruction(CilOperation.NewObject, constructor),
                    Instruction(CilOperation.StoreLocal,
                        new GeneratedCilOperand.Index(1)),
                    Instruction(CilOperation.NewObject, constructor),
                    Instruction(CilOperation.Pop),
                    Instruction(CilOperation.LoadLocal,
                        new GeneratedCilOperand.Index(1)),
                    Instruction(CilOperation.LoadArgument,
                        new GeneratedCilOperand.Index(0)),
                    Instruction(CilOperation.StoreLocal,
                        new GeneratedCilOperand.Index(0)),
                    Instruction(CilOperation.LoadLocal,
                        new GeneratedCilOperand.Index(0)),
                    Instruction(CilOperation.StoreArgument,
                        new GeneratedCilOperand.Index(0)),
                    Instruction(CilOperation.LoadLocalAddress,
                        new GeneratedCilOperand.Index(0)),
                    Instruction(CilOperation.Unaligned,
                        new GeneratedCilOperand.Index(1)),
                    Instruction(CilOperation.LoadObject, scalarType),
                    Instruction(CilOperation.StoreArgument,
                        new GeneratedCilOperand.Index(0)),
                    Instruction(CilOperation.Duplicate),
                    Instruction(CilOperation.LoadArgument,
                        new GeneratedCilOperand.Index(0)),
                    Instruction(CilOperation.StoreField, valueField),
                    Instruction(CilOperation.Duplicate),
                    Instruction(CilOperation.LoadField, valueField),
                    Instruction(CilOperation.StoreArgument,
                        new GeneratedCilOperand.Index(0)),
                    Instruction(CilOperation.Duplicate),
                    Instruction(CilOperation.LoadFieldAddress, valueField),
                    Instruction(CilOperation.LoadObject, scalarType),
                    Instruction(CilOperation.StoreArgument,
                        new GeneratedCilOperand.Index(0)),
                    Instruction(CilOperation.Pop),
                    Instruction(CilOperation.NewObject, constructor),
                    Instruction(CilOperation.LoadArgument,
                        new GeneratedCilOperand.Index(0)),
                    Instruction(CilOperation.CallVirtual, addMethod),
                    Instruction(CilOperation.StoreArgument,
                        new GeneratedCilOperand.Index(0)),
                    Instruction(CilOperation.LoadArgument,
                        new GeneratedCilOperand.Index(0)),
                    Instruction(CilOperation.StoreStaticField, staticField),
                    Instruction(CilOperation.LoadStaticField, staticField),
                    Instruction(CilOperation.LoadStaticFieldAddress, staticField),
                    Instruction(CilOperation.LoadObject, scalarType),
                    Instruction(CilOperation.Add),
                    Instruction(CilOperation.LoadInt32,
                        new GeneratedCilOperand.Int32(constant)),
                    Instruction(CilOperation.Call, helperMethod),
                    Instruction(CilOperation.StoreArgument,
                        new GeneratedCilOperand.Index(0)),
                    Instruction(CilOperation.LoadInt32,
                        new GeneratedCilOperand.Int32(2)),
                    Instruction(CilOperation.NewArray, scalarType),
                    Instruction(CilOperation.Duplicate),
                    Instruction(CilOperation.LoadInt32,
                        new GeneratedCilOperand.Int32(0)),
                    Instruction(CilOperation.LoadArgument,
                        new GeneratedCilOperand.Index(0)),
                    Instruction(CilOperation.StoreArrayElement, scalarType),
                    Instruction(CilOperation.Duplicate),
                    Instruction(CilOperation.LoadInt32,
                        new GeneratedCilOperand.Int32(1)),
                    Instruction(CilOperation.Readonly),
                    Instruction(CilOperation.LoadArrayElementAddress, scalarType),
                    Instruction(CilOperation.LoadArgument,
                        new GeneratedCilOperand.Index(0)),
                    Instruction(CilOperation.StoreObject, scalarType),
                    Instruction(CilOperation.Duplicate),
                    Instruction(CilOperation.LoadInt32,
                        new GeneratedCilOperand.Int32(1)),
                    Instruction(CilOperation.LoadArrayElementAddress, scalarType),
                    Instruction(CilOperation.LoadObject, scalarType),
                    Instruction(CilOperation.StoreArgument,
                        new GeneratedCilOperand.Index(0)),
                    Instruction(CilOperation.Duplicate),
                    Instruction(CilOperation.LoadInt32,
                        new GeneratedCilOperand.Int32(0)),
                    Instruction(CilOperation.LoadArrayElement, scalarType),
                    Instruction(CilOperation.StoreArgument,
                        new GeneratedCilOperand.Index(0)),
                    Instruction(CilOperation.Duplicate),
                    Instruction(CilOperation.LoadArrayLength),
                    Instruction(CilOperation.Pop),
                    Instruction(CilOperation.LoadInt32,
                        new GeneratedCilOperand.Int32(1)),
                    Instruction(CilOperation.NewArray, boxType),
                    Instruction(CilOperation.Duplicate),
                    Instruction(CilOperation.LoadInt32,
                        new GeneratedCilOperand.Int32(0)),
                    Instruction(CilOperation.NewObject, constructor),
                    Instruction(CilOperation.StoreArrayElementReference),
                    Instruction(CilOperation.LoadInt32,
                        new GeneratedCilOperand.Int32(0)),
                    Instruction(CilOperation.LoadArrayElementReference),
                    Instruction(CilOperation.Pop),
                    Instruction(CilOperation.LoadArgumentAddress,
                        new GeneratedCilOperand.Index(0)),
                    Instruction(CilOperation.Duplicate),
                    Instruction(CilOperation.LoadObject, scalarType),
                    Instruction(CilOperation.StoreArgument,
                        new GeneratedCilOperand.Index(0)),
                    Instruction(CilOperation.LoadArgumentAddress,
                        new GeneratedCilOperand.Index(0)),
                    Instruction(CilOperation.CopyObject, scalarType),
                    Instruction(CilOperation.LoadArgumentAddress,
                        new GeneratedCilOperand.Index(0)),
                    Instruction(CilOperation.InitializeObject, scalarType),
                    Instruction(CilOperation.LoadArgumentAddress,
                        new GeneratedCilOperand.Index(0)),
                    Instruction(CilOperation.LoadInt32,
                        new GeneratedCilOperand.Int32(constant)),
                    Instruction(CilOperation.StoreObject, scalarType),
                    Instruction(CilOperation.Pop),
                    Instruction(CilOperation.LoadArgument,
                        new GeneratedCilOperand.Index(0)),
                    Instruction(CilOperation.Box, scalarType),
                    Instruction(CilOperation.Unbox, scalarType),
                    Instruction(CilOperation.LoadObject, scalarType),
                    Instruction(CilOperation.StoreArgument,
                        new GeneratedCilOperand.Index(0)),
                    Instruction(CilOperation.LoadArgument,
                        new GeneratedCilOperand.Index(0)),
                    Instruction(CilOperation.Box, scalarType),
                    Instruction(CilOperation.UnboxAny, scalarType),
                    Instruction(CilOperation.StoreArgument,
                        new GeneratedCilOperand.Index(0)),
                    Instruction(CilOperation.LoadArgumentAddress,
                        new GeneratedCilOperand.Index(0)),
                    Instruction(CilOperation.Constrained, scalarType),
                    Instruction(CilOperation.CallVirtual, getHashCode),
                    Instruction(CilOperation.StoreArgument,
                        new GeneratedCilOperand.Index(0)),
                    Instruction(CilOperation.NewObject, constructor),
                    Instruction(CilOperation.CastClass, boxType),
                    Instruction(CilOperation.IsInstance, boxType),
                    Instruction(CilOperation.Pop),
                    Instruction(CilOperation.LoadTypeToken, scalarType),
                    Instruction(CilOperation.Pop),
                    Instruction(CilOperation.LoadFieldToken,
                        new GeneratedCilOperand.MetadataToken(
                            tokens.ArrayInitializerField,
                            CliValueKind.I4,
                            [],
                            CliValueKind.Void,
                            IsStatic: true,
                            IsConstructor: false)),
                    Instruction(CilOperation.Pop),
                    Instruction(CilOperation.LoadString, userString),
                    Instruction(CilOperation.Pop),
                    Instruction(CilOperation.SizeOf, scalarType),
                    Instruction(CilOperation.Pop),
                    Instruction(CilOperation.LoadArgument,
                        new GeneratedCilOperand.Index(0)),
                    Instruction(CilOperation.LoadFunction, unaryMethod),
                    Instruction(CilOperation.CallIndirect, unaryCallSite),
                    Instruction(CilOperation.StoreArgument,
                        new GeneratedCilOperand.Index(0)),
                    Instruction(CilOperation.NewObject, constructor),
                    Instruction(CilOperation.LoadVirtualFunction, addMethod),
                    Instruction(CilOperation.Pop),
                    Instruction(CilOperation.LoadInt32,
                        new GeneratedCilOperand.Int32(8)),
                    Instruction(CilOperation.LocalAllocate),
                    Instruction(CilOperation.Duplicate),
                    Instruction(CilOperation.Duplicate),
                    Instruction(CilOperation.LoadInt32,
                        new GeneratedCilOperand.Int32(0x5a)),
                    Instruction(CilOperation.LoadInt32,
                        new GeneratedCilOperand.Int32(8)),
                    Instruction(CilOperation.Volatile),
                    Instruction(CilOperation.InitializeBlock),
                    Instruction(CilOperation.LoadInt32,
                        new GeneratedCilOperand.Int32(8)),
                    Instruction(CilOperation.CopyBlock),
                    Instruction(CilOperation.LoadArgument,
                        new GeneratedCilOperand.Index(0)),
                    Instruction(CilOperation.Return))
            ]);
    }

    public GeneratedCilProgram GenerateExceptionHandling(int seed)
    {
        var filterConstant = Math.Abs(seed % 7) + 1;
        return new(
            seed,
            CliValueKind.I4,
            [CliValueKind.I4],
            [CliValueKind.I4],
            [
                BlockIn(0, [Membership(1, GeneratedExceptionRegionPart.Try),
                        Membership(0, GeneratedExceptionRegionPart.Try)],
                    Instruction(CilOperation.LoadNull),
                    Instruction(CilOperation.Throw)),
                BlockWithEntryIn(1, [CliValueKind.ManagedReference],
                    [Membership(1, GeneratedExceptionRegionPart.Try),
                        Membership(0, GeneratedExceptionRegionPart.Filter)],
                    Instruction(CilOperation.Pop),
                    Instruction(CilOperation.LoadArgument,
                        new GeneratedCilOperand.Index(0)),
                    Instruction(CilOperation.LoadInt32,
                        new GeneratedCilOperand.Int32(filterConstant)),
                    Instruction(CilOperation.CompareGreaterThanSigned),
                    Instruction(CilOperation.EndFilter)),
                BlockWithEntryIn(2, [CliValueKind.ManagedReference],
                    [Membership(1, GeneratedExceptionRegionPart.Try),
                        Membership(0, GeneratedExceptionRegionPart.Handler)],
                    Instruction(CilOperation.Pop),
                    Instruction(CilOperation.LoadArgument,
                        new GeneratedCilOperand.Index(0)),
                    Instruction(CilOperation.LoadInt32,
                        new GeneratedCilOperand.Int32(8)),
                    Instruction(CilOperation.BitwiseAnd),
                    Instruction(CilOperation.BranchIfTrue,
                        new GeneratedCilOperand.BlockTarget(4))),
                BlockIn(3, [Membership(1, GeneratedExceptionRegionPart.Try),
                        Membership(0, GeneratedExceptionRegionPart.Handler)],
                    Instruction(CilOperation.LoadInt32,
                        new GeneratedCilOperand.Int32(11)),
                    Instruction(CilOperation.StoreArgument,
                        new GeneratedCilOperand.Index(0)),
                    Instruction(CilOperation.Leave,
                        new GeneratedCilOperand.BlockTarget(5))),
                BlockIn(4, [Membership(1, GeneratedExceptionRegionPart.Try),
                        Membership(0, GeneratedExceptionRegionPart.Handler)],
                    Instruction(CilOperation.Rethrow)),
                BlockIn(5, [Membership(1, GeneratedExceptionRegionPart.Try)],
                    Instruction(CilOperation.LoadArgument,
                        new GeneratedCilOperand.Index(0)),
                    Instruction(CilOperation.LoadInt32,
                        new GeneratedCilOperand.Int32(3)),
                    Instruction(CilOperation.Add),
                    Instruction(CilOperation.StoreArgument,
                        new GeneratedCilOperand.Index(0)),
                    Instruction(CilOperation.Leave,
                        new GeneratedCilOperand.BlockTarget(7))),
                BlockIn(6, [Membership(1, GeneratedExceptionRegionPart.Handler)],
                    Instruction(CilOperation.LoadArgument,
                        new GeneratedCilOperand.Index(0)),
                    Instruction(CilOperation.LoadInt32,
                        new GeneratedCilOperand.Int32(5)),
                    Instruction(CilOperation.Add),
                    Instruction(CilOperation.StoreArgument,
                        new GeneratedCilOperand.Index(0)),
                    Instruction(CilOperation.EndFinally)),
                Block(7,
                    Instruction(CilOperation.LoadArgument,
                        new GeneratedCilOperand.Index(0)),
                    Instruction(CilOperation.Return)),
            ])
        {
            ExceptionRegions =
            [
                new(
                    0,
                    CilExceptionRegionKind.Filter,
                    0,
                    1,
                    2,
                    5,
                    FilterStartBlock: 1),
                new(
                    1,
                    CilExceptionRegionKind.Finally,
                    0,
                    6,
                    6,
                    7),
            ],
        };
    }

    public GeneratedCilProgram GenerateControlFlowExceptionCombination(int seed)
    {
        var firstConstant = Math.Abs(seed % 11) + 1;
        var secondConstant = Math.Abs(seed % 7) + 1;
        var outerTry = Membership(1, GeneratedExceptionRegionPart.Try);
        var innerTry = Membership(0, GeneratedExceptionRegionPart.Try);
        return new(
            seed,
            CliValueKind.I4,
            [CliValueKind.I4],
            [CliValueKind.I4],
            [
                BlockIn(0, [outerTry, innerTry],
                    Instruction(CilOperation.LoadArgument,
                        new GeneratedCilOperand.Index(0)),
                    Instruction(CilOperation.LoadInt32,
                        new GeneratedCilOperand.Int32(1)),
                    Instruction(CilOperation.BitwiseAnd),
                    Instruction(CilOperation.BranchIfTrue,
                        new GeneratedCilOperand.BlockTarget(2))),
                BlockIn(1, [outerTry, innerTry],
                    Instruction(CilOperation.Branch,
                        new GeneratedCilOperand.BlockTarget(3))),
                BlockIn(2, [outerTry, innerTry],
                    Instruction(CilOperation.Branch,
                        new GeneratedCilOperand.BlockTarget(4))),
                BlockIn(3, [outerTry, innerTry],
                    Instruction(CilOperation.LoadArgument,
                        new GeneratedCilOperand.Index(0)),
                    Instruction(CilOperation.LoadInt32,
                        new GeneratedCilOperand.Int32(firstConstant)),
                    Instruction(CilOperation.Add),
                    Instruction(CilOperation.StoreArgument,
                        new GeneratedCilOperand.Index(0)),
                    Instruction(CilOperation.Branch,
                        new GeneratedCilOperand.BlockTarget(5))),
                BlockIn(4, [outerTry, innerTry],
                    Instruction(CilOperation.LoadArgument,
                        new GeneratedCilOperand.Index(0)),
                    Instruction(CilOperation.LoadInt32,
                        new GeneratedCilOperand.Int32(secondConstant)),
                    Instruction(CilOperation.Subtract),
                    Instruction(CilOperation.StoreArgument,
                        new GeneratedCilOperand.Index(0)),
                    Instruction(CilOperation.LoadArgument,
                        new GeneratedCilOperand.Index(0)),
                    Instruction(CilOperation.BranchIfTrue,
                        new GeneratedCilOperand.BlockTarget(3))),
                BlockIn(5, [outerTry, innerTry],
                    Instruction(CilOperation.Leave,
                        new GeneratedCilOperand.BlockTarget(8))),
                BlockIn(6,
                    [outerTry, Membership(0, GeneratedExceptionRegionPart.Handler)],
                    Instruction(CilOperation.LoadArgument,
                        new GeneratedCilOperand.Index(0)),
                    Instruction(CilOperation.LoadInt32,
                        new GeneratedCilOperand.Int32(3)),
                    Instruction(CilOperation.Add),
                    Instruction(CilOperation.StoreArgument,
                        new GeneratedCilOperand.Index(0)),
                    Instruction(CilOperation.EndFinally)),
                BlockIn(7, [Membership(1, GeneratedExceptionRegionPart.Handler)],
                    Instruction(CilOperation.LoadArgument,
                        new GeneratedCilOperand.Index(0)),
                    Instruction(CilOperation.LoadInt32,
                        new GeneratedCilOperand.Int32(5)),
                    Instruction(CilOperation.Add),
                    Instruction(CilOperation.StoreArgument,
                        new GeneratedCilOperand.Index(0)),
                    Instruction(CilOperation.EndFinally)),
                Block(8,
                    Instruction(CilOperation.LoadArgument,
                        new GeneratedCilOperand.Index(0)),
                    Instruction(CilOperation.Return)),
            ])
        {
            ExceptionRegions =
            [
                new(
                    0,
                    CilExceptionRegionKind.Finally,
                    0,
                    6,
                    6,
                    7),
                new(
                    1,
                    CilExceptionRegionKind.Finally,
                    0,
                    7,
                    7,
                    8),
            ],
        };
    }

    private static GeneratedCilOperand.MetadataToken TypeToken(
        int token,
        CliValueKind kind) => new(
        token,
        kind,
        [],
        CliValueKind.Void,
        IsStatic: true,
        IsConstructor: false);

    private static GeneratedCilOperand.MetadataToken FieldToken(
        int token,
        CliValueKind kind,
        bool isStatic) => new(
        token,
        kind,
        [],
        CliValueKind.Void,
        isStatic,
        IsConstructor: false);

    private static GeneratedCilOperand.MetadataToken MethodToken(
        int token,
        ImmutableArray<CliValueKind> parameters,
        CliValueKind returnType,
        bool isStatic,
        bool isConstructor = false,
        bool isVirtual = false) => new(
        token,
        returnType,
        parameters,
        returnType,
        isStatic,
        isConstructor,
        isVirtual);

    private static GeneratedCilBlock MutationBlock(
        Random random,
        int id,
        int target)
    {
        var shape = random.Next(0, 4);
        if (shape == 1)
        {
            var unaryOperation = random.Next(0, 2) == 0
                ? CilOperation.Negate
                : CilOperation.OnesComplement;
            return Block(id,
                Instruction(CilOperation.LoadArgument,
                    new GeneratedCilOperand.Index(0)),
                Instruction(unaryOperation),
                Instruction(CilOperation.Branch,
                    new GeneratedCilOperand.BlockTarget(target)));
        }
        if (shape == 2)
        {
            var bits = random.Next(0, 3) switch
            {
                0 => 8,
                1 => 16,
                _ => 32,
            };
            return Block(id,
                Instruction(CilOperation.LoadArgument,
                    new GeneratedCilOperand.Index(0)),
                Instruction(CilOperation.ConvertNumeric,
                    new GeneratedCilOperand.NumericConversion(
                        bits,
                        DestinationUnsigned: random.Next(0, 2) == 0,
                        Checked: random.Next(0, 2) == 0,
                        SourceUnsigned: random.Next(0, 2) == 0,
                        Native: false)),
                Instruction(CilOperation.Branch,
                    new GeneratedCilOperand.BlockTarget(target)));
        }
        if (shape == 3)
        {
            return Block(id,
                Instruction(CilOperation.LoadArgument,
                    new GeneratedCilOperand.Index(0)),
                Instruction(CilOperation.Duplicate),
                Instruction(CilOperation.Pop),
                Instruction(CilOperation.Branch,
                    new GeneratedCilOperand.BlockTarget(target)));
        }

        var operation = BinaryOperations[random.Next(BinaryOperations.Length)];
        var constant = operation is CilOperation.Divide or
            CilOperation.DivideUnsigned or CilOperation.Remainder or
            CilOperation.RemainderUnsigned
                ? random.Next(1, 17)
                : operation is CilOperation.ShiftLeft or
                    CilOperation.ShiftRightSigned or
                    CilOperation.ShiftRightUnsigned
                    ? random.Next(0, 32)
                    : random.Next(-31, 32);
        return Block(id,
            Instruction(CilOperation.LoadArgument, new GeneratedCilOperand.Index(0)),
            Instruction(CilOperation.LoadInt32, new GeneratedCilOperand.Int32(constant)),
            Instruction(operation),
            Instruction(CilOperation.Branch, new GeneratedCilOperand.BlockTarget(target)));
    }

    private static GeneratedCilBlock ConditionBlock(
        Random random,
        int id,
        int falseTarget,
        bool consumesIncomingValue)
    {
        var operation = ConditionalBranches[random.Next(ConditionalBranches.Length)];
        var instructions = ImmutableArray.CreateBuilder<GeneratedCilInstruction>();
        if (consumesIncomingValue)
        {
            instructions.Add(Instruction(
                CilOperation.StoreArgument,
                new GeneratedCilOperand.Index(0)));
        }
        instructions.Add(Instruction(
            CilOperation.LoadArgument,
            new GeneratedCilOperand.Index(0)));
        if (operation is not (CilOperation.BranchIfTrue or
            CilOperation.BranchIfFalse))
        {
            instructions.Add(Instruction(
                CilOperation.LoadInt32,
                new GeneratedCilOperand.Int32(random.Next(-31, 32))));
        }
        instructions.Add(Instruction(
            operation,
            new GeneratedCilOperand.BlockTarget(falseTarget)));
        return new(
            id,
            consumesIncomingValue ? [CliValueKind.I4] : [],
            [],
            instructions.ToImmutable());
    }

    private static GeneratedCilBlock Block(
        int id,
        params GeneratedCilInstruction[] instructions) => new(
            id,
            [],
            [],
            [.. instructions]);

    private static GeneratedCilBlock BlockWithEntry(
        int id,
        ImmutableArray<CliValueKind> entryStack,
        params GeneratedCilInstruction[] instructions) => new(
            id,
            entryStack,
            [],
            [.. instructions]);

    private static GeneratedCilBlock BlockIn(
        int id,
        ImmutableArray<GeneratedExceptionRegionMembership> path,
        params GeneratedCilInstruction[] instructions) => new(
            id,
            [],
            path,
            [.. instructions]);

    private static GeneratedCilBlock BlockWithEntryIn(
        int id,
        ImmutableArray<CliValueKind> entryStack,
        ImmutableArray<GeneratedExceptionRegionMembership> path,
        params GeneratedCilInstruction[] instructions) => new(
            id,
            entryStack,
            path,
            [.. instructions]);

    private static GeneratedExceptionRegionMembership Membership(
        int region,
        GeneratedExceptionRegionPart part) => new(region, part);

    private static GeneratedCilInstruction Instruction(
        CilOperation operation,
        GeneratedCilOperand? operand = null) => new(
            operation,
            operand ?? new GeneratedCilOperand.None());
}
