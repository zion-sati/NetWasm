// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Analysis.Arrays;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

// for (j = 0; j < count; j++) { if ((uint)(start + j) >= (uint)length) throw; sum += start + j; }: a span read at an
// offset within a chunk. Its check holds for every j once start >= 0 and start + count <= length, so the loop runs a
// copy without it when a test before the loop says so.
public sealed class ArrayLoopVersionerTests
{
    private const int Index = 0, Start = 1, Length = 2, Count = 3, Sum = 4, Array = 5;

    [Fact]
    public void RejectsMissingCollaboratorsAndBodies()
    {
        var (graphs, stacks) = Collaborators();
        var planner = Planner();
        Assert.Throws<ArgumentNullException>(() => new ArrayLoopVersioner(null!, stacks, planner, 256));
        Assert.Throws<ArgumentNullException>(() => new ArrayLoopVersioner(graphs, null!, planner, 256));
        Assert.Throws<ArgumentNullException>(() => new ArrayLoopVersioner(graphs, stacks, null!, 256));
        Assert.Throws<ArgumentNullException>(() => Versioner().Version(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ArrayLoopVersioner(graphs, stacks, planner, -1).Version(Body(new())));
        Assert.Throws<ArgumentNullException>(() => new ArrayLoopVersionPlanner(null!));
        Assert.Throws<ArgumentNullException>(() => planner.Plan(null!));
    }

    public static TheoryData<int, int, CilOperation, bool, bool> Planned => new()
    {
        { 0, 0, CilOperation.BranchIfLessThanUnsigned, false, false },
        { 1, 1, CilOperation.BranchIfLessThanUnsigned, false, false },
        { 2, 1, CilOperation.BranchIfLessThanUnsigned, false, false },
        { 0, 0, CilOperation.BranchIfLessThanSigned, true, false },
        { 0, 0, CilOperation.BranchIfGreaterThanOrEqualUnsigned, false, true },
        { 0, 0, CilOperation.BranchIfGreaterThanOrEqualSigned, false, false },
    };

    // Each check form, either operand order, and a branch in the body that compares longs, which is no check.
    [Theory]
    [MemberData(nameof(Planned))]
    public void PlansTheCheckAndItsRange(int guardAhead, int ahead, CilOperation check, bool indexFirst, bool longCompare)
    {
        var shape = new Shape
        {
            GuardAhead = guardAhead,
            Ahead = ahead,
            CheckOperation = check,
            IndexFirst = indexFirst,
            LongCompare = longCompare,
        };
        var version = Assert.Single(Planner().Plan(Validate(Body(shape))));
        Assert.Equal((Start, Length), Assert.Single(version.Ranges));
        Assert.Equal(shape.Check, Assert.Single(version.Checks));
        Assert.Equal(shape.Entry, Assert.Single(version.Entries));
    }

    // The precheck widens start and length signed and the bound unsigned, enters the copy when both hold and the
    // loop as written otherwise; the copy's check is the proven one.
    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void TestsTheRangeBeforeRunningACopy(int initial)
    {
        var shape = new Shape { Initial = initial };
        var body = Body(shape);
        var (result, proven) = Versioner().Version(body);

        var test = body.Instructions[^1].NextOffset;
        Assert.Equal(new CilOperand.BranchTarget(test), result.Instructions[shape.Entry].Operand);
        Assert.Equal(
            [
                CilOperation.LoadLocal, CilOperation.LoadInt32, CilOperation.CompareLessThanSigned,
                CilOperation.LoadLocal, CilOperation.ConvertInt64, CilOperation.LoadLocal, CilOperation.ConvertInt64Unsigned,
                CilOperation.Add, CilOperation.LoadLocal, CilOperation.ConvertInt64, CilOperation.CompareGreaterThanSigned,
                CilOperation.BitwiseOr, CilOperation.BranchIfTrue,
                CilOperation.LoadInt32, CilOperation.StoreLocal, CilOperation.Branch,
            ],
            result.Instructions[test..(test + 16)].Select(instruction => instruction.Operation));
        // Either failure takes the loop as written, in one branch.
        Assert.Equal(new CilOperand.BranchTarget(shape.Guard), result.Instructions[test + 12].Operand);
        // The copy counts from the loop's start with an index of its own.
        Assert.Equal(new CilOperand.ConstantI4(shape.Initial), result.Instructions[test + 13].Operand);
        var copyIndex = Assert.IsType<CilOperand.Index>(result.Instructions[test + 14].Operand).Value;
        Assert.Equal(body.Locals.Length, copyIndex);
        Assert.Equal(CliValueKind.I4, result.Locals[copyIndex]);
        Assert.DoesNotContain(result.Instructions[(test + 15)..], instruction =>
            instruction is { Operation: CilOperation.LoadLocal or CilOperation.StoreLocal, Operand: CilOperand.Index { Value: Index } });
        var copiedCheck = Assert.Single(proven);
        Assert.Equal(CilOperation.BranchIfLessThanUnsigned, result.Instructions[copiedCheck].Operation);
        Assert.Equal(shape.Check, result.Instructions[copiedCheck].OriginalOffset);
        // The copy's check skips to the copy's read; the throw it passes over stays the original's.
        var read = ((CilOperand.BranchTarget)result.Instructions[copiedCheck].Operand).Offset;
        Assert.True(read > test);
        Assert.Equal(new CilOperand.BranchTarget(shape.Check + 1), result.Instructions[copiedCheck + 1].Operand);
        Assert.All(result.Instructions[test..], instruction => Assert.NotNull(instruction.OriginalOffset));
        _ = Validate(result);
    }

    // A check below the index or past the lookahead, or less a third local the loop leaves alone, as a pixel's left
    // neighbor and the row above are read, is an offset range of its own.
    [Theory]
    [InlineData(-1, false)]
    [InlineData(1, false)]
    [InlineData(0, true)]
    [InlineData(-1, true)]
    public void PlansACheckAtANegativeOrVariableOffsetAsAnOffsetRange(int ahead, bool term)
    {
        var shape = new Shape { Ahead = ahead, Term = term };
        var version = Assert.Single(Planner().Plan(Validate(Body(shape))));
        Assert.Empty(version.Ranges);
        Assert.Equal(new ArrayLoopOffsetRange(Start, Length, ahead, ahead, term ? Count : null, term),
            Assert.Single(version.OffsetRanges));
        Assert.Equal(shape.Check, Assert.Single(version.Checks));
    }

    // A check at the index alone, plus a constant, in a loop bounded by a count rather than by the length it compares
    // against, as `values[index]` is read for index < count, is an offset range with no start.
    [Theory]
    [InlineData(0, 0)]
    [InlineData(2, 0)]
    [InlineData(-1, 1)]
    public void PlansACheckAtTheIndexAloneAsARangeWithNoStart(int ahead, int initial)
    {
        var shape = new Shape { NoStart = true, Ahead = ahead, Initial = initial };
        var version = Assert.Single(Planner().Plan(Validate(Body(shape))));
        Assert.Empty(version.Ranges);
        Assert.Equal(new ArrayLoopOffsetRange(ArrayLoopOffsetRange.NoStart, Length, ahead, ahead, null, false),
            Assert.Single(version.OffsetRanges));
        Assert.Equal(shape.Check, Assert.Single(version.Checks));
    }

    // Its test has no lower side: one past the largest index read, bound + High - lookahead, past the length runs the
    // loop as written.
    [Fact]
    public void TestsARangeWithNoStartAgainstTheBoundAlone()
    {
        var shape = new Shape { NoStart = true, Ahead = 2 };
        var body = Body(shape);
        var (result, proven) = Versioner().Version(body);

        var test = body.Instructions[^1].NextOffset;
        Assert.Equal(new CilOperand.BranchTarget(test), result.Instructions[shape.Entry].Operand);
        Assert.Equal(
            [
                CilOperation.LoadLocal, CilOperation.ConvertInt64Unsigned, CilOperation.LoadInt64, CilOperation.Add,
                CilOperation.LoadLocal, CilOperation.ConvertInt64, CilOperation.CompareGreaterThanSigned, CilOperation.BranchIfTrue,
                CilOperation.LoadInt32, CilOperation.StoreLocal, CilOperation.Branch,
            ],
            result.Instructions[test..(test + 11)].Select(instruction => instruction.Operation));
        Assert.Equal(new CilOperand.Index(Count), result.Instructions[test].Operand);
        Assert.Equal(new CilOperand.ConstantI8(2), result.Instructions[test + 2].Operand);
        Assert.Equal(new CilOperand.Index(Length), result.Instructions[test + 4].Operand);
        Assert.Equal(new CilOperand.BranchTarget(shape.Guard), result.Instructions[test + 7].Operand);
        Assert.Equal(shape.Check, result.Instructions[Assert.Single(proven)].OriginalOffset);
        _ = Validate(result);
    }

    // The offset range's test: the smallest index read, start + initial + Low - term, below zero, or one past the
    // largest, start + bound + High - lookahead - term, past the length, runs the loop as written.
    [Fact]
    public void TestsAnOffsetRangeInSixtyFourBits()
    {
        var shape = new Shape { Ahead = -1, Term = true, Initial = 2 };
        var body = Body(shape);
        var (result, proven) = Versioner().Version(body);

        var test = body.Instructions[^1].NextOffset;
        Assert.Equal(new CilOperand.BranchTarget(test), result.Instructions[shape.Entry].Operand);
        Assert.Equal(
            [
                CilOperation.LoadLocal, CilOperation.ConvertInt64, CilOperation.LoadInt64, CilOperation.Add,
                CilOperation.LoadLocal, CilOperation.ConvertInt64, CilOperation.Subtract,
                CilOperation.LoadInt64, CilOperation.CompareLessThanSigned,
                CilOperation.LoadLocal, CilOperation.ConvertInt64, CilOperation.LoadLocal, CilOperation.ConvertInt64Unsigned,
                CilOperation.Add, CilOperation.LoadInt64, CilOperation.Add,
                CilOperation.LoadLocal, CilOperation.ConvertInt64, CilOperation.Subtract,
                CilOperation.LoadLocal, CilOperation.ConvertInt64, CilOperation.CompareGreaterThanSigned,
                CilOperation.BitwiseOr, CilOperation.BranchIfTrue,
            ],
            result.Instructions[test..(test + 24)].Select(instruction => instruction.Operation));
        Assert.Equal(new CilOperand.Index(Start), result.Instructions[test].Operand);
        Assert.Equal(new CilOperand.ConstantI8(2 - 1), result.Instructions[test + 2].Operand);
        Assert.Equal(new CilOperand.Index(Count), result.Instructions[test + 4].Operand);
        Assert.Equal(new CilOperand.ConstantI8(-1), result.Instructions[test + 14].Operand);
        Assert.Equal(new CilOperand.Index(Length), result.Instructions[test + 19].Operand);
        Assert.Equal(new CilOperand.BranchTarget(shape.Guard), result.Instructions[test + 23].Operand);
        Assert.Equal(shape.Check, result.Instructions[Assert.Single(proven)].OriginalOffset);
        _ = Validate(result);
    }

    // Two checks of one range at different offsets, as a pixel's two left neighbors are read, merge into one offset
    // range from the lower offset to the higher.
    [Fact]
    public void TwoChecksOfOneRangeMergeTheirOffsets()
    {
        var shape = new Shape { Ahead = -1, SecondAhead = -3 };

        var version = Assert.Single(Planner().Plan(Validate(Body(shape))));

        Assert.Equal(new ArrayLoopOffsetRange(Start, Length, -3, -1, null, false), Assert.Single(version.OffsetRanges));
        Assert.Equal(2, version.Checks.Count);
    }

    // A term the index adds, as a third local the loop leaves alone, is added in the test too.
    [Fact]
    public void TestsAnOffsetRangeWithAnAddedTerm()
    {
        var shape = new Shape { Ahead = -1, Term = true, TermAdded = true, Initial = 2 };
        var body = Body(shape);
        var version = Assert.Single(Planner().Plan(Validate(body)));
        var (result, proven) = Versioner().Version(body);

        Assert.False(Assert.Single(version.OffsetRanges).TermNegated);
        var test = body.Instructions[^1].NextOffset;
        var terms = Enumerable.Range(test, result.Instructions.Length - test - 2)
            .Where(index => result.Instructions[index] is { Operation: CilOperation.LoadLocal, Operand: CilOperand.Index { Value: Count } } &&
                result.Instructions[index + 1].Operation == CilOperation.ConvertInt64)
            .Select(index => result.Instructions[index + 2].Operation).ToArray();
        Assert.Equal([CilOperation.Add, CilOperation.Add], terms);
        Assert.Single(proven);
        _ = Validate(result);
    }

    [Fact]
    public void AnArrayBoundIsMeasuredInTheTest()
    {
        var body = Body(new() { ArrayBound = true });
        var (result, proven) = Versioner().Version(body);
        Assert.Single(proven);
        Assert.Contains(result.Instructions, instruction => instruction.Operation == CilOperation.LoadArrayLength &&
            instruction.Offset > body.Instructions[^1].Offset);
        _ = Validate(result);
    }

    // if ((index & 1) != 0) sum++: the copy's branch skips to the copy's increment.
    [Fact]
    public void ABranchInTheBodyBranchesWithinTheCopy()
    {
        var shape = new Shape { Odd = true };
        var body = Body(shape);
        var (result, _) = Versioner().Version(body);
        var skips = result.Instructions.Where(instruction => instruction.Operation == CilOperation.BranchIfFalse).ToArray();
        Assert.Equal(2, skips.Length);
        Assert.Equal(new CilOperand.BranchTarget(Body(shape).Instructions.Single(instruction =>
            instruction.Operation == CilOperation.BranchIfFalse).Operand is CilOperand.BranchTarget { Offset: var next } ? next : -1),
            skips[0].Operand);
        var copied = ((CilOperand.BranchTarget)skips[1].Operand).Offset;
        Assert.True(copied > body.Instructions[^1].Offset);
        Assert.Equal(CilOperation.LoadLocal, result.Instructions[copied].Operation);
        Assert.Equal(body.Locals.Length, ((CilOperand.Index)result.Instructions[copied].Operand).Value);
        _ = Validate(result);
    }

    public static TheoryData<string, Shape> Unversioned => new()
    {
        { "start changes in the loop", new() { StoreStart = true } },
        { "start's address is taken", new() { Exposed = Start } },
        { "length's address is taken", new() { Exposed = Length } },
        { "the check's sum leaves the index out", new() { OtherSum = true } },
        { "the check compares against the index", new() { LengthIsIndex = true } },
        { "the check's length is a sum", new() { LengthSum = true } },
        { "the check's length is offset", new() { LengthOffset = true } },
        { "the check compares against start", new() { LengthIsStart = true } },
        { "the check reads the index alone against the loop's own bound", new() { NoStart = true, BoundIsLength = true } },
        { "the check reads below the index's first value", new() { NoStart = true, Ahead = -1 } },
        { "the loop is entered by falling into it", new() { FallInto = true } },
        { "the index is read after the loop", new() { ReadIndexAfter = true } },
        { "an enclosing loop reads the index before writing it", new() { Outer = true, ReadIndexBefore = true } },
    };

    [Theory]
    [MemberData(nameof(Unversioned))]
    public void LoopsItCannotProveAreKept(string reason, Shape shape)
    {
        Assert.NotEmpty(reason);
        var body = Body(shape);
        var (result, proven) = Versioner().Version(body);
        Assert.Same(body, result);
        Assert.Empty(proven);
    }

    // A read before the loop sees the index's start, as an address induction's initialization does; a path from the
    // exit back through an enclosing loop writes it before the loop reads it.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AReadOfTheStartOnTheWayInIsFine(bool outer)
    {
        var (_, proven) = Versioner().Version(Body(new() { ReadIndexBefore = true, Outer = outer, OuterWritesFirst = true }));
        Assert.Single(proven);
    }

    // for (k = 0; k < count; k++) { check(start + k); for (j = 0; j < count; j++) { check(start + j); sum += j; } }:
    // both loops can be versioned; the inner one is, and the enclosing one, which holds it, is left as written.
    [Fact]
    public void AnInnerLoopIsVersionedBeforeTheLoopAroundIt()
    {
        const int Outer = 6;
        List<Line> code =
        [
            new(CilOperation.LoadArgument, new CilOperand.Index(0)), new(CilOperation.StoreLocal, Local(Start)),
            new(CilOperation.LoadArgument, new CilOperand.Index(1)), new(CilOperation.StoreLocal, Local(Count)),
            new(CilOperation.LoadArgument, new CilOperand.Index(2)), new(CilOperation.StoreLocal, Local(Length)),
            new(CilOperation.LoadInt32, new CilOperand.ConstantI4(0)), new(CilOperation.StoreLocal, Local(Sum)),
            new(CilOperation.LoadInt32, new CilOperand.ConstantI4(0)), new(CilOperation.StoreLocal, Local(Outer)),
            new(CilOperation.Branch, To("outer guard")),
            new(CilOperation.LoadLocal, Local(Start)) { Label = "outer body" },
            new(CilOperation.LoadLocal, Local(Outer)), new(CilOperation.Add), new(CilOperation.LoadLocal, Local(Length)),
            new(CilOperation.BranchIfLessThanUnsigned, To("outer read")) { Label = "outer check" },
            new(CilOperation.LoadNull), new(CilOperation.Throw),
            new(CilOperation.LoadInt32, new CilOperand.ConstantI4(0)) { Label = "outer read" },
            new(CilOperation.StoreLocal, Local(Index)),
            new(CilOperation.Branch, To("inner guard")),
            new(CilOperation.LoadLocal, Local(Start)) { Label = "inner body" },
            new(CilOperation.LoadLocal, Local(Index)), new(CilOperation.Add), new(CilOperation.LoadLocal, Local(Length)),
            new(CilOperation.BranchIfLessThanUnsigned, To("inner read")) { Label = "inner check" },
            new(CilOperation.LoadNull), new(CilOperation.Throw),
            new(CilOperation.LoadLocal, Local(Sum)) { Label = "inner read" },
            new(CilOperation.LoadLocal, Local(Index)), new(CilOperation.Add), new(CilOperation.StoreLocal, Local(Sum)),
            new(CilOperation.LoadLocal, Local(Index)), new(CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            new(CilOperation.Add), new(CilOperation.StoreLocal, Local(Index)),
            new(CilOperation.LoadLocal, Local(Index)) { Label = "inner guard" },
            new(CilOperation.LoadLocal, Local(Count)),
            new(CilOperation.BranchIfLessThanSigned, To("inner body")),
            new(CilOperation.LoadLocal, Local(Outer)), new(CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            new(CilOperation.Add), new(CilOperation.StoreLocal, Local(Outer)),
            new(CilOperation.LoadLocal, Local(Outer)) { Label = "outer guard" },
            new(CilOperation.LoadLocal, Local(Count)),
            new(CilOperation.BranchIfLessThanSigned, To("outer body")),
            new(CilOperation.LoadLocal, Local(Sum)), new(CilOperation.Return),
        ];
        var body = Assemble(code, extraLocals: 1);
        var labels = Labels(code);
        Assert.Equal(2, Planner().Plan(Validate(body)).Length);

        var (result, proven) = Versioner().Version(body);

        var copied = Assert.Single(proven);
        Assert.Equal(labels["inner check"], result.Instructions[copied].OriginalOffset);
        _ = Validate(result);
    }

    // A loop whose increment branches back to its guard rather than falling into it.
    [Fact]
    public void AnIncrementThatBranchesBackIsCopiedWithItsBranch()
    {
        var shape = new Shape { BranchBack = true };
        var body = Body(shape);
        var (result, proven) = Versioner().Version(body);
        Assert.Single(proven);
        var test = body.Instructions[^1].NextOffset;
        var copiedGuard = ((CilOperand.BranchTarget)result.Instructions[test + 15].Operand).Offset;
        Assert.Contains(result.Instructions[test..], instruction =>
            instruction.Operation == CilOperation.Branch && instruction.Operand == new CilOperand.BranchTarget(copiedGuard) &&
            instruction.OriginalOffset is { } source && source != shape.Guard);
        _ = Validate(result);
    }

    [Fact]
    public void ALoopOverTheBudgetIsKept()
    {
        var body = Body(new());
        var (result, proven) = new ArrayLoopVersioner(Collaborators().Graphs, Collaborators().Stacks, Planner(), 4).Version(body);
        Assert.Same(body, result);
        Assert.Empty(proven);
    }

    [Fact]
    public void ABodyWithoutLoopsIsKept()
    {
        var body = Assemble([new(CilOperation.LoadInt32, new CilOperand.ConstantI4(0)), new(CilOperation.Return)]);
        Assert.Same(body, Versioner().Version(body).Body);
        Assert.Empty(Planner().Plan(Validate(body)));
    }

    // The eliminator folds the copy's check: its branch is taken, so the copy reads without testing.
    [Fact]
    public void TheEliminatorFoldsTheCopysCheck()
    {
        var (graphs, stacks) = Collaborators();
        var eliminator = new ArrayBoundsCheckEliminator(graphs, stacks,
            new ArrayBoundsCheckEliminationPlanner(Induction(), new ControlFlowDominanceAnalyzer(new DominatorTreeBuilder())), true, Versioner());
        var body = Body(new());
        var result = eliminator.Rewrite(body);
        Assert.Equal(1, result.Instructions.Count(instruction => instruction.Operation == CilOperation.BranchIfLessThanUnsigned));
        // Each loop's guard; the test computes its start < 0 and branches once.
        Assert.Equal(2, result.Instructions.Count(instruction => instruction.Operation == CilOperation.BranchIfLessThanSigned));
        Assert.Equal(1, result.Instructions.Count(instruction => instruction.Operation == CilOperation.BranchIfTrue));
        _ = Validate(result);
    }

    // Whether the origin is the two locals' sum, in either order.
    private static bool Sums(ValueOrigin origin, int local, int other) => origin.Base is { } second &&
        (origin.Local == local && second == other || origin.Local == other && second == local);

    // Only two plain locals sum: an argument, a constant, an array's length or a sum on either side has no origin, and
    // neither has a sum's length.
    public static TheoryData<string, CilInstruction[]> Unsummed => new()
    {
        { "argument", [Op(CilOperation.LoadArgument, 0), Op(CilOperation.LoadLocal, Start), Op(CilOperation.Add)] },
        { "constant", [Op(CilOperation.LoadInt32, 3), Op(CilOperation.LoadLocal, Start), Op(CilOperation.Add)] },
        { "length on the left", [Op(CilOperation.LoadLocal, Array), Op(CilOperation.LoadArrayLength),
            Op(CilOperation.LoadLocal, Start), Op(CilOperation.Add)] },
        { "length on the right", [Op(CilOperation.LoadLocal, Start), Op(CilOperation.LoadLocal, Array),
            Op(CilOperation.LoadArrayLength), Op(CilOperation.Add)] },
        { "sum on the right", [Op(CilOperation.LoadLocal, Start), Op(CilOperation.LoadLocal, Index),
            Op(CilOperation.LoadLocal, Length), Op(CilOperation.Add), Op(CilOperation.Add)] },
        { "a sum's length", [Op(CilOperation.LoadLocal, Start), Op(CilOperation.LoadLocal, Index), Op(CilOperation.Add),
            Op(CilOperation.LoadArrayLength)] },
    };

    [Theory]
    [MemberData(nameof(Unsummed))]
    public void OnlyTwoPlainLocalsSum(string operands, CilInstruction[] code)
    {
        Assert.NotEmpty(operands);
        var origins = new List<ValueOrigin>();
        foreach (var instruction in code) ValueOrigins.Track(instruction, origins, []);
        Assert.Equal(ValueOrigin.Unknown, Assert.Single(origins));
    }

    private static CilInstruction Op(CilOperation operation, int? value = null) => new(0, 1, operation, operation switch
    {
        CilOperation.LoadInt32 => new CilOperand.ConstantI4(value!.Value),
        _ when value is { } index => new CilOperand.Index(index),
        _ => new CilOperand.None(),
    });

    // Storing either addend forgets the sum, on the stack and in a copy.
    [Fact]
    public void StoringAnAddendForgetsTheSum()
    {
        var origins = new List<ValueOrigin>();
        var copies = new Dictionary<int, ValueOrigin>();
        void Run(CilOperation operation, int? local = null) => ValueOrigins.Track(
            new(0, 1, operation, local is { } index ? new CilOperand.Index(index) : new CilOperand.None()), origins, copies);
        Run(CilOperation.LoadLocal, Start);
        Run(CilOperation.LoadLocal, Index);
        Run(CilOperation.Add);
        Assert.True(Sums(origins[^1], Index, Start));
        Run(CilOperation.Duplicate);
        Run(CilOperation.StoreLocal, Sum);
        Assert.True(Sums(copies[Sum], Index, Start));
        Run(CilOperation.LoadLocal, Sum);
        Assert.True(Sums(origins[^1], Index, Start));
        Run(CilOperation.LoadInt32, Index);
        Run(CilOperation.StoreLocal, Index);
        Assert.False(copies.ContainsKey(Sum));
        Assert.All(origins, origin => Assert.Equal(ValueOrigin.Unknown, origin));
        // Offsets whose sum leaves int's range are no origin.
        Run(CilOperation.LoadLocal, Start);
        ValueOrigins.Track(new(0, 1, CilOperation.LoadInt32, new CilOperand.ConstantI4(int.MaxValue)), origins, copies);
        Run(CilOperation.Add);
        Run(CilOperation.LoadLocal, Length);
        ValueOrigins.Track(new(0, 1, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)), origins, copies);
        Run(CilOperation.Add);
        Run(CilOperation.Add);
        Assert.Equal(ValueOrigin.Unknown, origins[^1]);
        // A third local less from the sum is its term, and storing that local forgets the sum too.
        Run(CilOperation.LoadLocal, Start);
        Run(CilOperation.LoadLocal, Index);
        Run(CilOperation.Add);
        Run(CilOperation.LoadLocal, Length);
        Run(CilOperation.Subtract);
        Assert.Equal(Length, origins[^1].Term);
        Assert.True(origins[^1].TermNegated);
        Run(CilOperation.Duplicate);
        Run(CilOperation.StoreLocal, Sum);
        Assert.Equal(Length, copies[Sum].Term);
        Run(CilOperation.LoadInt32, Length);
        Run(CilOperation.StoreLocal, Length);
        Assert.False(copies.ContainsKey(Sum));
        Assert.Equal(ValueOrigin.Unknown, origins[^1]);
        // A sum stored back into one of its addends is not a copy.
        Run(CilOperation.LoadLocal, Start);
        Run(CilOperation.LoadLocal, Index);
        Run(CilOperation.Add);
        Run(CilOperation.StoreLocal, Index);
        Assert.False(copies.ContainsKey(Index));
    }

    public sealed record Shape
    {
        public bool ArrayBound { get; init; }
        public bool Odd { get; init; }
        public bool StoreStart { get; init; }
        public int Exposed { get; init; } = -1;
        public int Ahead { get; init; }
        public bool LengthIsStart { get; init; }
        public bool NoStart { get; init; }
        public bool BoundIsLength { get; init; }
        public bool FallInto { get; init; }
        public int GuardAhead { get; init; }
        public bool ReadIndexAfter { get; init; }
        public bool ReadIndexBefore { get; init; }
        public bool BranchBack { get; init; }
        public CilOperation CheckOperation { get; init; } = CilOperation.BranchIfLessThanUnsigned;
        public bool IndexFirst { get; init; }
        public bool OtherSum { get; init; }
        public bool LengthIsIndex { get; init; }
        public bool LengthSum { get; init; }
        public bool LengthOffset { get; init; }
        public bool LongCompare { get; init; }
        public bool Outer { get; init; }
        public bool OuterWritesFirst { get; init; }
        public int Initial { get; init; }
        public bool Term { get; init; }
        public bool TermAdded { get; init; }
        public int? SecondAhead { get; init; }

        public int Entry { get; set; }
        public int Check { get; set; }
        public int Guard { get; set; }
    }

    // Locals: index, start, length, count, sum, and an int array the array-bound shape measures.
    private static CilMethodBody Body(Shape shape)
    {
        var code = new List<Line>
        {
            new(CilOperation.LoadArgument, new CilOperand.Index(0)), new(CilOperation.StoreLocal, Local(Start)),
            new(CilOperation.LoadArgument, new CilOperand.Index(1)), new(CilOperation.StoreLocal, Local(Count)),
            new(CilOperation.LoadArgument, new CilOperand.Index(2)), new(CilOperation.StoreLocal, Local(Length)),
            new(CilOperation.LoadInt32, new CilOperand.ConstantI4(0)), new(CilOperation.StoreLocal, Local(Sum)),
            new(CilOperation.LoadArgument, new CilOperand.Index(3)), new(CilOperation.StoreLocal, Local(Array)),
        };
        // An enclosing loop runs the loop again while the sum is below 100, entering it through its initialization
        // or, unless that writes the index first, through a read of it.
        if (shape.Outer) code.Add(new(CilOperation.Nop) { Label = "outer" });
        if (shape.Outer && !shape.OuterWritesFirst) code.Add(new(CilOperation.LoadLocal, Local(Index)) { Label = "again" });
        if (shape.Outer && !shape.OuterWritesFirst) code.Add(new(CilOperation.Pop));
        code.Add(new(CilOperation.LoadInt32, new CilOperand.ConstantI4(shape.Initial)));
        code.Add(new(CilOperation.StoreLocal, Local(Index)));
        if (shape.ReadIndexBefore)
        {
            code.Add(new(CilOperation.LoadLocal, Local(Index)));
            code.Add(new(CilOperation.Pop));
        }
        List<Line> loop =
        [
            new(CilOperation.LoadLocal, Local(shape.NoStart || shape.IndexFirst ? Index : Start)) { Label = "body" },
            .. shape.NoStart ? [] : new Line[]
            {
                new(CilOperation.LoadLocal, Local(shape.IndexFirst ? Start : shape.OtherSum ? Count : Index)),
                new(CilOperation.Add),
            },
            .. shape.Ahead == 0 ? [] : new Line[]
            {
                new(CilOperation.LoadInt32, new CilOperand.ConstantI4(shape.Ahead)), new(CilOperation.Add),
            },
            .. shape.Term ? new Line[]
            {
                new(CilOperation.LoadLocal, Local(Count)), new(shape.TermAdded ? CilOperation.Add : CilOperation.Subtract),
            } : [],
            new(CilOperation.LoadLocal, Local(shape.LengthIsStart ? Start : shape.LengthIsIndex ? Index : Length)),
            .. shape.LengthSum ? new Line[] { new(CilOperation.LoadLocal, Local(Count)), new(CilOperation.Add) } : [],
            .. shape.LengthOffset ? new Line[]
            {
                new(CilOperation.LoadInt32, new CilOperand.ConstantI4(1)), new(CilOperation.Subtract),
            } : [],
            .. Skips(shape.CheckOperation) ? new Line[]
            {
                new(shape.CheckOperation, To("read")) { Label = "check" },
                new(CilOperation.LoadNull),
                new(CilOperation.Throw),
            } : [new(shape.CheckOperation, To("fail")) { Label = "check" }],
            new(CilOperation.LoadLocal, Local(Sum)) { Label = "read" },
            new(CilOperation.LoadLocal, Local(Start)),
            new(CilOperation.LoadLocal, Local(Index)),
            new(CilOperation.Add),
            new(CilOperation.Add),
            new(CilOperation.StoreLocal, Local(Sum)),
            // A second check of the same range at another offset, in the default check's form: past a throw.
            .. shape.SecondAhead is { } second ? new Line[]
            {
                new(CilOperation.LoadLocal, Local(Start)), new(CilOperation.LoadLocal, Local(Index)), new(CilOperation.Add),
                new(CilOperation.LoadInt32, new CilOperand.ConstantI4(second)), new(CilOperation.Add),
                new(CilOperation.LoadLocal, Local(Length)), new(shape.CheckOperation, To("second")),
                new(CilOperation.LoadNull), new(CilOperation.Throw), new(CilOperation.Nop) { Label = "second" },
            } : [],
            .. shape.LongCompare ? new Line[]
            {
                new(CilOperation.LoadLocal, Local(Sum)),
                new(CilOperation.ConvertInt64),
                new(CilOperation.LoadInt64, new CilOperand.ConstantI8(0)),
                new(CilOperation.BranchIfLessThanUnsigned, To("next")),
            } : [],
            .. shape.Odd ? new Line[]
            {
                new(CilOperation.LoadLocal, Local(Index)),
                new(CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
                new(CilOperation.BitwiseAnd),
                new(CilOperation.BranchIfFalse, To("next")),
                new(CilOperation.LoadLocal, Local(Sum)),
                new(CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
                new(CilOperation.Add),
                new(CilOperation.StoreLocal, Local(Sum)),
            } : [],
            .. shape.StoreStart ? new Line[]
            {
                new(CilOperation.LoadLocal, Local(Start)), new(CilOperation.StoreLocal, Local(Start)),
            } : [],
            new(CilOperation.LoadLocal, Local(Index)) { Label = "next" },
            new(CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            new(CilOperation.Add),
            new(CilOperation.StoreLocal, Local(Index)),
            .. shape.BranchBack ? new Line[] { new(CilOperation.Branch, To("guard")) } : [],
        ];
        List<Line> bound =
        [
            new(CilOperation.LoadLocal, Local(Index)) { Label = "guard" },
            .. shape.GuardAhead == 0 ? [] : new Line[]
            {
                new(CilOperation.LoadInt32, new CilOperand.ConstantI4(shape.GuardAhead)), new(CilOperation.Add),
            },
            .. shape.ArrayBound ? new Line[]
            {
                new(CilOperation.LoadLocal, Local(Array)), new(CilOperation.LoadArrayLength), new(CilOperation.ConvertInt32),
            } : [new(CilOperation.LoadLocal, Local(shape.BoundIsLength ? Length : Count))],
        ];
        if (shape.FallInto)
        {
            // while (index < count) { ... }: the initialization falls into the guard, which leaves the loop.
            code.AddRange(bound);
            code.Add(new(CilOperation.BranchIfGreaterThanOrEqualSigned, To("exit")));
            code.AddRange(loop);
            code.Add(new(CilOperation.Branch, To("guard")));
        }
        else
        {
            code.Add(new(CilOperation.Branch, To("guard")) { Label = "entry" });
            code.AddRange(loop);
            code.AddRange(bound);
            code.Add(new(CilOperation.BranchIfLessThanSigned, To("body")));
        }
        if (shape.Outer)
        {
            code.Add(new(CilOperation.LoadLocal, Local(Sum)) { Label = "exit" });
            code.Add(new(CilOperation.LoadInt32, new CilOperand.ConstantI4(100)));
            code.Add(new(CilOperation.BranchIfLessThanSigned, To(shape.OuterWritesFirst ? "outer" : "again")));
            code.Add(new(CilOperation.LoadLocal, Local(Sum)));
        }
        else
        {
            code.Add(new(CilOperation.LoadLocal, Local(shape.ReadIndexAfter ? Index : Sum)) { Label = "exit" });
        }
        code.Add(new(CilOperation.Return));
        if (!Skips(shape.CheckOperation))
        {
            code.Add(new(CilOperation.LoadNull) { Label = "fail" });
            code.Add(new(CilOperation.Throw));
        }
        if (shape.Exposed >= 0)
        {
            code.AddRange(
            [
                new(CilOperation.LoadLocalAddress, Local(shape.Exposed)),
                new(CilOperation.Pop),
                new(CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
                new(CilOperation.Return),
            ]);
        }
        var labels = Labels(code);
        shape.Entry = labels.GetValueOrDefault("entry", -1);
        shape.Check = labels["check"];
        shape.Guard = labels["guard"];
        return Assemble(code);
    }

    private static CilOperand.Index Local(int index) => new(index);

    // Whether a check branches past its throw, as index < length does, or to it, as index >= length does.
    private static bool Skips(CilOperation check) =>
        check is CilOperation.BranchIfLessThanUnsigned or CilOperation.BranchIfLessThanSigned;

    // A branch to a label, resolved once the body is laid out.
    private static CilOperand.BranchTarget To(string label) => new(Pending(label));

    // Labels stand in as negative offsets until they resolve.
    private static readonly List<string> PendingLabels = [];

    private static int Pending(string label)
    {
        lock (PendingLabels)
        {
            var index = PendingLabels.IndexOf(label);
            if (index < 0)
            {
                PendingLabels.Add(label);
                index = PendingLabels.Count - 1;
            }
            return -1 - index;
        }
    }

    private static int Resolve(int offset, Dictionary<string, int> labels)
    {
        if (offset >= 0) return offset;
        lock (PendingLabels) return labels[PendingLabels[-1 - offset]];
    }

    private static Dictionary<string, int> Labels(List<Line> code) => code
        .Select((line, offset) => (line.Label, Offset: offset))
        .Where(item => item.Label is not null)
        .ToDictionary(item => item.Label!, item => item.Offset);

    // A static method of three int arguments and an int array returning an int, with more int locals as asked.
    private static CilMethodBody Assemble(List<Line> code, int extraLocals = 0)
    {
        var labels = Labels(code);
        var instructions = code.Select((line, offset) => new CilInstruction(offset, offset + 1, line.Operation,
            line.Operand switch
            {
                CilOperand.BranchTarget target => new CilOperand.BranchTarget(Resolve(target.Offset, labels)),
                { } operand => operand,
                null => new CilOperand.None(),
            }));
        var int32 = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        return new(new MethodDefinitionModel(new(ValidationTestData.Assembly, 0x06000001),
                new(ValidationTestData.Assembly, 0x02000001), "Run", true,
                new MethodSignatureModel(CliValueKind.I4,
                    [CliValueKind.I4, CliValueKind.I4, CliValueKind.I4, CliValueKind.ManagedReference]), 1),
            3, [CliValueKind.I4, CliValueKind.I4, CliValueKind.I4, CliValueKind.I4, CliValueKind.I4, CliValueKind.ManagedReference,
                .. Enumerable.Repeat(CliValueKind.I4, extraLocals)],
            [.. instructions])
        {
            LocalSignatureTypes = [int32, int32, int32, int32, int32, CliTypeIdentity.SzArray(int32),
                .. Enumerable.Repeat(int32, extraLocals)],
        };
    }

    private sealed record Line(CilOperation Operation, CilOperand? Operand = null)
    {
        public string? Label { get; init; }
    }


    private static ArrayLoopVersioner Versioner()
    {
        var (graphs, stacks) = Collaborators();
        return new(graphs, stacks, Planner(), ArrayLoopVersioner.LoopInstructionBudget);
    }

    private static ArrayLoopVersionPlanner Planner() => new(Induction());

    private static ArrayLoopInductionAnalyzer Induction()
    {
        var dominance = new ControlFlowDominanceAnalyzer(new DominatorTreeBuilder());
        return new(new ControlFlowNaturalLoopAnalyzer(dominance), dominance);
    }

    private static (IControlFlowGraphBuilder Graphs, ITypedStackValidator Stacks) Collaborators()
    {
        var program = new Program();
        return (new ControlFlowGraphBuilderFactory().Create(),
            new TypedStackValidatorFactory(new StackTypeCompatibilityValidator()).Create(program, program, program));
    }

    private static ValidatedControlFlowGraph Validate(CilMethodBody body)
    {
        var (graphs, stacks) = Collaborators();
        return stacks.Validate(graphs.Build(body));
    }

    private sealed class Program : ITypeRepository, IFieldRepository, IMethodRepository
    {
        public TypeDefinitionModel GetTypeDefinition(EntityKey key) => throw new NotSupportedException();

        public FieldDefinitionModel GetField(EntityKey key) => throw new NotSupportedException();

        public MethodDefinitionModel GetMethod(EntityKey key) => throw new NotSupportedException();
    }
}
