using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Analysis.AddressTakenValues;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests.Analysis.AddressTakenValues;

public sealed class SimpleValueTypeAccessorCanonicalizerTests
{
    private static readonly AssemblyIdentity Assembly = new("AccessorCanonicalizer.Tests");
    private static readonly EntityKey TypeKey = new(Assembly, 0x02000001);
    private static readonly CliTypeIdentity Aggregate =
        CliTypeIdentity.Named(Assembly, "Fixtures", "Aggregate", isValueType: true);
    private static readonly CliTypeIdentity Int32 =
        CliTypeIdentity.FromStackKind(CliValueKind.I4);

    [Theory]
    [InlineData(false, CilOperation.LoadField, AddressTakenValuePromotionPolicy.Conservative)]
    [InlineData(true, CilOperation.StoreField, AddressTakenValuePromotionPolicy.Conservative)]
    [InlineData(false, CilOperation.LoadField, AddressTakenValuePromotionPolicy.References)]
    public void CanonicalizesExactInstanceAccessors(
        bool setter,
        CilOperation expected,
        AddressTakenValuePromotionPolicy policy)
    {
        var fixture = new Fixture(setter);
        var body = CallerBody(fixture.Call);

        var result = fixture.Create(policy).Canonicalize(body);

        Assert.NotSame(body, result);
        var replacement = Assert.Single(result.Instructions.Where(
            instruction => instruction.Offset == fixture.Call.Offset));
        Assert.Equal(expected, replacement.Operation);
        Assert.Equal(fixture.FieldOperand, replacement.Operand);
    }

    [Fact]
    public void CanonicalizesDefinitionFieldOperandsForClosedTypes()
    {
        var fixture = new Fixture(setter: false, definitionOperand: true);

        var result = fixture.Create().Canonicalize(CallerBody(fixture.Call));

        Assert.IsType<CilOperand.Entity>(result.Instructions[0].Operand);
        Assert.Equal(CilOperation.LoadField, result.Instructions[0].Operation);
    }

    [Fact]
    public void DisabledPolicyPreservesTheOriginalBodyWithoutReadingTheAccessor()
    {
        var fixture = new Fixture(setter: false);
        var body = CallerBody(fixture.Call);

        var result = fixture.Create(AddressTakenValuePromotionPolicy.Disabled)
            .Canonicalize(body);

        Assert.Same(body, result);
        Assert.Equal(0, fixture.BodyReads);
    }

    [Theory]
    [InlineData(RejectedAccessor.Unresolved)]
    [InlineData(RejectedAccessor.Static)]
    [InlineData(RejectedAccessor.Virtual)]
    [InlineData(RejectedAccessor.Abstract)]
    [InlineData(RejectedAccessor.NoBody)]
    [InlineData(RejectedAccessor.ReferenceDeclaringType)]
    [InlineData(RejectedAccessor.ExceptionRegion)]
    [InlineData(RejectedAccessor.ExtraOperation)]
    [InlineData(RejectedAccessor.WrongSignature)]
    [InlineData(RejectedAccessor.WrongFieldOwner)]
    [InlineData(RejectedAccessor.StaticField)]
    [InlineData(RejectedAccessor.ConstructedDefinitionField)]
    public void RejectsAnythingOtherThanAnExactClosedValueTypeAccessor(
        RejectedAccessor rejection)
    {
        var fixture = new Fixture(setter: false, rejection: rejection);
        var body = CallerBody(fixture.Call);

        var result = fixture.Create().Canonicalize(body);

        Assert.Same(body, result);
    }

    [Fact]
    public void PreservesAccessorsThatTriggerTypeInitialization()
    {
        var fixture = new Fixture(setter: false) { RequiresInitialization = true };
        var body = CallerBody(fixture.Call);
        Assert.Same(body, fixture.Create().Canonicalize(body));
        Assert.Equal(0, fixture.BodyReads);
    }

    [Fact]
    public void RejectsMissingInputsAndUnknownPolicy()
    {
        var fixture = new Fixture(setter: false);

        Assert.Throws<ArgumentNullException>(() =>
            new SimpleValueTypeAccessorCanonicalizer(
                null!, fixture, fixture, AddressTakenValuePromotionPolicy.Conservative, fixture));
        Assert.Throws<ArgumentNullException>(() =>
            new SimpleValueTypeAccessorCanonicalizer(
                fixture, null!, fixture, AddressTakenValuePromotionPolicy.Conservative, fixture));
        Assert.Throws<ArgumentNullException>(() =>
            new SimpleValueTypeAccessorCanonicalizer(
                fixture, fixture, null!, AddressTakenValuePromotionPolicy.Conservative, fixture));
        Assert.Throws<ArgumentNullException>(() =>
            new SimpleValueTypeAccessorCanonicalizer(
                fixture, fixture, fixture, AddressTakenValuePromotionPolicy.Conservative, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SimpleValueTypeAccessorCanonicalizer(
                fixture, fixture, fixture, (AddressTakenValuePromotionPolicy)255, fixture));
        Assert.Throws<ArgumentNullException>(() => fixture.Create().Canonicalize(null!));
    }

    private static CilMethodBody CallerBody(CilInstruction call)
    {
        var method = Method("Caller", isStatic: true, MethodSignatureModel.Create(CliValueKind.Void));
        return new(
            method,
            2,
            [],
            [
                call,
                new CilInstruction(1, 2, CilOperation.Return, new CilOperand.None()),
            ]);
    }

    private static MethodDefinitionModel Method(
        string name,
        bool isStatic,
        MethodSignatureModel signature,
        int token = 0x06000001) => new(
        new(Assembly, token),
        TypeKey,
        name,
        isStatic,
        signature,
        RelativeVirtualAddress: 1);

    public enum RejectedAccessor
    {
        Unresolved,
        Static,
        Virtual,
        Abstract,
        NoBody,
        ReferenceDeclaringType,
        ExceptionRegion,
        ExtraOperation,
        WrongSignature,
        WrongFieldOwner,
        StaticField,
        ConstructedDefinitionField,
    }

    private sealed class Fixture : IMethodBodyReader, ICalleeMethodBodyReader, ICalledMethodResolver, IFieldRepository, IMethodInitializationTriggerClassifier
    {
        public bool TryReadMethodBody(
            MethodDefinitionModel method,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out CilMethodBody? body) =>
            new CalleeMethodBodyReader(this).TryReadMethodBody(method, out body);

        public bool TryReadMethodBody(
            MethodInstanceModel method,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out CilMethodBody? body) =>
            new CalleeMethodBodyReader(this).TryReadMethodBody(method, out body);

        private readonly MethodInstanceModel? _resolved;
        private readonly CilMethodBody _body;
        private readonly FieldDefinitionModel _field;

        public Fixture(
            bool setter,
            bool definitionOperand = false,
            RejectedAccessor? rejection = null)
        {
            var fieldOwner = rejection == RejectedAccessor.WrongFieldOwner
                ? new EntityKey(Assembly, 0x02000002)
                : TypeKey;
            _field = new(
                new(Assembly, 0x04000001),
                fieldOwner,
                "Value",
                Int32,
                isStatic: rejection == RejectedAccessor.StaticField);
            var fieldInstance = new FieldInstanceModel(_field, Aggregate, Int32);
            FieldOperand = definitionOperand ||
                rejection == RejectedAccessor.ConstructedDefinitionField
                ? new CilOperand.Entity(_field.Key)
                : new CilOperand.FieldInstance(fieldInstance);

            var signature = setter
                ? MethodSignatureModel.Create(
                    CliTypeIdentity.FromStackKind(CliValueKind.Void),
                    Int32)
                : MethodSignatureModel.Create(Int32);
            if (rejection == RejectedAccessor.WrongSignature)
            {
                signature = MethodSignatureModel.Create(CliValueKind.Void);
            }
            var definition = Method(
                setter ? "set_Value" : "get_Value",
                rejection == RejectedAccessor.Static,
                signature,
                0x06000002) with
            {
                IsVirtual = rejection == RejectedAccessor.Virtual,
                IsAbstract = rejection == RejectedAccessor.Abstract,
                RelativeVirtualAddress = rejection == RejectedAccessor.NoBody ? 0 : 1,
            };
            var declaringType = rejection switch
            {
                RejectedAccessor.ReferenceDeclaringType =>
                    CliTypeIdentity.Named(Assembly, "Fixtures", "ReferenceOwner", false),
                RejectedAccessor.ConstructedDefinitionField =>
                    CliTypeIdentity.GenericInstantiation(Aggregate, [Int32]),
                _ => Aggregate,
            };
            var instance = new MethodInstanceModel(definition, declaringType, [], signature);
            _resolved = rejection == RejectedAccessor.Unresolved ? null : instance;
            Call = new(
                0,
                1,
                CilOperation.Call,
                new CilOperand.MethodInstance(instance));

            var operations = setter
                ? new[]
                {
                    new CilInstruction(0, 1, CilOperation.LoadArgument, new CilOperand.Index(0)),
                    new CilInstruction(1, 2, CilOperation.LoadArgument, new CilOperand.Index(1)),
                    new CilInstruction(2, 3, CilOperation.StoreField, FieldOperand),
                    new CilInstruction(3, 4, CilOperation.Return, new CilOperand.None()),
                }
                : new[]
                {
                    new CilInstruction(0, 1, CilOperation.LoadArgument, new CilOperand.Index(0)),
                    new CilInstruction(1, 2, CilOperation.LoadField, FieldOperand),
                    new CilInstruction(2, 3, CilOperation.Return, new CilOperand.None()),
                };
            if (rejection == RejectedAccessor.ExtraOperation)
            {
                operations = [
                    new CilInstruction(0, 1, CilOperation.Nop, new CilOperand.None()),
                    .. operations,
                    new CilInstruction(operations.Length + 1, operations.Length + 2,
                        CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
                ];
            }
            _body = new(definition, 2, [], [.. operations])
            {
                MethodInstance = instance,
                ExceptionRegions = rejection == RejectedAccessor.ExceptionRegion
                    ? [new(CilExceptionRegionKind.Finally, 0, 1, 1, 1, null, null)]
                    : [],
            };
        }

        public CilInstruction Call { get; }

        public CilOperand FieldOperand { get; }

        public int BodyReads { get; private set; }
        public bool RequiresInitialization { get; init; }
        public bool Classify(MethodDefinitionModel method) => RequiresInitialization;

        public SimpleValueTypeAccessorCanonicalizer Create(
            AddressTakenValuePromotionPolicy policy =
                AddressTakenValuePromotionPolicy.Conservative) =>
            new(this, this, this, policy, this);

        public CilMethodBody ReadMethodBody(MethodDefinitionModel method) => Read();

        public CilMethodBody ReadMethodBody(MethodInstanceModel method) => Read();

        public MethodInstanceModel? Resolve(CilInstruction instruction) => _resolved;

        public FieldDefinitionModel GetField(EntityKey key)
        {
            Assert.Equal(_field.Key, key);
            return _field;
        }

        private CilMethodBody Read()
        {
            BodyReads++;
            return _body;
        }
    }
}
