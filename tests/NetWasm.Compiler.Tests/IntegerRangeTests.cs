using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

using Code = (CilOperation Operation, CilOperand? Operand);

// The integer ranges that decide which integer checks a method keeps, on CIL
// that C# rarely or never emits: stores that truncate, conversions of every
// width, masks and shifts, unsigned operations, calls, handlers, branches that
// cannot go one way, and loops whose bounds widen to their limits. Each body
// divides last, so the proof for that division says what its operands held.
public sealed class IntegerRangeTests
{
    private static readonly AssemblyIdentity Assembly = new("fixture");
    private static readonly CliTypeIdentity Int32 = CliTypeIdentity.FromStackKind(CliValueKind.I4);
    private static readonly CliTypeIdentity Int64 = CliTypeIdentity.FromStackKind(CliValueKind.I8);
    private static readonly CliTypeIdentity Byte = CliTypeIdentity.Primitive("u1", CliValueKind.I4);
    private static readonly CliTypeIdentity Object = CliTypeIdentity.FromStackKind(CliValueKind.ManagedReference);

    // A store to a byte keeps the low byte: 256 stored is zero.
    [Theory]
    [InlineData(256, false)]
    [InlineData(255, true)]
    public void AStoreToASmallIntegerKeepsItsRange(int stored, bool nonZero)
    {
        var proof = Prove([Int32], [Byte],
            I(CilOperation.LoadInt32, new CilOperand.ConstantI4(stored)), Local(CilOperation.StoreLocal, 0),
            Argument(0), Local(CilOperation.LoadLocal, 0), I(CilOperation.Divide), I(CilOperation.Return));
        Assert.Equal(nonZero, NonZero(proof, 4));
    }

    // conv.i4 keeps a long that fits and wraps one that does not; conv.i8
    // widens; conv.u1 wraps; conv.ovf.u1 keeps what it does not throw for; an
    // unsigned source's checked conversion knows nothing of a negative value;
    // native and floating conversions are not followed.
    [Theory]
    [InlineData(7L, CilOperation.ConvertInt32, null, true)]
    [InlineData(1L << 32, CilOperation.ConvertInt32, null, false)]
    [InlineData(9L, CilOperation.ConvertNumeric, "u1", true)]
    [InlineData(256L, CilOperation.ConvertNumeric, "u1", false)]
    [InlineData(9L, CilOperation.ConvertNumeric, "ovf.u1", true)]
    [InlineData(300L, CilOperation.ConvertNumeric, "ovf.u1", false)]
    [InlineData(-3L, CilOperation.ConvertNumeric, "ovf.u1.un", false)]
    [InlineData(5L, CilOperation.ConvertNumeric, "ovf.i", false)]
    public void ConversionsKeepTheValuesTheyCanHold(long value, CilOperation operation, string? conversion, bool nonZero)
    {
        CilOperand operand = conversion switch
        {
            null => new CilOperand.None(),
            "u1" => new CilOperand.NumericConversion(8, true, false, false, false),
            "ovf.u1" => new CilOperand.NumericConversion(8, true, true, false, false),
            "ovf.u1.un" => new CilOperand.NumericConversion(8, true, true, true, false),
            _ => new CilOperand.NumericConversion(0, false, true, false, true),
        };
        var proof = Prove([Int32], [],
            Argument(0), I(CilOperation.LoadInt64, new CilOperand.ConstantI8(value)), I(operation, operand),
            I(CilOperation.Divide), I(CilOperation.Pop), I(CilOperation.LoadInt32, new CilOperand.ConstantI4(0)), I(CilOperation.Return));
        Assert.Equal(nonZero, NonZero(proof, 3));
    }

    // conv.ovf.u4 of 3,000,000,000 fits its target, and is negative as an int.
    [Fact]
    public void ACheckedUnsignedConversionPastIntsRangeIsAnyInt()
    {
        var proof = Prove([Int32], [],
            Argument(0), I(CilOperation.LoadInt64, new CilOperand.ConstantI8(3_000_000_000)),
            I(CilOperation.ConvertNumeric, new CilOperand.NumericConversion(32, true, true, false, false)),
            I(CilOperation.Divide), I(CilOperation.Return));
        Assert.True(NonOverflowing(proof, 2));
        Assert.False(NonZero(proof, 3));
    }

    // A shift by a native int count is not followed.
    [Fact]
    public void AShiftByANativeIntIsNotFollowed()
    {
        var proof = ProveBody([Int64], [],
            [Argument(0), I(CilOperation.LoadInt64, new CilOperand.ConstantI8(64)),
             I(CilOperation.LoadInt32, new CilOperand.ConstantI4(2)), I(CilOperation.ConvertNativeInt),
             I(CilOperation.ShiftRightSigned), I(CilOperation.Divide), I(CilOperation.Return)], returns: Int64);
        Assert.False(NonZero(proof, 5));
    }

    // A value tested twice through a copy: once it is at least 5, it is never
    // below 3, so that edge reaches nothing.
    [Fact]
    public void ACopyTestedAgainCannotTakeAnEdgeTheFirstTestRuledOut()
    {
        var proof = Prove([Int32], [],
            Argument(0), I(CilOperation.Duplicate), I(CilOperation.LoadInt32, new CilOperand.ConstantI4(5)),
            I(CilOperation.BranchIfLessThanSigned, new CilOperand.BranchTarget(14)),
            I(CilOperation.LoadInt32, new CilOperand.ConstantI4(3)),
            I(CilOperation.BranchIfLessThanSigned, new CilOperand.BranchTarget(10)),
            Argument(0), Argument(0), I(CilOperation.Divide), I(CilOperation.Return),
            Argument(0), Argument(0), I(CilOperation.Divide), I(CilOperation.Return),
            I(CilOperation.Pop), I(CilOperation.LoadInt32, new CilOperand.ConstantI4(0)), I(CilOperation.Return));
        Assert.True(NonZero(proof, 8));
        Assert.False(NonZero(proof, 12));
    }

    [Fact]
    public void AnIntWidenedToALongKeepsItsRange()
    {
        var proof = ProveBody([Int64], [],
            [Argument(0), I(CilOperation.LoadInt32, new CilOperand.ConstantI4(3)), I(CilOperation.ConvertInt64),
             I(CilOperation.Divide), I(CilOperation.Return)], returns: Int64);
        Assert.True(NonZero(proof, 3));
        Assert.True(NonOverflowing(proof, 3));
    }

    // (7 & 6) + 1 is between 1 and 7: a mask with a value that is not negative
    // is within it, and a mask of two negative values is not followed.
    [Theory]
    [InlineData(6, 7, true)]
    [InlineData(-6, 7, true)]
    [InlineData(6, -7, true)]
    [InlineData(-6, -7, false)]
    public void AMaskWithANonNegativeValueStaysWithinIt(int mask, int other, bool nonZero)
    {
        var proof = Prove([Int32], [],
            Argument(0),
            I(CilOperation.LoadInt32, new CilOperand.ConstantI4(other)), I(CilOperation.LoadInt32, new CilOperand.ConstantI4(mask)),
            I(CilOperation.BitwiseAnd), I(CilOperation.LoadInt32, new CilOperand.ConstantI4(1)), I(CilOperation.Add),
            I(CilOperation.Divide), I(CilOperation.Return));
        Assert.Equal(nonZero, NonZero(proof, 6));
    }

    // 100 >> 2 is 25; -100 >>> 1 is somewhere below 2^31; shifts by zero,
    // by a range, or by more than the width are not followed.
    [Theory]
    [InlineData(CilOperation.ShiftRightSigned, 100, 2, true)]
    [InlineData(CilOperation.ShiftRightSigned, 100, 40, false)]
    [InlineData(CilOperation.ShiftRightUnsigned, 100, 2, true)]
    [InlineData(CilOperation.ShiftRightUnsigned, -100, 1, false)]
    [InlineData(CilOperation.ShiftRightUnsigned, -100, 0, false)]
    public void ShiftsByAConstantMoveTheBounds(CilOperation shift, int value, int count, bool nonZero)
    {
        var proof = Prove([Int32], [],
            Argument(0), I(CilOperation.LoadInt32, new CilOperand.ConstantI4(value)),
            I(CilOperation.LoadInt32, new CilOperand.ConstantI4(count)), I(shift),
            I(CilOperation.Divide), I(CilOperation.Return));
        Assert.Equal(nonZero, NonZero(proof, 4));
    }

    [Fact]
    public void AShiftByAnUnknownCountIsNotFollowed()
    {
        var proof = Prove([Int32, Int32], [],
            Argument(0), I(CilOperation.LoadInt32, new CilOperand.ConstantI4(64)), Argument(1),
            I(CilOperation.ShiftRightSigned), I(CilOperation.Divide), I(CilOperation.Return));
        Assert.False(NonZero(proof, 4));
    }

    // A comparison is 0 or 1, so 1 plus a comparison is never zero.
    [Fact]
    public void AComparisonIsZeroOrOne()
    {
        var proof = Prove([Int32, Int32], [],
            Argument(0), I(CilOperation.LoadInt32, new CilOperand.ConstantI4(1)), Argument(0), Argument(1),
            I(CilOperation.CompareLessThanSigned), I(CilOperation.Add), I(CilOperation.Divide), I(CilOperation.Return));
        Assert.True(NonZero(proof, 6));
    }

    // An unsigned division of values that may be negative is not followed.
    [Theory]
    [InlineData(40, true)]
    [InlineData(-40, false)]
    public void AnUnsignedQuotientIsFollowedOnlyForNonNegativeOperands(int dividend, bool nonZero)
    {
        var proof = Prove([Int32], [],
            Argument(0), I(CilOperation.LoadInt32, new CilOperand.ConstantI4(dividend)),
            I(CilOperation.LoadInt32, new CilOperand.ConstantI4(4)), I(CilOperation.DivideUnsigned),
            I(CilOperation.Divide), I(CilOperation.Return));
        Assert.Equal(nonZero, NonZero(proof, 4));
    }

    // 7 % 0 and 7 / 0 always throw, so what they push is never read.
    [Theory]
    [InlineData(CilOperation.Remainder)]
    [InlineData(CilOperation.Divide)]
    public void ADivisionByOnlyZeroPushesAnyValue(CilOperation operation)
    {
        var proof = Prove([Int32], [],
            Argument(0), I(CilOperation.LoadInt32, new CilOperand.ConstantI4(7)), I(CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(operation), I(CilOperation.Divide), I(CilOperation.Return));
        Assert.False(NonZero(proof, 3));
        Assert.False(NonZero(proof, 4));
    }

    // A quotient by a divisor of either sign spans both signs' quotients:
    // 12 / (-3..4) is between -12 and 12, and plus 13 never zero.
    [Fact]
    public void AQuotientByEitherSignUnitesBoth()
    {
        var proof = Prove([Int32, Int32], [],
            Argument(1), I(CilOperation.LoadInt32, new CilOperand.ConstantI4(-3)),
            I(CilOperation.BranchIfLessThanSigned, new CilOperand.BranchTarget(14)),
            Argument(1), I(CilOperation.LoadInt32, new CilOperand.ConstantI4(4)),
            I(CilOperation.BranchIfGreaterThanSigned, new CilOperand.BranchTarget(14)),
            Argument(0), I(CilOperation.LoadInt32, new CilOperand.ConstantI4(12)), Argument(1), I(CilOperation.Divide),
            I(CilOperation.LoadInt32, new CilOperand.ConstantI4(13)), I(CilOperation.Add), I(CilOperation.Divide),
            I(CilOperation.Return),
            I(CilOperation.LoadInt32, new CilOperand.ConstantI4(0)), I(CilOperation.Return));
        Assert.False(NonZero(proof, 9));
        Assert.True(NonZero(proof, 12));
    }

    // Checked arithmetic keeps what it does not throw for; unsigned checked
    // arithmetic of values that may be negative is not followed.
    [Theory]
    [InlineData(CilOperation.AddChecked, 2147483640, 20, false, false)]
    [InlineData(CilOperation.AddChecked, 3, 4, true, true)]
    [InlineData(CilOperation.AddCheckedUnsigned, 3, 4, true, true)]
    [InlineData(CilOperation.SubtractCheckedUnsigned, 3, 4, false, false)]
    [InlineData(CilOperation.MultiplyCheckedUnsigned, -3, 4, false, false)]
    [InlineData(CilOperation.MultiplyChecked, -3, 4, true, true)]
    public void CheckedArithmeticKeepsWhatItDoesNotThrowFor(CilOperation operation, int left, int right, bool fits, bool nonZero)
    {
        var proof = Prove([Int32], [],
            Argument(0), I(CilOperation.LoadInt32, new CilOperand.ConstantI4(left)),
            I(CilOperation.LoadInt32, new CilOperand.ConstantI4(right)), I(operation),
            I(CilOperation.Divide), I(CilOperation.Return));
        Assert.Equal(fits, NonOverflowing(proof, 3));
        Assert.Equal(nonZero, NonZero(proof, 4));
    }

    // A call leaves its result, and an instruction whose effect is unknown
    // leaves the stack the next instruction starts with; neither changes a slot.
    [Fact]
    public void CallsAndUnknownInstructionsLeaveValuesOfAnyValue()
    {
        var callee = Method("Callee", Int32);
        var proof = Prove([Int32], [Int32],
            I(CilOperation.LoadInt32, new CilOperand.ConstantI4(3)), Local(CilOperation.StoreLocal, 0),
            I(CilOperation.Call, new CilOperand.MethodInstance(callee)), I(CilOperation.Pop),
            I(CilOperation.LoadInt32, new CilOperand.ConstantI4(16)), I(CilOperation.LocalAllocate),
            I(CilOperation.Pop),
            Argument(0), Local(CilOperation.LoadLocal, 0), I(CilOperation.Divide),
            Argument(0), I(CilOperation.Call, new CilOperand.MethodInstance(callee)), I(CilOperation.Divide),
            I(CilOperation.Add), I(CilOperation.Return));
        Assert.True(NonZero(proof, 9));
        Assert.False(NonZero(proof, 12));
    }

    // A branch that cannot go one way leaves nothing on that edge, so the
    // division on it is never reached and keeps its checks.
    [Fact]
    public void ABranchThatCannotBeTakenReachesNothing()
    {
        var proof = Prove([Int32], [],
            I(CilOperation.LoadInt32, new CilOperand.ConstantI4(3)), I(CilOperation.LoadInt32, new CilOperand.ConstantI4(3)),
            I(CilOperation.BranchIfLessThanSigned, new CilOperand.BranchTarget(7)),
            Argument(0), I(CilOperation.LoadInt32, new CilOperand.ConstantI4(3)), I(CilOperation.Divide), I(CilOperation.Return),
            Argument(0), I(CilOperation.LoadInt32, new CilOperand.ConstantI4(5)), I(CilOperation.Divide), I(CilOperation.Return));
        Assert.True(NonZero(proof, 5));
        Assert.False(NonZero(proof, 9));
    }

    // value != 3 says nothing of 3 when 3 is the other operand's only value.
    [Fact]
    public void AnInequalityWithASingleValueNarrowsTheOtherSide()
    {
        var proof = Prove([Int32, Int32], [],
            Argument(1), I(CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(CilOperation.BranchIfLessThanSigned, new CilOperand.BranchTarget(10)),
            I(CilOperation.LoadInt32, new CilOperand.ConstantI4(0)), Argument(1),
            I(CilOperation.BranchIfEqual, new CilOperand.BranchTarget(10)),
            Argument(0), Argument(1), I(CilOperation.Divide), I(CilOperation.Return),
            I(CilOperation.LoadInt32, new CilOperand.ConstantI4(0)), I(CilOperation.Return));
        Assert.True(NonZero(proof, 8));
    }

    // A branch on a reference narrows nothing.
    [Fact]
    public void ABranchOnAReferenceNarrowsNothing()
    {
        var proof = Prove([Int32, Object], [],
            Argument(1), I(CilOperation.BranchIfFalse, new CilOperand.BranchTarget(6)),
            Argument(0), Argument(0), I(CilOperation.Divide), I(CilOperation.Return),
            I(CilOperation.LoadInt32, new CilOperand.ConstantI4(0)), I(CilOperation.Return));
        Assert.False(NonZero(proof, 4));
    }

    // Two counters that grow without bound widen past each other's bounds, then to their limits.
    [Fact]
    public void BoundsThatKeepGrowingWidenToTheirLimits()
    {
        var proof = Prove([Int32], [Int32, Int32],
            I(CilOperation.LoadInt32, new CilOperand.ConstantI4(1)), Local(CilOperation.StoreLocal, 0),
            I(CilOperation.LoadInt32, new CilOperand.ConstantI4(2)), Local(CilOperation.StoreLocal, 1),
            Local(CilOperation.LoadLocal, 0), Local(CilOperation.LoadLocal, 1), I(CilOperation.Add), Local(CilOperation.StoreLocal, 0),
            Local(CilOperation.LoadLocal, 1), I(CilOperation.LoadInt32, new CilOperand.ConstantI4(3)), I(CilOperation.Add),
            Local(CilOperation.StoreLocal, 1),
            Argument(0), I(CilOperation.BranchIfTrue, new CilOperand.BranchTarget(4)),
            Argument(0), Local(CilOperation.LoadLocal, 0), I(CilOperation.Divide), I(CilOperation.Return));
        Assert.False(NonZero(proof, 16));
    }

    // A value on the stack across a loop's back edge widens with the loop.
    [Fact]
    public void StackValuesAcrossALoopWiden()
    {
        var proof = Prove([Int32], [],
            Argument(0), I(CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(CilOperation.LoadInt32, new CilOperand.ConstantI4(1)), I(CilOperation.Add),
            Argument(0), I(CilOperation.BranchIfTrue, new CilOperand.BranchTarget(2)),
            I(CilOperation.Duplicate), I(CilOperation.Pop), I(CilOperation.Divide), I(CilOperation.Return));
        Assert.False(NonZero(proof, 8));
    }

    // A handler starts with its slots holding any value: the local the try
    // set to 4 may be anything in the catch, while the division after the
    // region, which only the try's leave reaches, sees the 4.
    [Fact]
    public void HandlersStartKnowingNothing()
    {
        var proof = ProveBody([Int32], [Int32],
            [I(CilOperation.LoadInt32, new CilOperand.ConstantI4(4)), Local(CilOperation.StoreLocal, 0),
             Argument(0), Local(CilOperation.LoadLocal, 0), I(CilOperation.Divide), I(CilOperation.Pop),
             I(CilOperation.Leave, new CilOperand.BranchTarget(13)),
             I(CilOperation.Pop), Argument(0), Local(CilOperation.LoadLocal, 0), I(CilOperation.Divide), I(CilOperation.Pop),
             I(CilOperation.Rethrow),
             Argument(0), Local(CilOperation.LoadLocal, 0), I(CilOperation.Divide), I(CilOperation.Return)],
            regions: [new(CilExceptionRegionKind.Catch, 0, 7, 7, 6, null, null) { CatchTypeIdentity = Object }]);
        Assert.True(NonZero(proof, 4));
        Assert.False(NonZero(proof, 10));
        Assert.True(NonZero(proof, 15));
    }

    // An instance method's receiver is not an integer.
    [Fact]
    public void AnInstanceMethodsReceiverIsNotFollowed()
    {
        var proof = ProveBody([Int32], [],
            [Argument(1), I(CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
             I(CilOperation.BranchIfLessThanSigned, new CilOperand.BranchTarget(7)),
             Argument(1), Argument(1), I(CilOperation.Divide), I(CilOperation.Return),
             I(CilOperation.LoadInt32, new CilOperand.ConstantI4(0)), I(CilOperation.Return)],
            instance: true);
        Assert.True(NonZero(proof, 5));
    }

    // A float division is not an integer check, and a division unreachable
    // from the entry has no ranges.
    [Fact]
    public void FloatsAndUnreachableDivisionsKeepTheirChecks()
    {
        var proof = Prove([Int32], [],
            I(CilOperation.LoadFloat64, new CilOperand.ConstantF8(1)), I(CilOperation.LoadFloat64, new CilOperand.ConstantF8(2)),
            I(CilOperation.Divide), I(CilOperation.Pop),
            Argument(0), I(CilOperation.LoadInt32, new CilOperand.ConstantI4(3)), I(CilOperation.Divide), I(CilOperation.Return),
            Argument(0), I(CilOperation.LoadInt32, new CilOperand.ConstantI4(3)), I(CilOperation.Divide), I(CilOperation.Return));
        Assert.False(NonZero(proof, 2));
        Assert.True(NonZero(proof, 6));
        Assert.False(NonZero(proof, 10));
    }

    [Fact]
    public void TheEliminatorAndAnalysisRequireTheirInputs()
    {
        var ranges = new IntegerRangeAnalysis(new StackEffectResolver(new Calls()));
        var graph = Validator().Validate(new ControlFlowGraphBuilderFactory().Create().Build(new CilMethodBody(
            new MethodDefinitionModel(new(Assembly, 0x06000001), new(Assembly, 0x02000001), "Empty", true,
                new MethodSignatureModel(CliTypeIdentity.FromStackKind(CliValueKind.Void), []), 1),
            0, [], [new CilInstruction(0, 1, CilOperation.Return, new CilOperand.None())])));
        Assert.Throws<ArgumentNullException>(() =>
            new IntegerCheckEliminator(new ControlFlowGraphBuilderFactory().Create(), Validator(), ranges, true).Rewrite(null!));
        Assert.Throws<ArgumentNullException>(() => ranges.StacksAt(null!, new HashSet<int>()));
        Assert.Throws<ArgumentNullException>(() => ranges.StacksAt(graph, null!));
    }

    private static bool NonZero(IntegerCheckProof? proof, int offset) => proof?.NonZeroDivisors.Contains(offset) == true;

    private static bool NonOverflowing(IntegerCheckProof? proof, int offset) => proof?.NonOverflowing.Contains(offset) == true;

    private static IntegerCheckProof? Prove(CliTypeIdentity[] parameters, CliTypeIdentity[] locals, params Code[] code) =>
        ProveBody(parameters, locals, code);

    private static IntegerCheckProof? ProveBody(CliTypeIdentity[] parameters, CliTypeIdentity[] locals, Code[] code,
        CilExceptionRegion[]? regions = null, CliTypeIdentity? returns = null, bool instance = false)
    {
        var method = new MethodDefinitionModel(new(Assembly, 0x06000001), new(Assembly, 0x02000001), "Probe", !instance,
            new MethodSignatureModel(returns ?? Int32, [.. parameters]), 1);
        var body = new CilMethodBody(method, 8, [.. locals.Select(static local => local.StackKind)],
            [.. code.Select(static (instruction, index) =>
                new CilInstruction(index, index + 1, instruction.Operation, instruction.Operand ?? new CilOperand.None()))])
        {
            LocalSignatureTypes = [.. locals],
            ExceptionRegions = [.. regions ?? []],
        };
        return new IntegerCheckEliminator(new ControlFlowGraphBuilderFactory().Create(), Validator(),
            new IntegerRangeAnalysis(new StackEffectResolver(new Calls())), true).Rewrite(body).IntegerCheckProof;
    }

    private static TypedStackValidator Validator() =>
        new(new Repository(), new Repository(), new Repository(), new StackTypeCompatibilityValidator());

    private static Code I(CilOperation operation, CilOperand? operand = null) => (operation, operand);

    private static Code Argument(int index) => (CilOperation.LoadArgument, new CilOperand.Index(index));

    private static Code Local(CilOperation operation, int index) => (operation, new CilOperand.Index(index));

    private static MethodInstanceModel Method(string name, CliTypeIdentity returns)
    {
        var signature = new MethodSignatureModel(returns, []);
        var definition = new MethodDefinitionModel(new(Assembly, 0x06000002), new(Assembly, 0x02000002), name, true, signature, 1);
        return new(definition, CliTypeIdentity.Named(Assembly, "Fixture", "Owner", isValueType: false), [], signature);
    }

    private sealed class Calls : ICalledMethodResolver
    {
        public MethodInstanceModel? Resolve(CilInstruction instruction) =>
            instruction.Operand is CilOperand.MethodInstance method ? method.Value : null;
    }

    private sealed class Repository : ITypeRepository, IFieldRepository, IMethodRepository
    {
        public TypeDefinitionModel GetTypeDefinition(EntityKey key) => throw new NotSupportedException();

        public FieldDefinitionModel GetField(EntityKey key) => throw new NotSupportedException();

        public MethodDefinitionModel GetMethod(EntityKey key) => throw new NotSupportedException();
    }
}
