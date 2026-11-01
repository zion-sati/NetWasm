using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

public sealed class StaticDataViewEliminatorTests
{
    [Fact]
    public void ConservativePolicyEliminatesTheCompleteNonescapingUse()
    {
        var fixture = new Fixture();
        var method = fixture.Managed();

        var optimized = fixture.Optimizer().Optimize(method);

        Assert.Equal(
            [
                CilOperation.Nop,
                CilOperation.Nop,
                CilOperation.Nop,
                CilOperation.LoadArgument,
                CilOperation.Nop,
                CilOperation.LoadStaticDataElement,
                CilOperation.Return,
            ],
            optimized.Instructions.Select(instruction => instruction.Operation));
        Assert.Same(
            fixture.View,
            optimized.Instructions.Single(instruction =>
                instruction.Operation == CilOperation.LoadStaticDataElement).Operand);
    }

    [Fact]
    public void DisabledPolicyPreservesTheOriginalBody()
    {
        var fixture = new Fixture();
        var method = fixture.Managed();

        var optimized = fixture.Optimizer(
            StaticDataViewEliminationPolicy.Disabled).Optimize(method);

        Assert.Same(method.Body, optimized);
    }

    [Fact]
    public void ReapplyingTheTransformPreservesTheTransformedBody()
    {
        var fixture = new Fixture();
        var optimizer = fixture.Optimizer();
        var first = optimizer.Optimize(fixture.Managed());
        var stacks = first.Instructions.ToImmutableDictionary(
            instruction => instruction.Offset,
            instruction => instruction.Offset switch
            {
                4 or 5 or 6 => ImmutableArray.Create(CliValueKind.I4),
                _ => ImmutableArray<CliValueKind>.Empty,
            });

        var second = optimizer.Optimize(fixture.Managed(first, stacks));

        Assert.Same(first, second);
    }

    [Fact]
    public void UnprovenProducerPreservesTheOriginalBody()
    {
        var fixture = new Fixture(proveView: false);
        var method = fixture.Managed();

        var optimized = fixture.Optimizer().Optimize(method);

        Assert.Same(method.Body, optimized);
    }

    [Fact]
    public void UnresolvedProducerPreservesTheOriginalBody()
    {
        var fixture = new Fixture();
        var method = fixture.Managed();

        var optimized = fixture.Optimizer(
            calledMethods: new UnresolvedCalledMethodResolver()).Optimize(method);

        Assert.Same(method.Body, optimized);
    }

    [Fact]
    public void ViewStoredInAnotherLocalTypePreservesTheOriginalBody()
    {
        var fixture = new Fixture();
        var body = fixture.Body() with
        {
            LocalSignatureTypes =
            [
                fixture.ElementType,
                CliTypeIdentity.Primitive("i4", CliValueKind.I4),
            ],
        };
        var method = fixture.Managed(body);

        var optimized = fixture.Optimizer().Optimize(method);

        Assert.Same(method.Body, optimized);
    }

    [Fact]
    public void AddressOfAnotherLocalPreservesTheOriginalBody()
    {
        var fixture = new Fixture();
        var instructions = fixture.Instructions.ToArray();
        instructions[2] = instructions[2] with
        {
            Operand = new CilOperand.Index(1),
        };
        var method = fixture.Managed(fixture.Body([.. instructions]));

        var optimized = fixture.Optimizer().Optimize(method);

        Assert.Same(method.Body, optimized);
    }

    [Fact]
    public void AnyOtherUseOfTheTemporaryPreservesEveryCandidate()
    {
        var fixture = new Fixture();
        var body = fixture.Body([
            .. fixture.Instructions[..^1],
            new(6, 7, CilOperation.LoadLocalAddress, new CilOperand.Index(0)),
            new(7, 8, CilOperation.Pop, new CilOperand.None()),
            new(8, 9, CilOperation.Return, new CilOperand.None()),
        ]);
        var method = fixture.Managed(body);

        var optimized = fixture.Optimizer().Optimize(method);

        Assert.Same(method.Body, optimized);
    }

    [Fact]
    public void EffectfulIndexExpressionPreservesTheOriginalBody()
    {
        var fixture = new Fixture();
        var instructions = fixture.Instructions.ToArray();
        instructions[3] = instructions[3] with
        {
            Operation = CilOperation.Call,
            Operand = new CilOperand.MethodInstance(fixture.OtherCall),
        };
        var method = fixture.Managed(fixture.Body([.. instructions]));

        var optimized = fixture.Optimizer().Optimize(method);

        Assert.Same(method.Body, optimized);
    }

    [Theory]
    [InlineData(CilOperation.LoadArgument)]
    [InlineData(CilOperation.LoadLocal)]
    [InlineData(CilOperation.LoadInt32)]
    [InlineData(CilOperation.Add)]
    [InlineData(CilOperation.Subtract)]
    [InlineData(CilOperation.Multiply)]
    [InlineData(CilOperation.BitwiseAnd)]
    [InlineData(CilOperation.BitwiseOr)]
    [InlineData(CilOperation.BitwiseXor)]
    [InlineData(CilOperation.ShiftLeft)]
    [InlineData(CilOperation.ShiftRightSigned)]
    [InlineData(CilOperation.ShiftRightUnsigned)]
    [InlineData(CilOperation.Negate)]
    [InlineData(CilOperation.OnesComplement)]
    [InlineData(CilOperation.ConvertInt32)]
    [InlineData(CilOperation.ConvertInt32Unsigned)]
    public void EveryPureIntegerIndexOperationCanRemainBetweenViewAndIndexer(
        CilOperation operation)
    {
        var fixture = new Fixture();
        (var body, var stacks) = fixture.IndexExpressionBody(operation);
        var method = fixture.Managed(body, stacks);

        var optimized = fixture.Optimizer().Optimize(method);

        Assert.Contains(optimized.Instructions, instruction =>
            instruction.Operation == CilOperation.LoadStaticDataElement);
    }

    [Fact]
    public void NopsBetweenTheReceiverAndIndexerAreIgnored()
    {
        var fixture = new Fixture();
        (var body, var stacks) = fixture.IndexExpressionBody(
            CilOperation.LoadArgument,
            includeNop: true);

        var optimized = fixture.Optimizer().Optimize(
            fixture.Managed(body, stacks));

        Assert.Contains(optimized.Instructions, instruction =>
            instruction.Operation == CilOperation.LoadStaticDataElement);
    }

    [Fact]
    public void NopsBetweenCandidateComponentsAreIgnored()
    {
        var fixture = new Fixture();
        (var body, var stacks) = fixture.NopSeparatedCandidate();

        var optimized = fixture.Optimizer().Optimize(
            fixture.Managed(body, stacks));

        Assert.Contains(optimized.Instructions, instruction =>
            instruction.Operation == CilOperation.LoadStaticDataElement);
    }

    [Theory]
    [InlineData(IndexerPathFault.DisallowedOperation)]
    [InlineData(IndexerPathFault.ShortEntryStack)]
    [InlineData(IndexerPathFault.WrongReceiverKind)]
    public void UnprovenIndexerPathsPreserveTheOriginalBody(IndexerPathFault fault)
    {
        var fixture = new Fixture();
        (var body, var stacks) = fixture.FaultingIndexerPath(fault);
        var method = fixture.Managed(body, stacks);

        var optimized = fixture.Optimizer().Optimize(method);

        Assert.Same(method.Body, optimized);
    }

    [Fact]
    public void IndexArithmeticCannotConsumeAndReplaceTheViewReceiver()
    {
        var fixture = new Fixture();
        (var body, var stacks) = fixture.ReceiverConsumingIndexExpression();
        var method = fixture.Managed(body, stacks);

        var optimized = fixture.Optimizer().Optimize(method);

        Assert.Same(method.Body, optimized);
    }

    [Fact]
    public void ProducerEndingAtAControlFlowBoundaryIsNotCombinedAcrossBlocks()
    {
        var fixture = new Fixture();
        (var body, var stacks) = fixture.SplitProducerAcrossBlocks();
        var method = fixture.Managed(body, stacks);

        var optimized = fixture.Optimizer().Optimize(method);

        Assert.Same(method.Body, optimized);
    }

    [Fact]
    public void IndexExpressionEndingAtAControlFlowBoundaryIsNotCombinedAcrossBlocks()
    {
        var fixture = new Fixture();
        (var body, var stacks) = fixture.SplitIndexExpressionAcrossBlocks();
        var method = fixture.Managed(body, stacks);

        var optimized = fixture.Optimizer().Optimize(method);

        Assert.Same(method.Body, optimized);
    }

    [Fact]
    public void ModifiedByReferenceIndexerResultRemainsEligible()
    {
        var fixture = new Fixture();
        var modifier = CliTypeIdentity.Named(
            new AssemblyIdentity("fixture"),
            "Fixture",
            "Modifier",
            isValueType: false);
        var indexer = fixture.IndexerWithReturnType(CliTypeIdentity.Modified(
            CliTypeIdentity.ManagedByReference(fixture.ElementType),
            modifier,
            isRequired: true));
        var instructions = fixture.Instructions.ToArray();
        instructions[4] = instructions[4] with
        {
            Operand = new CilOperand.MethodInstance(indexer),
        };

        var optimized = fixture.Optimizer().Optimize(
            fixture.Managed(fixture.Body([.. instructions])));

        Assert.Contains(optimized.Instructions, instruction =>
            instruction.Operation == CilOperation.LoadStaticDataElement);
    }

    [Fact]
    public void IndexerReturningAValuePreservesTheOriginalBody()
    {
        var fixture = new Fixture();
        var indexer = fixture.IndexerWithReturnType(fixture.ElementType);
        var instructions = fixture.Instructions.ToArray();
        instructions[4] = instructions[4] with
        {
            Operand = new CilOperand.MethodInstance(indexer),
        };
        var method = fixture.Managed(fixture.Body([.. instructions]));

        var optimized = fixture.Optimizer().Optimize(method);

        Assert.Same(method.Body, optimized);
    }

    [Theory]
    [InlineData("bool", "u1", true)]
    [InlineData("char", "u2", true)]
    [InlineData("u4", "i4", false)]
    [InlineData("u1", "i1", false)]
    public void ElementLoadOfTheSameBytesAndValueIsEligible(string element, string loaded, bool eligible)
    {
        var fixture = new Fixture(element: element);
        var instructions = fixture.Instructions.ToArray();
        instructions[5] = instructions[5] with
        {
            Operand = new CilOperand.TypeIdentity(CliTypeIdentity.Primitive(loaded, CliValueKind.I4)),
        };

        var optimized = fixture.Optimizer().Optimize(fixture.Managed(fixture.Body([.. instructions])));

        Assert.Equal(eligible, optimized.Instructions.Any(static instruction =>
            instruction.Operation == CilOperation.LoadStaticDataElement));
    }

    // A table's length guard, (uint)i < (uint)Table.Length, reads the view's
    // length, which is the data's element count.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LengthOfTheViewBecomesItsElementCount(bool field)
    {
        var fixture = new Fixture();
        var body = fixture.LengthBody(fixture.Length());
        if (field)
        {
            body = body with
            {
                Instructions = body.Instructions.SetItem(3, body.Instructions[3] with
                {
                    Operation = CilOperation.LoadField,
                    Operand = new CilOperand.FieldInstance(fixture.LengthField("_length")),
                }),
            };
        }
        var optimized = fixture.Optimizer().Optimize(fixture.Managed(body, Fixture.LengthStacks()));

        Assert.Equal(
            [CilOperation.Nop, CilOperation.Nop, CilOperation.Nop, CilOperation.LoadInt32, CilOperation.Return],
            optimized.Instructions.Select(static instruction => instruction.Operation));
        Assert.Equal(new CilOperand.ConstantI4(fixture.View.ElementCount), optimized.Instructions[3].Operand);
    }

    public enum LengthFault { Static, Parameter, OtherSpan, OtherMember }

    [Theory]
    [InlineData(LengthFault.Static)]
    [InlineData(LengthFault.Parameter)]
    [InlineData(LengthFault.OtherSpan)]
    [InlineData(LengthFault.OtherMember)]
    public void AnotherCallOnTheViewPreservesTheOriginalBody(LengthFault fault)
    {
        var fixture = new Fixture();
        var method = fixture.Managed(fixture.LengthBody(fixture.Length(fault)), Fixture.LengthStacks());

        Assert.Same(method.Body, fixture.Optimizer().Optimize(method));
    }

    [Theory]
    [InlineData(CilOperation.Call)]
    [InlineData(CilOperation.LoadField)]
    public void AnUnresolvedReadOfTheViewPreservesTheOriginalBody(CilOperation operation)
    {
        var fixture = new Fixture();
        var body = fixture.LengthBody(fixture.Length());
        body = body with
        {
            Instructions = body.Instructions.SetItem(3, body.Instructions[3] with
            {
                Operation = operation,
                Operand = new CilOperand.Entity(new(new AssemblyIdentity("fixture"), 8)),
            }),
        };
        var method = fixture.Managed(body, Fixture.LengthStacks());

        Assert.Same(method.Body, fixture.Optimizer().Optimize(method));
    }

    [Theory]
    [InlineData("_reference", false)]
    [InlineData("_length", true)]
    public void AnotherFieldOfTheViewPreservesTheOriginalBody(string name, bool otherSpan)
    {
        var fixture = new Fixture();
        var body = fixture.LengthBody(fixture.Length());
        body = body with
        {
            Instructions = body.Instructions.SetItem(3, body.Instructions[3] with
            {
                Operation = CilOperation.LoadField,
                Operand = new CilOperand.FieldInstance(fixture.LengthField(name, otherSpan)),
            }),
        };
        var method = fixture.Managed(body, Fixture.LengthStacks());

        Assert.Same(method.Body, fixture.Optimizer().Optimize(method));
    }

    [Fact]
    public void InvalidPolicyAndNullInputFailBeforeAnalysis()
    {
        var fixture = new Fixture();

        Assert.Throws<ArgumentOutOfRangeException>(() => fixture.Optimizer(
            (StaticDataViewEliminationPolicy)255));
        Assert.Throws<ArgumentNullException>(() => fixture.Optimizer().Optimize(null!));
    }

    public enum IndexerPathFault
    {
        DisallowedOperation,
        ShortEntryStack,
        WrongReceiverKind,
    }

    private sealed class Fixture
    {
        private readonly AssemblyIdentity _assembly = new("fixture");
        private readonly MethodDefinitionModel _caller;
        private readonly MethodInstanceModel _callerInstance;
        private readonly FixedViewResolver _views;

        public Fixture(bool proveView = true, string element = "u4")
        {
            ElementType = CliTypeIdentity.Primitive(element, CliValueKind.I4);
            SpanType = CliTypeIdentity.GenericInstantiation(
                CliTypeIdentity.Named(
                    _assembly,
                    "System",
                    "ReadOnlySpan`1",
                    isValueType: true),
                [ElementType]);
            var owner = CliTypeIdentity.Named(
                _assembly,
                "Fixture",
                "Owner",
                isValueType: false);
            var getterDefinition = new MethodDefinitionModel(
                Key(1),
                Key(20),
                "get_Data",
                true,
                MethodSignatureModel.Create(SpanType),
                1);
            Getter = new(getterDefinition, owner, [], getterDefinition.Signature);
            var indexerDefinition = new MethodDefinitionModel(
                Key(2),
                Key(21),
                "get_Item",
                false,
                MethodSignatureModel.Create(
                    CliTypeIdentity.ManagedByReference(ElementType),
                    CliTypeIdentity.Primitive("i4", CliValueKind.I4)),
                1)
            {
                IsPublic = true,
            };
            Indexer = new(indexerDefinition, SpanType, [], indexerDefinition.Signature);
            var otherDefinition = new MethodDefinitionModel(
                Key(3),
                Key(20),
                "Compute",
                true,
                MethodSignatureModel.Create(CliValueKind.I4),
                1);
            OtherCall = new(otherDefinition, owner, [], otherDefinition.Signature);
            var fieldDefinition = new FieldDefinitionModel(
                Key(4),
                Key(22),
                "Data",
                CliTypeIdentity.Named(
                    _assembly,
                    "",
                    "__StaticArrayInitTypeSize8",
                    isValueType: true),
                true)
            {
                InitialData = [1, 0, 0, 0, 2, 0, 0, 0],
            };
            View = new(
                new(fieldDefinition, owner, fieldDefinition.SignatureType),
                SpanType,
                ElementType,
                2);
            _views = new(Getter, proveView ? View : null);
            _caller = new(
                Key(5),
                Key(20),
                "Run",
                true,
                MethodSignatureModel.Create(
                    CliValueKind.I4,
                    CliValueKind.I4),
                1);
            _callerInstance = new(_caller, owner, [], _caller.Signature);
            Instructions =
            [
                new(0, 1, CilOperation.Call, new CilOperand.MethodInstance(Getter)),
                new(1, 2, CilOperation.StoreLocal, new CilOperand.Index(0)),
                new(2, 3, CilOperation.LoadLocalAddress, new CilOperand.Index(0)),
                new(3, 4, CilOperation.LoadArgument, new CilOperand.Index(0)),
                new(4, 5, CilOperation.Call, new CilOperand.MethodInstance(Indexer)),
                new(5, 6, CilOperation.LoadObject, new CilOperand.TypeIdentity(ElementType)),
                new(6, 7, CilOperation.Return, new CilOperand.None()),
            ];
        }

        public CliTypeIdentity ElementType { get; }
        public CliTypeIdentity SpanType { get; }
        public MethodInstanceModel Getter { get; }
        public MethodInstanceModel Indexer { get; }
        public MethodInstanceModel OtherCall { get; }
        public CilOperand.ReadOnlySpanData View { get; }
        public ImmutableArray<CilInstruction> Instructions { get; }

        public MethodInstanceModel Length(LengthFault? fault = null)
        {
            var signature = fault == LengthFault.Parameter
                ? MethodSignatureModel.Create(CliTypeIdentity.Primitive("i4", CliValueKind.I4), CliTypeIdentity.Primitive("i4", CliValueKind.I4))
                : MethodSignatureModel.Create(CliTypeIdentity.Primitive("i4", CliValueKind.I4));
            var definition = new MethodDefinitionModel(
                Key(6), Key(21), fault == LengthFault.OtherMember ? "get_IsEmpty" : "get_Length",
                fault == LengthFault.Static, signature, 1);
            var declaring = fault == LengthFault.OtherSpan
                ? CliTypeIdentity.GenericInstantiation(
                    CliTypeIdentity.Named(_assembly, "System", "Span`1", isValueType: true), [ElementType])
                : SpanType;
            return new(definition, declaring, [], signature);
        }

        public FieldInstanceModel LengthField(string name, bool otherSpan = false)
        {
            var definition = new FieldDefinitionModel(Key(7), Key(21), name, CliTypeIdentity.Primitive("i4", CliValueKind.I4), false);
            return new(
                definition,
                otherSpan
                    ? CliTypeIdentity.GenericInstantiation(
                        CliTypeIdentity.Named(_assembly, "System", "Span`1", isValueType: true), [ElementType])
                    : SpanType,
                definition.SignatureType);
        }

        public CilMethodBody LengthBody(MethodInstanceModel length) => Body(
        [
            new(0, 1, CilOperation.Call, new CilOperand.MethodInstance(Getter)),
            new(1, 2, CilOperation.StoreLocal, new CilOperand.Index(0)),
            new(2, 3, CilOperation.LoadLocalAddress, new CilOperand.Index(0)),
            new(3, 4, CilOperation.Call, new CilOperand.MethodInstance(length)),
            new(4, 5, CilOperation.Return, new CilOperand.None()),
        ]);

        public static ImmutableDictionary<int, ImmutableArray<CliValueKind>> LengthStacks() =>
            new Dictionary<int, ImmutableArray<CliValueKind>>
            {
                [0] = [],
                [1] = [CliValueKind.ValueType],
                [2] = [],
                [3] = [CliValueKind.ManagedAddress],
                [4] = [CliValueKind.I4],
            }.ToImmutableDictionary();

        public MethodInstanceModel IndexerWithReturnType(CliTypeIdentity returnType)
        {
            var signature = MethodSignatureModel.Create(
                returnType,
                CliTypeIdentity.Primitive("i4", CliValueKind.I4));
            var definition = Indexer.Definition with { Signature = signature };
            return new(definition, SpanType, [], signature);
        }

        public StaticDataViewEliminator Optimizer(
            StaticDataViewEliminationPolicy policy =
                StaticDataViewEliminationPolicy.Conservative,
            ICalledMethodResolver? calledMethods = null) => new(
                    calledMethods ?? new OperandCalledMethodResolver(),
                    _views,
                    policy);

        public CilMethodBody Body(ImmutableArray<CilInstruction>? instructions = null) =>
            new(
                _caller,
                3,
                [CliValueKind.ValueType, CliValueKind.I4],
                instructions ?? Instructions)
            {
                MethodInstance = _callerInstance,
                LocalSignatureTypes =
                [
                    SpanType,
                    CliTypeIdentity.Primitive("i4", CliValueKind.I4),
                ],
            };

        public ManagedMethodBody Managed(
            CilMethodBody? body = null,
            ImmutableDictionary<int, ImmutableArray<CliValueKind>>? entryStacks = null)
        {
            var actual = body ?? Body();
            var graph = new ControlFlowGraphBuilderFactory().Create().Build(actual);
            var stacks = entryStacks ?? actual.Instructions.ToImmutableDictionary(
                instruction => instruction.Offset,
                instruction => instruction.Offset switch
                {
                    0 => ImmutableArray<CliValueKind>.Empty,
                    1 => [CliValueKind.ValueType],
                    2 => [],
                    3 => [CliValueKind.ManagedAddress],
                    4 => [CliValueKind.ManagedAddress, CliValueKind.I4],
                    5 => [CliValueKind.ManagedAddress],
                    6 when actual.Instructions.Length == 7 => [CliValueKind.I4],
                    _ => [],
                });
            var entries = graph.Blocks.ToImmutableDictionary(
                block => block.Index,
                block => stacks[block.StartOffset]);
            return new(
                _callerInstance,
                new(graph, entries, stacks));
        }

        public (
            CilMethodBody Body,
            ImmutableDictionary<int, ImmutableArray<CliValueKind>> Stacks)
            IndexExpressionBody(
                CilOperation operation,
                bool includeNop = false)
        {
            var instructions = ImmutableArray.CreateBuilder<CilInstruction>();
            var stacks = ImmutableDictionary.CreateBuilder<
                int,
                ImmutableArray<CliValueKind>>();
            Add(CilOperation.Call, new CilOperand.MethodInstance(Getter), []);
            Add(CilOperation.StoreLocal, new CilOperand.Index(0),
                [CliValueKind.ValueType]);
            Add(CilOperation.LoadLocalAddress, new CilOperand.Index(0), []);
            if (includeNop)
            {
                Add(CilOperation.Nop, new CilOperand.None(),
                    [CliValueKind.ManagedAddress]);
            }

            if (operation == CilOperation.LoadLocal)
            {
                Add(operation, new CilOperand.Index(1),
                    [CliValueKind.ManagedAddress]);
            }
            else if (operation is
                CilOperation.LoadArgument or
                CilOperation.LoadInt32)
            {
                Add(operation, new CilOperand.Index(0),
                    [CliValueKind.ManagedAddress]);
            }
            else if (operation is
                CilOperation.Negate or
                CilOperation.OnesComplement or
                CilOperation.ConvertInt32 or
                CilOperation.ConvertInt32Unsigned)
            {
                Add(CilOperation.LoadArgument, new CilOperand.Index(0),
                    [CliValueKind.ManagedAddress]);
                Add(operation, new CilOperand.None(),
                    [CliValueKind.ManagedAddress, CliValueKind.I4]);
            }
            else
            {
                Add(CilOperation.LoadArgument, new CilOperand.Index(0),
                    [CliValueKind.ManagedAddress]);
                Add(CilOperation.LoadInt32, new CilOperand.ConstantI4(1),
                    [CliValueKind.ManagedAddress, CliValueKind.I4]);
                Add(operation, new CilOperand.None(),
                    [CliValueKind.ManagedAddress, CliValueKind.I4, CliValueKind.I4]);
            }

            Add(CilOperation.Call, new CilOperand.MethodInstance(Indexer),
                [CliValueKind.ManagedAddress, CliValueKind.I4]);
            Add(CilOperation.LoadObject, new CilOperand.TypeIdentity(ElementType),
                [CliValueKind.ManagedAddress]);
            Add(CilOperation.Return, new CilOperand.None(), [CliValueKind.I4]);
            return (Body(instructions.ToImmutable()), stacks.ToImmutable());

            void Add(
                CilOperation current,
                CilOperand operand,
                ImmutableArray<CliValueKind> stack)
            {
                var offset = instructions.Count;
                instructions.Add(new(offset, offset + 1, current, operand));
                stacks.Add(offset, stack);
            }
        }

        public (
            CilMethodBody Body,
            ImmutableDictionary<int, ImmutableArray<CliValueKind>> Stacks)
            NopSeparatedCandidate()
        {
            var instructions = ImmutableArray.CreateBuilder<CilInstruction>();
            var stacks = ImmutableDictionary.CreateBuilder<
                int,
                ImmutableArray<CliValueKind>>();
            Add(CilOperation.Call, new CilOperand.MethodInstance(Getter), []);
            Add(CilOperation.Nop, new CilOperand.None(), [CliValueKind.ValueType]);
            Add(CilOperation.StoreLocal, new CilOperand.Index(0),
                [CliValueKind.ValueType]);
            Add(CilOperation.Nop, new CilOperand.None(), []);
            Add(CilOperation.LoadLocalAddress, new CilOperand.Index(0), []);
            Add(CilOperation.LoadArgument, new CilOperand.Index(0),
                [CliValueKind.ManagedAddress]);
            Add(CilOperation.Call, new CilOperand.MethodInstance(Indexer),
                [CliValueKind.ManagedAddress, CliValueKind.I4]);
            Add(CilOperation.Nop, new CilOperand.None(),
                [CliValueKind.ManagedAddress]);
            Add(CilOperation.LoadObject, new CilOperand.TypeIdentity(ElementType),
                [CliValueKind.ManagedAddress]);
            Add(CilOperation.Return, new CilOperand.None(), [CliValueKind.I4]);
            return (Body(instructions.ToImmutable()), stacks.ToImmutable());

            void Add(
                CilOperation current,
                CilOperand operand,
                ImmutableArray<CliValueKind> stack)
            {
                var offset = instructions.Count;
                instructions.Add(new(offset, offset + 1, current, operand));
                stacks.Add(offset, stack);
            }
        }

        public (
            CilMethodBody Body,
            ImmutableDictionary<int, ImmutableArray<CliValueKind>> Stacks)
            ReceiverConsumingIndexExpression()
        {
            var instructions = ImmutableArray.CreateBuilder<CilInstruction>();
            var stacks = ImmutableDictionary.CreateBuilder<
                int,
                ImmutableArray<CliValueKind>>();
            Add(CilOperation.Call, new CilOperand.MethodInstance(Getter), []);
            Add(CilOperation.StoreLocal, new CilOperand.Index(0),
                [CliValueKind.ValueType]);
            Add(CilOperation.LoadLocalAddress, new CilOperand.Index(0), []);
            Add(CilOperation.LoadArgument, new CilOperand.Index(0),
                [CliValueKind.ManagedAddress]);
            Add(CilOperation.Add, new CilOperand.None(),
                [CliValueKind.ManagedAddress, CliValueKind.I4]);
            Add(CilOperation.LoadArgument, new CilOperand.Index(0),
                [CliValueKind.ManagedAddress]);
            Add(CilOperation.Call, new CilOperand.MethodInstance(Indexer),
                [CliValueKind.ManagedAddress, CliValueKind.I4]);
            Add(CilOperation.LoadObject, new CilOperand.TypeIdentity(ElementType),
                [CliValueKind.ManagedAddress]);
            Add(CilOperation.Return, new CilOperand.None(), [CliValueKind.I4]);
            return (Body(instructions.ToImmutable()), stacks.ToImmutable());

            void Add(
                CilOperation current,
                CilOperand operand,
                ImmutableArray<CliValueKind> stack)
            {
                var offset = instructions.Count;
                instructions.Add(new(offset, offset + 1, current, operand));
                stacks.Add(offset, stack);
            }
        }

        public (
            CilMethodBody Body,
            ImmutableDictionary<int, ImmutableArray<CliValueKind>> Stacks)
            SplitProducerAcrossBlocks()
        {
            var instructions = new[]
            {
                new CilInstruction(0, 1, CilOperation.LoadInt32,
                    new CilOperand.ConstantI4(0)),
                new CilInstruction(1, 2, CilOperation.BranchIfTrue,
                    new CilOperand.BranchTarget(4)),
                new CilInstruction(2, 3, CilOperation.Call,
                    new CilOperand.MethodInstance(Getter)),
                new CilInstruction(3, 4, CilOperation.Branch,
                    new CilOperand.BranchTarget(5)),
                new CilInstruction(4, 5, CilOperation.Call,
                    new CilOperand.MethodInstance(Getter)),
                new CilInstruction(5, 6, CilOperation.StoreLocal,
                    new CilOperand.Index(0)),
                new CilInstruction(6, 7, CilOperation.LoadLocalAddress,
                    new CilOperand.Index(0)),
                new CilInstruction(7, 8, CilOperation.LoadArgument,
                    new CilOperand.Index(0)),
                new CilInstruction(8, 9, CilOperation.Call,
                    new CilOperand.MethodInstance(Indexer)),
                new CilInstruction(9, 10, CilOperation.LoadObject,
                    new CilOperand.TypeIdentity(ElementType)),
                new CilInstruction(10, 11, CilOperation.Return,
                    new CilOperand.None()),
            };
            var stacks = new[]
            {
                ImmutableArray<CliValueKind>.Empty,
                [CliValueKind.I4],
                [],
                [CliValueKind.ValueType],
                [],
                [CliValueKind.ValueType],
                [],
                [CliValueKind.ManagedAddress],
                [CliValueKind.ManagedAddress, CliValueKind.I4],
                [CliValueKind.ManagedAddress],
                [CliValueKind.I4],
            };
            return (Body([.. instructions]), instructions
                .Select((instruction, index) => (
                    instruction.Offset,
                    Stack: stacks[index]))
                .ToImmutableDictionary(item => item.Offset, item => item.Stack));
        }

        public (
            CilMethodBody Body,
            ImmutableDictionary<int, ImmutableArray<CliValueKind>> Stacks)
            SplitIndexExpressionAcrossBlocks()
        {
            var instructions = new[]
            {
                new CilInstruction(0, 1, CilOperation.LoadInt32,
                    new CilOperand.ConstantI4(0)),
                new CilInstruction(1, 2, CilOperation.BranchIfTrue,
                    new CilOperand.BranchTarget(7)),
                new CilInstruction(2, 3, CilOperation.Call,
                    new CilOperand.MethodInstance(Getter)),
                new CilInstruction(3, 4, CilOperation.StoreLocal,
                    new CilOperand.Index(0)),
                new CilInstruction(4, 5, CilOperation.LoadLocalAddress,
                    new CilOperand.Index(0)),
                new CilInstruction(5, 6, CilOperation.LoadArgument,
                    new CilOperand.Index(0)),
                new CilInstruction(6, 7, CilOperation.Branch,
                    new CilOperand.BranchTarget(11)),
                new CilInstruction(7, 8, CilOperation.Call,
                    new CilOperand.MethodInstance(Getter)),
                new CilInstruction(8, 9, CilOperation.StoreLocal,
                    new CilOperand.Index(0)),
                new CilInstruction(9, 10, CilOperation.LoadLocalAddress,
                    new CilOperand.Index(0)),
                new CilInstruction(10, 11, CilOperation.LoadArgument,
                    new CilOperand.Index(0)),
                new CilInstruction(11, 12, CilOperation.Call,
                    new CilOperand.MethodInstance(Indexer)),
                new CilInstruction(12, 13, CilOperation.LoadObject,
                    new CilOperand.TypeIdentity(ElementType)),
                new CilInstruction(13, 14, CilOperation.Return,
                    new CilOperand.None()),
            };
            var stacks = new[]
            {
                ImmutableArray<CliValueKind>.Empty,
                [CliValueKind.I4],
                [],
                [CliValueKind.ValueType],
                [],
                [CliValueKind.ManagedAddress],
                [CliValueKind.ManagedAddress, CliValueKind.I4],
                [],
                [CliValueKind.ValueType],
                [],
                [CliValueKind.ManagedAddress],
                [CliValueKind.ManagedAddress, CliValueKind.I4],
                [CliValueKind.ManagedAddress],
                [CliValueKind.I4],
            };
            return (Body([.. instructions]), instructions
                .Select((instruction, index) => (
                    instruction.Offset,
                    Stack: stacks[index]))
                .ToImmutableDictionary(item => item.Offset, item => item.Stack));
        }

        public (
            CilMethodBody Body,
            ImmutableDictionary<int, ImmutableArray<CliValueKind>> Stacks)
            FaultingIndexerPath(IndexerPathFault fault)
        {
            (var body, var stacks) = IndexExpressionBody(CilOperation.LoadArgument);
            var instructions = body.Instructions.ToList();
            var addressIndex = instructions.FindIndex(instruction =>
                instruction.Operation == CilOperation.LoadLocalAddress);
            var expressionIndex = addressIndex + 1;
            switch (fault)
            {
                case IndexerPathFault.DisallowedOperation:
                    instructions[expressionIndex] = instructions[expressionIndex] with
                    {
                        Operation = CilOperation.Duplicate,
                        Operand = new CilOperand.None(),
                    };
                    break;
                case IndexerPathFault.ShortEntryStack:
                    stacks = stacks.SetItem(
                        instructions[expressionIndex].Offset,
                        []);
                    break;
                case IndexerPathFault.WrongReceiverKind:
                    stacks = stacks.SetItem(
                        instructions[expressionIndex].Offset,
                        [CliValueKind.I4]);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(fault));
            }
            return (Body([.. instructions]), stacks);
        }

        private EntityKey Key(int token) => new(_assembly, token);
    }

    private sealed class FixedViewResolver(
        MethodInstanceModel producer,
        CilOperand.ReadOnlySpanData? view) : IStaticDataViewResolver
    {
        public CilOperand.ReadOnlySpanData? Resolve(MethodInstanceModel target) =>
            target.CanonicalName == producer.CanonicalName ? view : null;
    }

    private sealed class OperandCalledMethodResolver : ICalledMethodResolver
    {
        public MethodInstanceModel? Resolve(CilInstruction instruction) =>
            instruction.Operand is CilOperand.MethodInstance method
                ? method.Value
                : null;
    }

    private sealed class UnresolvedCalledMethodResolver : ICalledMethodResolver
    {
        public MethodInstanceModel? Resolve(CilInstruction instruction) => null;
    }
}
