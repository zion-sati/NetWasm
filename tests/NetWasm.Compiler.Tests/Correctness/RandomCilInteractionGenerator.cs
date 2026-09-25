using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Correctness;

internal sealed class RandomCilInteractionGenerator : IRandomCilInteractionGenerator
{
    private readonly ImmutableDictionary<string, Func<InteractionContext,
        ImmutableArray<GeneratedCilInstruction>>> _data;
    private readonly ImmutableDictionary<string, Func<InteractionContext,
        ImmutableArray<GeneratedCilInstruction>>> _calls;
    private readonly ImmutableDictionary<string, Func<InteractionContext,
        ImmutableArray<GeneratedCilInstruction>>> _lifetimes;
    private readonly ImmutableDictionary<string, Func<InteractionContext,
        ImmutableArray<GeneratedCilBlock>>> _controlFlow;

    public RandomCilInteractionGenerator()
    {
        _data = new Dictionary<string, Func<InteractionContext,
            ImmutableArray<GeneratedCilInstruction>>>(StringComparer.Ordinal)
        {
            ["argument-local"] = ArgumentLocal,
            ["array"] = Array,
            ["static-array-initializer"] = StaticArrayInitializer,
            ["range-slice"] = RangeSlice,
            ["object-field"] = ObjectField,
            ["boxed-value"] = BoxedValue,
        }.ToImmutableDictionary(StringComparer.Ordinal);
        _calls = new Dictionary<string, Func<InteractionContext,
            ImmutableArray<GeneratedCilInstruction>>>(StringComparer.Ordinal)
        {
            ["none"] = _ => [],
            ["direct"] = DirectCall,
            ["virtual"] = VirtualCall,
            ["interface"] = InterfaceCall,
            ["default-interface"] = DefaultInterfaceCall,
            ["static-interface"] = StaticInterfaceCall,
            ["covariant-return"] = CovariantReturnCall,
            ["indirect"] = IndirectCall,
        }.ToImmutableDictionary(StringComparer.Ordinal);
        _lifetimes = new Dictionary<string, Func<InteractionContext,
            ImmutableArray<GeneratedCilInstruction>>>(StringComparer.Ordinal)
        {
            ["none"] = _ => [],
            ["reference-across-allocation"] = ReferenceAcrossAllocation,
        }.ToImmutableDictionary(StringComparer.Ordinal);
        _controlFlow = new Dictionary<string, Func<InteractionContext,
            ImmutableArray<GeneratedCilBlock>>>(StringComparer.Ordinal)
        {
            ["diamond"] = Diamond,
            ["switch"] = Switch,
            ["overlap"] = Overlap,
            ["backedge"] = Backedge,
        }.ToImmutableDictionary(StringComparer.Ordinal);
    }

    public GeneratedCilProgram Generate(
        RandomCilInteractionCase testCase,
        RandomCilMetadataTokens tokens)
    {
        ArgumentNullException.ThrowIfNull(testCase);
        var context = CreateContext(testCase, tokens);
        var blocks = _controlFlow[testCase.Levels["cfg"]](context).ToBuilder();
        AddCompletion(blocks, context);
        return new(
            testCase.Seed,
            CliValueKind.I4,
            [CliValueKind.I4],
            [CliValueKind.I4, CliValueKind.ManagedReference, CliValueKind.I4],
            blocks.ToImmutable())
        {
            ExceptionRegions = CreateExceptionRegions(context),
        };
    }

    private InteractionContext CreateContext(
        RandomCilInteractionCase testCase,
        RandomCilMetadataTokens tokens)
    {
        var context = new InteractionContext(testCase, tokens, [], [], 0);
        var fragments = new Dictionary<string, ImmutableArray<GeneratedCilInstruction>>(
            StringComparer.Ordinal)
        {
            ["data"] = _data[testCase.Levels["data"]](context),
            ["call"] = _calls[testCase.Levels["call"]](context),
            ["lifetime"] = _lifetimes[testCase.Levels["lifetime"]](context),
        };
        string[] order = testCase.Levels["order"] switch
        {
            "forward" => ["data", "call", "lifetime"],
            "reverse-compatible" => ["lifetime", "call", "data"],
            "interleaved" => ["call", "data", "lifetime"],
            _ => throw new InvalidDataException("unknown interaction order"),
        };
        var prelude = order.SelectMany(name => fragments[name]).ToImmutableArray();
        var path = ProtectedPath(testCase.Levels["eh"]);
        var completionStart = testCase.Levels["cfg"] switch
        {
            "diamond" => 4,
            "switch" => 7,
            "overlap" => 7,
            "backedge" => 5,
            _ => throw new InvalidDataException("unknown CFG interaction"),
        };
        return context with
        {
            Prelude = prelude,
            ProtectedPath = path,
            CompletionStart = completionStart,
        };
    }

    private static ImmutableArray<GeneratedCilBlock> Diamond(InteractionContext context)
    {
        var path = context.ProtectedPath;
        return
        [
            BlockIn(0, path,
                [.. context.Prelude,
                    I(CilOperation.LoadArgument, Index(0)),
                    I(CilOperation.BranchIfTrue, Target(2))]),
            MutationBlock(context, 1, 3, 1, finalEdge: true),
            MutationBlock(context, 2, 3, 2, finalEdge: true),
            JoinBlock(context, 3),
        ];
    }

    private static ImmutableArray<GeneratedCilBlock> Switch(InteractionContext context)
    {
        var path = context.ProtectedPath;
        var blocks = ImmutableArray.CreateBuilder<GeneratedCilBlock>();
        blocks.Add(BlockIn(0, path,
            [.. context.Prelude,
                I(CilOperation.LoadArgument, Index(0)),
                I(CilOperation.LoadInt32, Int32(3)),
                I(CilOperation.BitwiseAnd),
                I(CilOperation.Switch, new GeneratedCilOperand.BlockTargets([2, 3, 4, 5]))]));
        for (var id = 1; id <= 5; id++)
        {
            blocks.Add(MutationBlock(context, id, 6, id, finalEdge: true));
        }
        blocks.Add(JoinBlock(context, 6));
        return blocks.ToImmutable();
    }

    private static ImmutableArray<GeneratedCilBlock> Overlap(InteractionContext context)
    {
        var path = context.ProtectedPath;
        return
        [
            BlockIn(0, path,
                [.. context.Prelude,
                    I(CilOperation.LoadArgument, Index(0)),
                    I(CilOperation.BranchIfTrue, Target(2))]),
            MutationBlock(context, 1, 3, 1, finalEdge: false),
            MutationBlock(context, 2, 4, 2, finalEdge: false),
            MutationBlock(context, 3, 6, 3, finalEdge: true),
            BlockIn(4, path,
                I(CilOperation.LoadArgument, Index(0)),
                I(CilOperation.BranchIfTrue, Target(3))),
            MutationBlock(context, 5, 6, 5, finalEdge: true),
            JoinBlock(context, 6),
        ];
    }

    private static ImmutableArray<GeneratedCilBlock> Backedge(InteractionContext context)
    {
        var path = context.ProtectedPath;
        return
        [
            BlockIn(0, path,
                [.. context.Prelude,
                    I(CilOperation.LoadInt32, Int32(3)),
                    I(CilOperation.StoreLocal, Index(2)),
                    I(CilOperation.Branch, Target(1))]),
            BlockIn(1, path,
                I(CilOperation.LoadLocal, Index(2)),
                I(CilOperation.BranchIfFalse, Target(3))),
            BlockIn(2, path,
                [.. Mutation(context, 2, finalEdge: false),
                    I(CilOperation.LoadLocal, Index(2)),
                    I(CilOperation.LoadInt32, Int32(1)),
                    I(CilOperation.Subtract),
                    I(CilOperation.StoreLocal, Index(2)),
                    I(CilOperation.Branch, Target(1))]),
            MutationBlock(context, 3, 4, 3, finalEdge: true),
            JoinBlock(context, 4),
        ];
    }

    private static GeneratedCilBlock MutationBlock(
        InteractionContext context,
        int id,
        int target,
        int variant,
        bool finalEdge) => BlockIn(
        id,
        context.ProtectedPath,
        [.. Mutation(context, variant, finalEdge),
            I(CilOperation.Branch, Target(target))]);

    private static ImmutableArray<GeneratedCilInstruction> Mutation(
        InteractionContext context,
        int variant,
        bool finalEdge)
    {
        var instructions = Numeric(context, variant).ToBuilder();
        var stack = context.Case.Levels["stack"];
        if (finalEdge && stack == "value-join")
        {
            return instructions.ToImmutable();
        }
        if (stack == "local-roundtrip")
        {
            instructions.Add(I(CilOperation.StoreLocal, Index(0)));
            instructions.Add(I(CilOperation.LoadLocal, Index(0)));
        }
        else if (stack == "duplicate-pop")
        {
            instructions.Add(I(CilOperation.Duplicate));
            instructions.Add(I(CilOperation.Pop));
        }
        instructions.Add(I(CilOperation.StoreArgument, Index(0)));
        return instructions.ToImmutable();
    }

    private static ImmutableArray<GeneratedCilInstruction> Numeric(
        InteractionContext context,
        int variant)
    {
        var constant = unchecked(context.Case.Seed + variant * 17) | 1;
        return context.Case.Levels["numeric"] switch
        {
            "arithmetic" =>
            [
                I(CilOperation.LoadArgument, Index(0)),
                I(CilOperation.LoadInt32, Int32(constant)),
                I(CilOperation.Multiply),
            ],
            "checked" =>
            [
                I(CilOperation.LoadArgument, Index(0)),
                I(CilOperation.LoadInt32, Int32(constant)),
                I(CilOperation.AddChecked),
            ],
            "division" =>
            [
                I(CilOperation.LoadInt32, Int32(constant)),
                I(CilOperation.LoadArgument, Index(0)),
                I(CilOperation.Divide),
            ],
            "bitwise" =>
            [
                I(CilOperation.LoadArgument, Index(0)),
                I(CilOperation.LoadInt32, Int32(constant)),
                I(CilOperation.BitwiseXor),
            ],
            "conversion" =>
            [
                I(CilOperation.LoadArgument, Index(0)),
                I(CilOperation.ConvertNumeric, new GeneratedCilOperand.NumericConversion(
                    16,
                    DestinationUnsigned: variant % 2 == 0,
                    Checked: true,
                    SourceUnsigned: variant % 3 == 0,
                    Native: false)),
            ],
            "unsigned-finite" =>
            [
                I(CilOperation.Break),
                I(CilOperation.LoadArgument, Index(0)),
                I(CilOperation.LoadInt32, Int32(int.MaxValue)),
                I(CilOperation.BitwiseAnd),
                I(CilOperation.ConvertFloatUnsigned),
                I(CilOperation.CheckFinite),
                I(CilOperation.ConvertNumeric, new GeneratedCilOperand.NumericConversion(
                    32,
                    DestinationUnsigned: false,
                    Checked: false,
                    SourceUnsigned: false,
                    Native: false)),
            ],
            _ => throw new InvalidDataException("unknown numeric interaction"),
        };
    }

    private static GeneratedCilBlock JoinBlock(InteractionContext context, int id)
    {
        if (context.Case.Levels["stack"] == "value-join")
        {
            return BlockWithEntryIn(
                id,
                [CliValueKind.I4],
                context.ProtectedPath,
                I(CilOperation.StoreArgument, Index(0)),
                I(CilOperation.Branch, Target(context.CompletionStart)));
        }
        return BlockIn(
            id,
            context.ProtectedPath,
            I(CilOperation.Branch, Target(context.CompletionStart)));
    }

    private static void AddCompletion(
        ImmutableArray<GeneratedCilBlock>.Builder blocks,
        InteractionContext context)
    {
        var start = context.CompletionStart;
        var eh = context.Case.Levels["eh"];
        if (eh == "none")
        {
            blocks.Add(Block(start,
                I(CilOperation.LoadArgument, Index(0)),
                I(CilOperation.Return)));
            return;
        }
        if (eh == "finally")
        {
            blocks.Add(BlockIn(start, context.ProtectedPath,
                I(CilOperation.Leave, Target(start + 2))));
            blocks.Add(BlockIn(start + 1,
                [Membership(0, GeneratedExceptionRegionPart.Handler)],
                Cleanup(3)));
            blocks.Add(ReturnBlock(start + 2));
            return;
        }
        if (eh == "nested-finally")
        {
            blocks.Add(BlockIn(start, context.ProtectedPath,
                I(CilOperation.Leave, Target(start + 3))));
            blocks.Add(BlockIn(start + 1,
                [Membership(1, GeneratedExceptionRegionPart.Try),
                    Membership(0, GeneratedExceptionRegionPart.Handler)],
                Cleanup(3)));
            blocks.Add(BlockIn(start + 2,
                [Membership(1, GeneratedExceptionRegionPart.Handler)],
                Cleanup(5)));
            blocks.Add(ReturnBlock(start + 3));
            return;
        }
        if (eh == "fault-catch")
        {
            blocks.Add(BlockIn(start, context.ProtectedPath,
                I(CilOperation.LoadNull),
                I(CilOperation.Throw)));
            blocks.Add(BlockIn(start + 1,
                [Membership(1, GeneratedExceptionRegionPart.Try),
                    Membership(0, GeneratedExceptionRegionPart.Handler)],
                Cleanup(3)));
            blocks.Add(BlockWithEntryIn(
                start + 2,
                [CliValueKind.ManagedReference],
                [Membership(1, GeneratedExceptionRegionPart.Handler)],
                I(CilOperation.Pop),
                I(CilOperation.Leave, Target(start + 3))));
            blocks.Add(ReturnBlock(start + 3));
            return;
        }
        if (eh != "filter-finally")
        {
            throw new InvalidDataException("unknown EH interaction");
        }
        blocks.Add(BlockIn(start, context.ProtectedPath,
            I(CilOperation.LoadNull),
            I(CilOperation.Throw)));
        blocks.Add(BlockWithEntryIn(
            start + 1,
            [CliValueKind.ManagedReference],
            [Membership(1, GeneratedExceptionRegionPart.Try),
                Membership(0, GeneratedExceptionRegionPart.Filter)],
            I(CilOperation.Pop),
            I(CilOperation.LoadArgument, Index(0)),
            I(CilOperation.LoadInt32, Int32(Math.Abs(context.Case.Seed % 7))),
            I(CilOperation.CompareGreaterThanSigned),
            I(CilOperation.EndFilter)));
        blocks.Add(BlockWithEntryIn(
            start + 2,
            [CliValueKind.ManagedReference],
            [Membership(1, GeneratedExceptionRegionPart.Try),
                Membership(0, GeneratedExceptionRegionPart.Handler)],
            I(CilOperation.Pop),
            I(CilOperation.LoadArgument, Index(0)),
            I(CilOperation.LoadInt32, Int32(7)),
            I(CilOperation.Add),
            I(CilOperation.StoreArgument, Index(0)),
            I(CilOperation.Leave, Target(start + 4))));
        blocks.Add(BlockIn(start + 3,
            [Membership(1, GeneratedExceptionRegionPart.Handler)],
            Cleanup(5)));
        blocks.Add(ReturnBlock(start + 4));
    }

    private static ImmutableArray<GeneratedCilExceptionRegion> CreateExceptionRegions(
        InteractionContext context)
    {
        var start = context.CompletionStart;
        return context.Case.Levels["eh"] switch
        {
            "none" => [],
            "finally" =>
            [
                new(0, CilExceptionRegionKind.Finally, 0, start + 1, start + 1,
                    start + 2),
            ],
            "nested-finally" =>
            [
                new(0, CilExceptionRegionKind.Finally, 0, start + 1, start + 1,
                    start + 2),
                new(1, CilExceptionRegionKind.Finally, 0, start + 2, start + 2,
                    start + 3),
            ],
            "filter-finally" =>
            [
                new(0, CilExceptionRegionKind.Filter, 0, start + 1, start + 2,
                    start + 3, FilterStartBlock: start + 1),
                new(1, CilExceptionRegionKind.Finally, 0, start + 3, start + 3,
                    start + 4),
            ],
            "fault-catch" =>
            [
                new(0, CilExceptionRegionKind.Fault, 0, start + 1, start + 1,
                    start + 2),
                new(1, CilExceptionRegionKind.Catch, 0, start + 2, start + 2,
                    start + 3, CatchTypeToken: context.Tokens.ExceptionType),
            ],
            _ => throw new InvalidDataException("unknown EH interaction"),
        };
    }

    private static ImmutableArray<GeneratedExceptionRegionMembership> ProtectedPath(
        string eh) => eh switch
        {
            "none" => [],
            "finally" => [Membership(0, GeneratedExceptionRegionPart.Try)],
            "nested-finally" =>
            [
                Membership(1, GeneratedExceptionRegionPart.Try),
            Membership(0, GeneratedExceptionRegionPart.Try),
        ],
            "filter-finally" =>
            [
                Membership(1, GeneratedExceptionRegionPart.Try),
            Membership(0, GeneratedExceptionRegionPart.Try),
        ],
            "fault-catch" =>
            [
                Membership(1, GeneratedExceptionRegionPart.Try),
                Membership(0, GeneratedExceptionRegionPart.Try),
            ],
            _ => throw new InvalidDataException("unknown EH interaction"),
        };

    private static ImmutableArray<GeneratedCilInstruction> ArgumentLocal(
        InteractionContext _) =>
    [
        I(CilOperation.LoadArgument, Index(0)),
        I(CilOperation.StoreLocal, Index(0)),
        I(CilOperation.LoadLocal, Index(0)),
        I(CilOperation.StoreArgument, Index(0)),
    ];

    private static ImmutableArray<GeneratedCilInstruction> Array(
        InteractionContext context) =>
    [
        I(CilOperation.LoadInt32, Int32(1)),
        I(CilOperation.NewArray, context.Int32Type),
        I(CilOperation.Duplicate),
        I(CilOperation.LoadInt32, Int32(0)),
        I(CilOperation.LoadArgument, Index(0)),
        I(CilOperation.StoreArrayElement, context.Int32Type),
        I(CilOperation.LoadInt32, Int32(0)),
        I(CilOperation.Readonly),
        I(CilOperation.LoadArrayElementAddress, context.Int32Type),
        I(CilOperation.Unaligned, Index(1)),
        I(CilOperation.LoadObject, context.Int32Type),
        I(CilOperation.StoreArgument, Index(0)),
    ];

    private static ImmutableArray<GeneratedCilInstruction> ObjectField(
        InteractionContext context) =>
    [
        I(CilOperation.NewObject, context.Constructor),
        I(CilOperation.StoreLocal, Index(1)),
        I(CilOperation.LoadLocal, Index(1)),
        I(CilOperation.LoadArgument, Index(0)),
        I(CilOperation.StoreField, context.ValueField),
        I(CilOperation.LoadLocal, Index(1)),
        I(CilOperation.LoadField, context.ValueField),
        I(CilOperation.StoreArgument, Index(0)),
    ];

    private static ImmutableArray<GeneratedCilInstruction> StaticArrayInitializer(
        InteractionContext context) =>
    [
        I(CilOperation.LoadInt32, Int32(5)),
        I(CilOperation.NewArray, context.Int32Type),
        I(CilOperation.Duplicate),
        I(CilOperation.LoadFieldToken, context.ArrayInitializerField),
        I(CilOperation.Call, context.InitializeArrayMethod),
        I(CilOperation.LoadInt32, Int32(Math.Abs(context.Case.Seed % 5))),
        I(CilOperation.LoadArrayElement, context.Int32Type),
        I(CilOperation.StoreArgument, Index(0)),
    ];

    private static ImmutableArray<GeneratedCilInstruction> BoxedValue(
        InteractionContext context) =>
    [
        I(CilOperation.LoadArgument, Index(0)),
        I(CilOperation.Box, context.Int32Type),
        I(CilOperation.UnboxAny, context.Int32Type),
        I(CilOperation.StoreArgument, Index(0)),
    ];

    private static ImmutableArray<GeneratedCilInstruction> RangeSlice(
        InteractionContext context) =>
    [
        I(CilOperation.LoadArgument, Index(0)),
        I(CilOperation.Call, context.RangeSliceMethod),
        I(CilOperation.StoreArgument, Index(0)),
    ];

    private static ImmutableArray<GeneratedCilInstruction> DirectCall(
        InteractionContext context) =>
    [
        I(CilOperation.LoadArgument, Index(0)),
        I(CilOperation.LoadInt32, Int32(context.Case.Seed)),
        I(CilOperation.Call, context.Helper),
        I(CilOperation.StoreArgument, Index(0)),
    ];

    private static ImmutableArray<GeneratedCilInstruction> VirtualCall(
        InteractionContext context) =>
    [
        I(CilOperation.NewObject, context.Constructor),
        I(CilOperation.LoadArgument, Index(0)),
        I(CilOperation.CallVirtual, context.AddMethod),
        I(CilOperation.StoreArgument, Index(0)),
    ];

    private static ImmutableArray<GeneratedCilInstruction> InterfaceCall(
        InteractionContext context) =>
    [
        I(CilOperation.NewObject, context.Constructor),
        I(CilOperation.LoadArgument, Index(0)),
        I(CilOperation.CallVirtual, context.InterfaceAddMethod),
        I(CilOperation.StoreArgument, Index(0)),
    ];

    private static ImmutableArray<GeneratedCilInstruction> StaticInterfaceCall(
        InteractionContext context) =>
    [
        I(CilOperation.LoadArgument, Index(0)),
        I(CilOperation.Constrained, context.StaticTransformType),
        I(CilOperation.Call, context.StaticApplyMethod),
        I(CilOperation.StoreArgument, Index(0)),
    ];

    private static ImmutableArray<GeneratedCilInstruction> DefaultInterfaceCall(
        InteractionContext context) =>
    [
        I(CilOperation.NewObject, context.DefaultBoxConstructor),
        I(CilOperation.LoadArgument, Index(0)),
        I(CilOperation.CallVirtual, context.DefaultAddMethod),
        I(CilOperation.StoreArgument, Index(0)),
    ];

    private static ImmutableArray<GeneratedCilInstruction> CovariantReturnCall(
        InteractionContext context) =>
    [
        I(CilOperation.NewObject, context.DerivedBoxConstructor),
        I(CilOperation.CallVirtual, context.BaseCopyMethod),
        I(CilOperation.CallVirtual, context.BaseReadMethod),
        I(CilOperation.LoadArgument, Index(0)),
        I(CilOperation.Add),
        I(CilOperation.StoreArgument, Index(0)),
    ];

    private static ImmutableArray<GeneratedCilInstruction> IndirectCall(
        InteractionContext context) =>
    [
        I(CilOperation.LoadArgument, Index(0)),
        I(CilOperation.LoadFunction, context.UnaryMethod),
        I(CilOperation.CallIndirect, context.UnaryCallSite),
        I(CilOperation.StoreArgument, Index(0)),
    ];

    private static ImmutableArray<GeneratedCilInstruction> ReferenceAcrossAllocation(
        InteractionContext context) =>
    [
        I(CilOperation.NewObject, context.Constructor),
        I(CilOperation.StoreLocal, Index(1)),
        I(CilOperation.LoadLocal, Index(1)),
        I(CilOperation.LoadArgument, Index(0)),
        I(CilOperation.StoreField, context.ValueField),
        I(CilOperation.NewObject, context.Constructor),
        I(CilOperation.Pop),
        I(CilOperation.LoadLocal, Index(1)),
        I(CilOperation.LoadField, context.ValueField),
        I(CilOperation.StoreArgument, Index(0)),
    ];

    private static GeneratedCilInstruction[] Cleanup(int constant) =>
    [
        I(CilOperation.LoadArgument, Index(0)),
        I(CilOperation.LoadInt32, Int32(constant)),
        I(CilOperation.Add),
        I(CilOperation.StoreArgument, Index(0)),
        I(CilOperation.EndFinally),
    ];

    private static GeneratedCilBlock ReturnBlock(int id) => Block(id,
        I(CilOperation.LoadArgument, Index(0)),
        I(CilOperation.Return));

    private static GeneratedCilBlock Block(
        int id,
        params GeneratedCilInstruction[] instructions) => new(id, [], [], [.. instructions]);

    private static GeneratedCilBlock BlockIn(
        int id,
        ImmutableArray<GeneratedExceptionRegionMembership> path,
        params GeneratedCilInstruction[] instructions) => new(id, [], path, [.. instructions]);

    private static GeneratedCilBlock BlockWithEntryIn(
        int id,
        ImmutableArray<CliValueKind> stack,
        ImmutableArray<GeneratedExceptionRegionMembership> path,
        params GeneratedCilInstruction[] instructions) => new(id, stack, path, [.. instructions]);

    private static GeneratedCilInstruction I(
        CilOperation operation,
        GeneratedCilOperand? operand = null) => new(
        operation,
        operand ?? new GeneratedCilOperand.None());

    private static GeneratedCilOperand.Index Index(int value) => new(value);

    private static GeneratedCilOperand.Int32 Int32(int value) => new(value);

    private static GeneratedCilOperand.BlockTarget Target(int value) => new(value);

    private static GeneratedExceptionRegionMembership Membership(
        int region,
        GeneratedExceptionRegionPart part) => new(region, part);

    private sealed record InteractionContext(
        RandomCilInteractionCase Case,
        RandomCilMetadataTokens Tokens,
        ImmutableArray<GeneratedCilInstruction> Prelude,
        ImmutableArray<GeneratedExceptionRegionMembership> ProtectedPath,
        int CompletionStart)
    {
        public GeneratedCilOperand.MetadataToken Int32Type => TypeToken(
            Tokens.Int32Type,
            CliValueKind.I4);

        public GeneratedCilOperand.MetadataToken Constructor => MethodToken(
            Tokens.BoxConstructor,
            [],
            CliValueKind.ManagedReference,
            isStatic: false,
            isConstructor: true);

        public GeneratedCilOperand.MetadataToken AddMethod => MethodToken(
            Tokens.BoxAddMethod,
            [CliValueKind.I4],
            CliValueKind.I4,
            isStatic: false,
            isVirtual: true);

        public GeneratedCilOperand.MetadataToken InterfaceAddMethod => MethodToken(
            Tokens.InterfaceAddMethod,
            [CliValueKind.I4],
            CliValueKind.I4,
            isStatic: false,
            isVirtual: true);

        public GeneratedCilOperand.MetadataToken StaticTransformType => TypeToken(
            Tokens.StaticTransformType,
            CliValueKind.ValueType);

        public GeneratedCilOperand.MetadataToken StaticApplyMethod => MethodToken(
            Tokens.StaticApplyMethod,
            [CliValueKind.I4],
            CliValueKind.I4,
            isStatic: true);

        public GeneratedCilOperand.MetadataToken DefaultBoxConstructor => MethodToken(
            Tokens.DefaultBoxConstructor,
            [],
            CliValueKind.ManagedReference,
            isStatic: false,
            isConstructor: true);

        public GeneratedCilOperand.MetadataToken DefaultAddMethod => MethodToken(
            Tokens.DefaultAddMethod,
            [CliValueKind.I4],
            CliValueKind.I4,
            isStatic: false,
            isVirtual: true);

        public GeneratedCilOperand.MetadataToken DerivedBoxConstructor => MethodToken(
            Tokens.DerivedBoxConstructor,
            [],
            CliValueKind.ManagedReference,
            isStatic: false,
            isConstructor: true);

        public GeneratedCilOperand.MetadataToken BaseCopyMethod => MethodToken(
            Tokens.BaseCopyMethod,
            [],
            CliValueKind.ManagedReference,
            isStatic: false,
            isVirtual: true);

        public GeneratedCilOperand.MetadataToken BaseReadMethod => MethodToken(
            Tokens.BaseReadMethod,
            [],
            CliValueKind.I4,
            isStatic: false,
            isVirtual: true);

        public GeneratedCilOperand.MetadataToken ArrayInitializerField => new(
            Tokens.ArrayInitializerField,
            CliValueKind.I4,
            [],
            CliValueKind.Void,
            IsStatic: true,
            IsConstructor: false);

        public GeneratedCilOperand.MetadataToken InitializeArrayMethod => MethodToken(
            Tokens.InitializeArrayMethod,
            [CliValueKind.ManagedReference, CliValueKind.I4],
            CliValueKind.Void,
            isStatic: true);

        public GeneratedCilOperand.MetadataToken Helper => MethodToken(
            Tokens.HelperMethod,
            [CliValueKind.I4, CliValueKind.I4],
            CliValueKind.I4,
            isStatic: true);

        public GeneratedCilOperand.MetadataToken RangeSliceMethod => MethodToken(
            Tokens.RangeSliceMethod,
            [CliValueKind.I4],
            CliValueKind.I4,
            isStatic: true);

        public GeneratedCilOperand.MetadataToken UnaryMethod => MethodToken(
            Tokens.HelperUnaryMethod,
            [CliValueKind.I4],
            CliValueKind.I4,
            isStatic: true);

        public GeneratedCilOperand.MetadataToken UnaryCallSite => MethodToken(
            Tokens.UnaryCallSite,
            [CliValueKind.I4],
            CliValueKind.I4,
            isStatic: true);

        public GeneratedCilOperand.MetadataToken ValueField => new(
            Tokens.BoxValueField,
            CliValueKind.I4,
            [],
            CliValueKind.Void,
            IsStatic: false,
            IsConstructor: false);

        private static GeneratedCilOperand.MetadataToken TypeToken(
            int token,
            CliValueKind kind) => new(
            token,
            kind,
            [],
            CliValueKind.Void,
            IsStatic: true,
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
    }
}
