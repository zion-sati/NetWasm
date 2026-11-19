// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Analysis.Attributes;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests;

public sealed class AttributeProvenanceAnalyzerTests
{
    private static readonly AssemblyIdentity Assembly = new("ProofTests");
    private static readonly CliTypeIdentity First = CliTypeIdentity.Named(Assembly, "Example", "First", false);
    private static readonly CliTypeIdentity Second = CliTypeIdentity.Named(Assembly, "Example", "Second", false);
    private static readonly CliTypeIdentity Nullable = CliTypeIdentity.Named(Assembly, "System", "Nullable`1", true);
    private static readonly CliTypeIdentity TypeType = CliTypeIdentity.Named(Assembly, "System", "Type", false);
    private static readonly CliTypeIdentity Reference = CliTypeIdentity.FromStackKind(CliValueKind.ManagedReference);
    private static readonly CliTypeIdentity Integer = CliTypeIdentity.FromStackKind(CliValueKind.I4);
    private static readonly MethodInstanceModel Unwrap = Method(2, CliValueKind.ManagedReference, CliValueKind.ManagedReference);

    // With the Type-value classifier, a value statically typed Type is a Type the analysis cannot name: an argument, a
    // local, a static field, a cast or test to Type, a call returning Type, GetType() and a materialized non-token.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TypedValuesProveARuntimeTypeOnlyWithTheClassifier(bool classified)
    {
        var typed = TypedMethod(6, isStatic: true, TypeType);
        var untyped = TypedMethod(7, isStatic: true, Reference);
        var field = new FieldInstanceModel(
            new(new(Assembly, 8), new(Assembly, 1), "Current", CliValueKind.ManagedReference, true), First, TypeType);
        var caller = TypedMethod(9, isStatic: true, Reference, TypeType, Integer);
        var body = new CilMethodBody(caller.Definition, 5, [CliValueKind.ManagedReference, CliValueKind.ManagedReference], [
            I(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(1, CilOperation.StoreLocal, new CilOperand.Index(1)),
            I(2, CilOperation.LoadLocal, new CilOperand.Index(0)),
            I(3, CilOperation.LoadLocal, new CilOperand.Index(1)),
            I(4, CilOperation.Pop),
            I(5, CilOperation.Pop),
            I(6, CilOperation.LoadNull),
            I(7, CilOperation.GetObjectType),
            I(8, CilOperation.Pop),
            I(9, CilOperation.LoadStaticField, new CilOperand.FieldInstance(field)),
            I(10, CilOperation.Pop),
            I(11, CilOperation.LoadNull),
            I(12, CilOperation.IsInstance, new CilOperand.TypeIdentity(TypeType)),
            I(13, CilOperation.Pop),
            I(14, CilOperation.Call, new CilOperand.MethodInstance(untyped)),
            I(15, CilOperation.CastClass, new CilOperand.TypeIdentity(TypeType)),
            I(16, CilOperation.Pop),
            I(17, CilOperation.Call, new CilOperand.MethodInstance(typed)),
            I(18, CilOperation.Pop),
            I(19, CilOperation.LoadArgument, new CilOperand.Index(1)),
            I(20, CilOperation.MaterializeType),
            I(21, CilOperation.Pop),
            I(22, CilOperation.Call, new CilOperand.MethodInstance(untyped)),
            I(23, CilOperation.Pop),
            I(24, CilOperation.LoadNull),
            I(25, CilOperation.Return)])
        {
            MethodInstance = caller,
            LocalSignatureTypes = [TypeType, Reference],
        };

        var facts = Analyze(body, field: field.Definition, typeValues: classified);

        var runtime = classified ? new AttributeValueProof(AttributeValueKind.RuntimeType) : default;
        Assert.Equal(runtime, facts[1][^1]);
        Assert.Equal(runtime, facts[3][^1]);
        Assert.Equal(runtime, facts[4][^1]);
        Assert.Equal(runtime, facts[8][^1]);
        Assert.Equal(runtime, facts[10][^1]);
        Assert.Equal(runtime, facts[13][^1]);
        Assert.Equal(runtime, facts[16][^1]);
        Assert.Equal(runtime, facts[18][^1]);
        Assert.Equal(default, facts[20][^1]);
        Assert.Equal(runtime, facts[21][^1]);
        Assert.Equal(default, facts[23][^1]);
    }

    [Fact]
    public void AnInstanceMethodsReceiverIsTypedByItsDeclaringType()
    {
        var caller = TypedMethod(10, isStatic: false, Reference, Integer);
        var body = new CilMethodBody(caller.Definition, 2, [], [
            I(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(1, CilOperation.Pop),
            I(2, CilOperation.LoadArgument, new CilOperand.Index(1)),
            I(3, CilOperation.Pop),
            I(4, CilOperation.LoadNull),
            I(5, CilOperation.Return)])
        { MethodInstance = caller };

        var facts = Analyze(body, typeValues: true);

        Assert.Equal(new(AttributeValueKind.RuntimeType), facts[1][^1]);
        Assert.Equal(default, facts[3][^1]);
    }

    // Types (or nulls) meeting at a join are a Type the analysis cannot name; anything else meeting a type is unknown.
    [Theory]
    [InlineData("types", true)]
    [InlineData("null", true)]
    [InlineData("unknown", false)]
    [InlineData("integers", false)]
    public void ValuesMeetAsARuntimeTypeOnlyWhenEveryOneIsAType(string incoming, bool runtimeType)
    {
        var untyped = TypedMethod(11, isStatic: true, Reference);
        CilInstruction[] Value(int offset, CliTypeIdentity type) => incoming switch
        {
            "integers" => [I(offset, CilOperation.LoadInt32, new CilOperand.ConstantI4(offset)), I(offset + 1, CilOperation.Nop)],
            "null" when type == Second => [I(offset, CilOperation.LoadNull), I(offset + 1, CilOperation.Nop)],
            "unknown" when type == Second => [I(offset, CilOperation.Call, new CilOperand.MethodInstance(untyped)), I(offset + 1, CilOperation.Nop)],
            _ => [I(offset, CilOperation.LoadTypeToken, new CilOperand.TypeIdentity(type)), I(offset + 1, CilOperation.MaterializeType)],
        };
        var body = Body([
            I(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(1, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(5)),
            .. Value(2, First),
            I(4, CilOperation.Branch, new CilOperand.BranchTarget(12)),
            I(5, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(6, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(10)),
            .. Value(7, Second),
            I(9, CilOperation.Branch, new CilOperand.BranchTarget(12)),
            .. Value(10, Nullable),
            I(12, CilOperation.Pop),
            I(13, CilOperation.LoadNull),
            I(14, CilOperation.Return)]);

        var facts = Analyze(body, typeValues: true);

        Assert.Equal(runtimeType ? new(AttributeValueKind.RuntimeType) : default, Assert.Single(facts[12]));
    }

    [Fact]
    public void TracksTypeTokensMaterializationAndLocalCopies()
    {
        var body = Body(
            I(0, CilOperation.LoadTypeToken, new CilOperand.TypeIdentity(First)),
            I(1, CilOperation.MaterializeType),
            I(2, CilOperation.StoreLocal, new CilOperand.Index(0)),
            I(3, CilOperation.LoadLocal, new CilOperand.Index(0)),
            I(4, CilOperation.Return));

        var facts = Analyze(body);

        Assert.Equal(new(AttributeValueKind.TypeToken, First), Assert.Single(facts[1]));
        Assert.Equal(new(AttributeValueKind.Type, First), Assert.Single(facts[2]));
        Assert.Equal(new(AttributeValueKind.Type, First), Assert.Single(facts[4]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void JoinsOnlyIdenticalTypesAcrossRuntimeBranches(bool same)
    {
        var body = Body(
            I(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(1, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(5)),
            I(2, CilOperation.LoadTypeToken, new CilOperand.TypeIdentity(First)),
            I(3, CilOperation.MaterializeType),
            I(4, CilOperation.Branch, new CilOperand.BranchTarget(7)),
            I(5, CilOperation.LoadTypeToken, new CilOperand.TypeIdentity(same ? First : Second)),
            I(6, CilOperation.MaterializeType),
            I(7, CilOperation.Return));

        var facts = Analyze(body);

        Assert.Equal(same ? new(AttributeValueKind.Type, First) : default, Assert.Single(facts[7]));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void FollowsOnlyFeasibleConstantBooleanBranches(bool value, bool inverted)
    {
        var body = Body(
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(value ? 1 : 0)),
            I(1, inverted ? CilOperation.BranchIfFalse : CilOperation.BranchIfTrue, new CilOperand.BranchTarget(5)),
            I(2, CilOperation.LoadTypeToken, new CilOperand.TypeIdentity(First)),
            I(3, CilOperation.MaterializeType),
            I(4, CilOperation.Branch, new CilOperand.BranchTarget(7)),
            I(5, CilOperation.LoadTypeToken, new CilOperand.TypeIdentity(Second)),
            I(6, CilOperation.MaterializeType),
            I(7, CilOperation.Return));

        var facts = Analyze(body);

        Assert.Equal(new(AttributeValueKind.Type, value != inverted ? Second : First), Assert.Single(facts[7]));
        Assert.False(facts.ContainsKey(value != inverted ? 2 : 5));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NullableCoalescingResolvesToOneExactType(bool nullable)
    {
        var enumType = CliTypeIdentity.Named(Assembly, "Example", "Enum", true);
        var declared = nullable ? CliTypeIdentity.GenericInstantiation(Nullable, [enumType]) : enumType;
        var body = Body(
            I(0, CilOperation.LoadTypeToken, new CilOperand.TypeIdentity(declared)),
            I(1, CilOperation.MaterializeType),
            I(2, CilOperation.Call, new CilOperand.MethodInstance(Unwrap)),
            I(3, CilOperation.Duplicate),
            I(4, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(8)),
            I(5, CilOperation.Pop),
            I(6, CilOperation.LoadTypeToken, new CilOperand.TypeIdentity(declared)),
            I(7, CilOperation.MaterializeType),
            I(8, CilOperation.Return));

        var facts = Analyze(body);

        Assert.Equal(new(AttributeValueKind.Type, enumType), Assert.Single(facts[8]));
        Assert.Equal(!nullable, facts.ContainsKey(5));
    }

    [Fact]
    public void LoopLosesExactProofWhenAnyIterationCanChangeTheLocal()
    {
        var body = Body(
            I(0, CilOperation.LoadTypeToken, new CilOperand.TypeIdentity(First)),
            I(1, CilOperation.MaterializeType),
            I(2, CilOperation.StoreLocal, new CilOperand.Index(0)),
            I(3, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(4, CilOperation.BranchIfFalse, new CilOperand.BranchTarget(9)),
            I(5, CilOperation.LoadTypeToken, new CilOperand.TypeIdentity(Second)),
            I(6, CilOperation.MaterializeType),
            I(7, CilOperation.StoreLocal, new CilOperand.Index(0)),
            I(8, CilOperation.Branch, new CilOperand.BranchTarget(3)),
            I(9, CilOperation.LoadLocal, new CilOperand.Index(0)),
            I(10, CilOperation.Return));

        var facts = Analyze(body);

        Assert.Equal(default, Assert.Single(facts[10]));
    }

    [Fact]
    public void SideEffectingCallsAndArithmeticPreserveValuesBelowTheirArguments()
    {
        var call = Method(3, CliValueKind.I4, CliValueKind.I4);
        var body = Body(
            I(0, CilOperation.LoadTypeToken, new CilOperand.TypeIdentity(First)),
            I(1, CilOperation.MaterializeType),
            I(2, CilOperation.LoadInt32, new CilOperand.ConstantI4(5)),
            I(3, CilOperation.Call, new CilOperand.MethodInstance(call)),
            I(4, CilOperation.LoadInt32, new CilOperand.ConstantI4(2)),
            I(5, CilOperation.Add),
            I(6, CilOperation.Pop),
            I(7, CilOperation.Return));

        var facts = Analyze(body);

        Assert.Equal(new(AttributeValueKind.Type, First), Assert.Single(facts[7]));
        Assert.Equal(default, facts[4][1]);
        Assert.Equal(default, facts[6][1]);
    }

    [Fact]
    public void AddressTakenLocalIsNeverConsideredImmutable()
    {
        var body = Body(
            I(0, CilOperation.LoadTypeToken, new CilOperand.TypeIdentity(First)),
            I(1, CilOperation.MaterializeType),
            I(2, CilOperation.StoreLocal, new CilOperand.Index(0)),
            I(3, CilOperation.LoadLocalAddress, new CilOperand.Index(0)),
            I(4, CilOperation.Pop),
            I(5, CilOperation.LoadLocal, new CilOperand.Index(0)),
            I(6, CilOperation.Return));

        Assert.Equal(default, Assert.Single(Analyze(body)[6]));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void FieldLoadsRequireAnIndependentProof(bool proved, bool constructed)
    {
        var field = new FieldInstanceModel(
            new(new(Assembly, 4), new(Assembly, 1), "Type", CliValueKind.ManagedReference, false),
            First, CliTypeIdentity.FromStackKind(CliValueKind.ManagedReference));
        var body = Body(
            I(0, CilOperation.LoadNull),
            I(1, CilOperation.LoadField, constructed ? new CilOperand.FieldInstance(field) : new CilOperand.Entity(field.Definition.Key)),
            I(2, CilOperation.CastClass, new CilOperand.TypeIdentity(First)),
            I(3, CilOperation.Return));
        var fields = new Fields(proved ? First : null);

        var facts = Analyze(body, fields, field.Definition);

        if (constructed)
        {
            Assert.Equal(field, Assert.Single(fields.Requests));
            Assert.Empty(fields.Definitions);
        }
        else
        {
            Assert.Equal(field.Definition.Key, Assert.Single(fields.Definitions));
            Assert.Empty(fields.Requests);
        }
        Assert.Equal(proved ? new(AttributeValueKind.Type, First) : default, Assert.Single(facts[3]));
    }

    [Fact]
    public void ExceptionHandlerJoinsLocalsFromEveryPotentialThrowPoint()
    {
        var body = Body(
            I(0, CilOperation.LoadTypeToken, new CilOperand.TypeIdentity(First)),
            I(1, CilOperation.MaterializeType),
            I(2, CilOperation.StoreLocal, new CilOperand.Index(0)),
            I(3, CilOperation.Call, new CilOperand.MethodInstance(Method(5, CliValueKind.Void))),
            I(4, CilOperation.LoadTypeToken, new CilOperand.TypeIdentity(Second)),
            I(5, CilOperation.MaterializeType),
            I(6, CilOperation.StoreLocal, new CilOperand.Index(0)),
            I(7, CilOperation.Call, new CilOperand.MethodInstance(Method(5, CliValueKind.Void))),
            I(8, CilOperation.Leave, new CilOperand.BranchTarget(12)),
            I(9, CilOperation.Pop),
            I(10, CilOperation.LoadLocal, new CilOperand.Index(0)),
            I(11, CilOperation.Throw),
            I(12, CilOperation.LoadLocal, new CilOperand.Index(0)),
            I(13, CilOperation.Return)) with
        {
            ExceptionRegions = [new(CilExceptionRegionKind.Catch, 3, 6, 9, 3, new(Assembly, 8), null)],
        };

        Assert.Equal(default, Assert.Single(Analyze(body)[11]));
    }

    [Fact]
    public void NormalLeaveVisitsFinallyAndInvalidatesItsWritesAtTheContinuation()
    {
        var body = Body(
            I(0, CilOperation.LoadTypeToken, new CilOperand.TypeIdentity(First)),
            I(1, CilOperation.MaterializeType),
            I(2, CilOperation.StoreLocal, new CilOperand.Index(0)),
            I(3, CilOperation.Nop),
            I(4, CilOperation.Leave, new CilOperand.BranchTarget(11)),
            I(5, CilOperation.LoadTypeToken, new CilOperand.TypeIdentity(Second)),
            I(6, CilOperation.MaterializeType),
            I(7, CilOperation.StoreLocal, new CilOperand.Index(0)),
            I(8, CilOperation.LoadLocal, new CilOperand.Index(0)),
            I(9, CilOperation.Pop),
            I(10, CilOperation.EndFinally),
            I(11, CilOperation.LoadLocal, new CilOperand.Index(0)),
            I(12, CilOperation.Return)) with
        {
            ExceptionRegions = [new(CilExceptionRegionKind.Finally, 3, 2, 5, 6, null, null)],
        };

        var facts = Analyze(body);

        Assert.Equal(new(AttributeValueKind.Type, Second), Assert.Single(facts[9]));
        Assert.Equal(default, Assert.Single(facts[12]));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public void AcceptedFiltersCarryMutatedLocalsIntoTheirHandlers(int accept)
    {
        var body = Body(
            I(0, CilOperation.LoadTypeToken, new CilOperand.TypeIdentity(First)),
            I(1, CilOperation.MaterializeType),
            I(2, CilOperation.StoreLocal, new CilOperand.Index(0)),
            I(3, CilOperation.Call, new CilOperand.MethodInstance(Method(5, CliValueKind.Void))),
            I(4, CilOperation.Leave, new CilOperand.BranchTarget(15)),
            I(5, CilOperation.Pop),
            I(6, CilOperation.LoadTypeToken, new CilOperand.TypeIdentity(Second)),
            I(7, CilOperation.MaterializeType),
            I(8, CilOperation.StoreLocal, new CilOperand.Index(0)),
            accept < 0 ? I(9, CilOperation.LoadArgument, new CilOperand.Index(0)) :
                I(9, CilOperation.LoadInt32, new CilOperand.ConstantI4(accept)),
            I(10, CilOperation.EndFilter),
            I(11, CilOperation.Pop),
            I(12, CilOperation.LoadLocal, new CilOperand.Index(0)),
            I(13, CilOperation.Pop),
            I(14, CilOperation.Leave, new CilOperand.BranchTarget(15)),
            I(15, CilOperation.LoadLocal, new CilOperand.Index(0)),
            I(16, CilOperation.Return)) with
        {
            ExceptionRegions = [new(CilExceptionRegionKind.Filter, 3, 2, 11, 4, null, 5)],
        };

        var facts = Analyze(body);

        Assert.Equal(accept != 0, facts.ContainsKey(13));
        if (accept != 0)
        {
            Assert.Equal(new(AttributeValueKind.Type, Second), Assert.Single(facts[13]));
        }
        Assert.Equal(accept == 0 ? new(AttributeValueKind.Type, First) : default, Assert.Single(facts[16]));
    }

    [Fact]
    public void FilterLocalProofsFlowOnlyToTheMatchingHandler()
    {
        var body = Body(
            I(0, CilOperation.Call, new CilOperand.MethodInstance(Method(5, CliValueKind.Void))),
            I(1, CilOperation.Leave, new CilOperand.BranchTarget(21)),
            I(2, CilOperation.Pop),
            I(3, CilOperation.LoadTypeToken, new CilOperand.TypeIdentity(First)),
            I(4, CilOperation.MaterializeType),
            I(5, CilOperation.StoreLocal, new CilOperand.Index(0)),
            I(6, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(7, CilOperation.EndFilter),
            I(8, CilOperation.Pop),
            I(9, CilOperation.LoadLocal, new CilOperand.Index(0)),
            I(10, CilOperation.Pop),
            I(11, CilOperation.Leave, new CilOperand.BranchTarget(21)),
            I(12, CilOperation.Pop),
            I(13, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(14, CilOperation.EndFilter),
            I(15, CilOperation.Pop),
            I(16, CilOperation.LoadLocal, new CilOperand.Index(0)),
            I(17, CilOperation.Pop),
            I(18, CilOperation.Leave, new CilOperand.BranchTarget(21)),
            I(19, CilOperation.Pop),
            I(20, CilOperation.Leave, new CilOperand.BranchTarget(21)),
            I(21, CilOperation.LoadLocal, new CilOperand.Index(0)),
            I(22, CilOperation.Return)) with
        {
            ExceptionRegions =
            [
                new(CilExceptionRegionKind.Filter, 0, 2, 8, 4, null, 2),
                new(CilExceptionRegionKind.Filter, 0, 2, 15, 4, null, 12),
                new(CilExceptionRegionKind.Catch, 0, 2, 19, 2, new(Assembly, 8), null),
            ],
        };

        var facts = Analyze(body);

        Assert.Equal(new(AttributeValueKind.Type, First), Assert.Single(facts[10]));
        Assert.Equal(default, Assert.Single(facts[17]));
        Assert.Equal(default, Assert.Single(facts[22]));
    }

    [Fact]
    public void NonThrowingStoresDoNotIntroduceImpossibleHandlerValues()
    {
        var body = Body(
            I(0, CilOperation.LoadTypeToken, new CilOperand.TypeIdentity(First)),
            I(1, CilOperation.MaterializeType),
            I(2, CilOperation.StoreLocal, new CilOperand.Index(0)),
            I(3, CilOperation.LoadTypeToken, new CilOperand.TypeIdentity(Second)),
            I(4, CilOperation.MaterializeType),
            I(5, CilOperation.StoreLocal, new CilOperand.Index(1)),
            I(6, CilOperation.LoadLocal, new CilOperand.Index(1)),
            I(7, CilOperation.StoreLocal, new CilOperand.Index(0)),
            I(8, CilOperation.Call, new CilOperand.MethodInstance(Method(5, CliValueKind.Void))),
            I(9, CilOperation.Leave, new CilOperand.BranchTarget(13)),
            I(10, CilOperation.Pop),
            I(11, CilOperation.LoadLocal, new CilOperand.Index(0)),
            I(12, CilOperation.Throw),
            I(13, CilOperation.LoadLocal, new CilOperand.Index(0)),
            I(14, CilOperation.Return)) with
        {
            Locals = [CliValueKind.ManagedReference, CliValueKind.ManagedReference],
            LocalSignatureTypes = [CliTypeIdentity.FromStackKind(CliValueKind.ManagedReference),
                CliTypeIdentity.FromStackKind(CliValueKind.ManagedReference)],
            ExceptionRegions = [new(CilExceptionRegionKind.Catch, 6, 4, 10, 3, new(Assembly, 8), null)],
        };

        Assert.Equal(new(AttributeValueKind.Type, Second), Assert.Single(Analyze(body)[12]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExceptionalUnwindingCannotRetainProofsChangedByCleanup(bool fault)
    {
        var body = Body(
            I(0, CilOperation.LoadTypeToken, new CilOperand.TypeIdentity(First)),
            I(1, CilOperation.MaterializeType),
            I(2, CilOperation.StoreLocal, new CilOperand.Index(0)),
            I(3, CilOperation.Call, new CilOperand.MethodInstance(Method(5, CliValueKind.Void))),
            I(4, CilOperation.Leave, new CilOperand.BranchTarget(9)),
            I(5, CilOperation.LoadTypeToken, new CilOperand.TypeIdentity(Second)),
            I(6, CilOperation.MaterializeType),
            I(7, CilOperation.StoreLocal, new CilOperand.Index(0)),
            I(8, CilOperation.EndFinally),
            I(9, CilOperation.Leave, new CilOperand.BranchTarget(13)),
            I(10, CilOperation.Pop),
            I(11, CilOperation.LoadLocal, new CilOperand.Index(0)),
            I(12, CilOperation.Throw),
            I(13, CilOperation.LoadLocal, new CilOperand.Index(0)),
            I(14, CilOperation.Return)) with
        {
            ExceptionRegions =
            [
                new(fault ? CilExceptionRegionKind.Fault : CilExceptionRegionKind.Finally, 3, 2, 5, 4, null, null),
                new(CilExceptionRegionKind.Catch, 3, 7, 10, 3, new(Assembly, 8), null),
            ],
        };

        var facts = Analyze(body);

        Assert.Equal(default, Assert.Single(facts[12]));
        Assert.Equal(fault ? new(AttributeValueKind.Type, First) : default, Assert.Single(facts[14]));
    }

    [Fact]
    public void NullInputIsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => Analyze(CreateAnalyzer(new Fields(null)), null!));
    }

    private static ImmutableDictionary<int, ImmutableArray<AttributeValueProof>> Analyze(
        CilMethodBody body, Fields? fields = null, FieldDefinitionModel? field = null, bool typeValues = false)
    {
        var graph = new ControlFlowGraphBuilderFactory().Create().Build(body);
        var repository = new Repository(field);
        var validated = new TypedStackValidatorFactory(new StackTypeCompatibilityValidator())
            .Create(repository, repository, repository).Validate(graph);
        return Analyze(CreateAnalyzer(fields ?? new Fields(null), typeValues ? new TypeValues() : null), validated);
    }

    private static AttributeProvenanceAnalyzer CreateAnalyzer(Fields fields, IAttributeTypeValueClassifier? typeValues = null) =>
        new(new Calls(), new Types(), fields, [Unwrap.Definition.Key], Nullable, typeValues: typeValues);

    private static ImmutableDictionary<int, ImmutableArray<AttributeValueProof>> Analyze<TAnalyzer>(
        TAnalyzer analyzer, ValidatedControlFlowGraph graph) where TAnalyzer : IAttributeProvenanceAnalyzer =>
        analyzer.Analyze(graph);

    private static CilInstruction I(int offset, CilOperation operation, CilOperand? operand = null) =>
        new(offset, offset + 1, operation, operand ?? new CilOperand.None());

    private static CilMethodBody Body(params CilInstruction[] instructions) =>
        new(Method(1, CliValueKind.ManagedReference, CliValueKind.I4).Definition,
            5, [CliValueKind.ManagedReference], [.. instructions]);

    private static MethodInstanceModel Method(int token, CliValueKind result, params CliValueKind[] parameters)
    {
        var signature = MethodSignatureModel.Create(result, parameters);
        return new(new(new(Assembly, token), new(Assembly, 1), "Method", true, signature, 1), First, [], signature);
    }

    private static MethodInstanceModel TypedMethod(int token, bool isStatic, CliTypeIdentity returnType,
        params CliTypeIdentity[] parameters)
    {
        var signature = new MethodSignatureModel(returnType, [.. parameters]);
        return new(new(new(Assembly, token), new(Assembly, 1), "Typed", isStatic, signature, 1), TypeType, [], signature);
    }

    private sealed class TypeValues : IAttributeTypeValueClassifier
    {
        public bool IsTypeValue(CliTypeIdentity type) => type == TypeType;
    }

    private sealed class Calls : ICalledMethodResolver
    {
        public MethodInstanceModel? Resolve(CilInstruction instruction) =>
            ((CilOperand.MethodInstance)instruction.Operand).Value;
    }

    private sealed class Types : ITypeOperandResolver
    {
        public CliTypeIdentity Resolve(CilInstruction instruction, MethodInstanceModel? methodInstance) =>
            ((CilOperand.TypeIdentity)instruction.Operand).Value;
    }

    private sealed class Fields(CliTypeIdentity? type) : IReadonlyTypeFieldValueResolver
    {
        public List<FieldInstanceModel> Requests { get; } = [];
        public List<EntityKey> Definitions { get; } = [];

        public CliTypeIdentity? Resolve(EntityKey field)
        {
            Definitions.Add(field);
            return type;
        }

        public CliTypeIdentity? Resolve(FieldInstanceModel field)
        {
            Requests.Add(field);
            return type;
        }
    }

    private sealed class Repository(FieldDefinitionModel? field = null) : ITypeRepository, IFieldRepository, IMethodRepository
    {
        // Only an instance method's declaring type, which its receiver argument names.
        public TypeDefinitionModel GetTypeDefinition(EntityKey key) => key == new EntityKey(Assembly, 1)
            ? new(key, "System", "Type", false, [], [])
            : throw new InvalidOperationException("The fixture uses resolved type operands.");

        public FieldDefinitionModel GetField(EntityKey key) =>
            field?.Key == key ? field : throw new InvalidOperationException("The fixture uses resolved field operands.");

        public MethodDefinitionModel GetMethod(EntityKey key) =>
            throw new InvalidOperationException("The fixture uses resolved method operands.");
    }
}
