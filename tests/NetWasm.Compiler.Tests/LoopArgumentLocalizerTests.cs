// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Analysis.Loops;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

// An int or array argument that a loop reads, and that the method only reads, is copied into a local on entry and
// read from it; every other argument stays as it is.
public sealed class LoopArgumentLocalizerTests
{
    private static readonly AssemblyIdentity Assembly = new("fixture");
    private static readonly EntityKey Owner = new(Assembly, 0x02000001);
    private static readonly CliTypeIdentity Int32 = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
    private static readonly CliTypeIdentity Int64 = CliTypeIdentity.Primitive("i8", CliValueKind.I8);
    private static readonly CliTypeIdentity Byte = CliTypeIdentity.Primitive("u1", CliValueKind.I4);
    private static readonly CliTypeIdentity Ints = CliTypeIdentity.SzArray(Int32);
    private static readonly CliTypeIdentity Object = CliTypeIdentity.Named(Assembly, "System", "Object", isValueType: false);

    // static int Sum(int[] values, int count): for (index = 0; index < count; index++) sum += values[index];
    [Fact]
    public void ALoopsIntAndArrayArgumentsAreReadFromLocalsCopiedOnEntry()
    {
        var body = Body(isStatic: true, [Ints, Int32], [Int32, Int32], Sum(values: 0, count: 1));

        var rewritten = new LoopArgumentLocalizer(enabled: true).Rewrite(body);

        // Two new locals after the method's own, typed as the parameters.
        Assert.Equal([CliValueKind.I4, CliValueKind.I4, CliValueKind.ManagedReference, CliValueKind.I4], rewritten.Locals);
        Assert.Equal([Int32, Int32, Ints, Int32], rewritten.LocalSignatureTypes);
        // The copies run once on entry, in argument order, and nothing else reads an argument.
        Assert.Equal(
            [
                (CilOperation.LoadArgument, 0), (CilOperation.StoreLocal, 2),
                (CilOperation.LoadArgument, 1), (CilOperation.StoreLocal, 3),
            ],
            rewritten.Instructions.Take(4).Select(instruction => (instruction.Operation, ((CilOperand.Index)instruction.Operand).Value)));
        Assert.DoesNotContain(rewritten.Instructions.Skip(4), instruction => instruction.Operation == CilOperation.LoadArgument);
        Assert.Equal(body.Instructions.Length + 4, rewritten.Instructions.Length);
        // The loop reads the copies, and its back edge still lands on its body, after the copies.
        Assert.Contains(rewritten.Instructions, instruction => instruction is
        { Operation: CilOperation.LoadLocal, Operand: CilOperand.Index { Value: 2 } });
        Assert.Contains(rewritten.Instructions, instruction => instruction is
        { Operation: CilOperation.LoadLocal, Operand: CilOperand.Index { Value: 3 } });
        var before = (CilOperand.BranchTarget)body.Instructions
            .Single(instruction => instruction.Operation == CilOperation.BranchIfLessThanSigned).Operand;
        var after = (CilOperand.BranchTarget)rewritten.Instructions
            .Single(instruction => instruction.Operation == CilOperation.BranchIfLessThanSigned).Operand;
        Assert.Equal(before.Offset + 4, after.Offset);
    }

    // A loop that starts at the method's first instruction: the copies run once, before it.
    [Fact]
    public void ALoopAtTheFirstInstructionKeepsItsEntryCopyOutsideTheLoop()
    {
        // do { local 0 += count; } while (local 0 < count);
        var code = new CilBuilder().Label("start")
            .Local(CilOperation.LoadLocal, 0).Local(CilOperation.LoadArgument, 0).Op(CilOperation.Add).Local(CilOperation.StoreLocal, 0)
            .Local(CilOperation.LoadLocal, 0).Local(CilOperation.LoadArgument, 0)
            .Branch(CilOperation.BranchIfLessThanSigned, "start").Return().Build();
        var body = Body(isStatic: true, [Int32], [Int32], code);

        var rewritten = new LoopArgumentLocalizer(enabled: true).Rewrite(body);

        Assert.Equal(CilOperation.LoadArgument, rewritten.Instructions[0].Operation);
        Assert.Equal(CilOperation.StoreLocal, rewritten.Instructions[1].Operation);
        var back = rewritten.Instructions.Single(instruction => instruction.Operation == CilOperation.BranchIfLessThanSigned);
        Assert.Equal(rewritten.Instructions[2].Offset, ((CilOperand.BranchTarget)back.Operand).Offset);
        Assert.Equal(1, rewritten.Instructions.Count(instruction => instruction.Operation == CilOperation.LoadArgument));
    }

    // An instance method's first parameter is argument 1; the receiver stays an argument.
    [Fact]
    public void AnInstanceMethodsParametersFollowItsReceiver()
    {
        var code = new CilBuilder().Loop(0, loop => loop
            .Local(CilOperation.LoadArgument, 0).Op(CilOperation.Pop)
            .Local(CilOperation.LoadArgument, 1).Op(CilOperation.Pop)).Return().Build();
        var body = Body(isStatic: false, [Int32], [Int32], code);

        var rewritten = new LoopArgumentLocalizer(enabled: true).Rewrite(body);

        Assert.Equal([Int32, Int32], rewritten.LocalSignatureTypes);
        Assert.Equal((CilOperation.LoadArgument, 1),
            (rewritten.Instructions[0].Operation, ((CilOperand.Index)rewritten.Instructions[0].Operand).Value));
        // The receiver is still read as an argument inside the loop.
        Assert.Contains(rewritten.Instructions.Skip(2), instruction => instruction is
        { Operation: CilOperation.LoadArgument, Operand: CilOperand.Index { Value: 0 } });
    }

    public static TheoryData<string> UntouchedShapes() =>
    [
        "read only outside the loop", "stored by the method", "address taken", "a long", "a byte", "an object",
        "no loop", "a forward branch only",
    ];

    [Theory]
    [MemberData(nameof(UntouchedShapes))]
    public void AnArgumentTheRuleDoesNotCoverStaysAnArgument(string shape)
    {
        var body = shape switch
        {
            "read only outside the loop" => Body(true, [Int32], [Int32], new CilBuilder()
                .Local(CilOperation.LoadArgument, 0).Op(CilOperation.Pop)
                .Loop(0, loop => loop.Op(CilOperation.Nop)).Return().Build()),
            "stored by the method" => Body(true, [Int32], [Int32], new CilBuilder()
                .Op(CilOperation.LoadInt32, 5).Local(CilOperation.StoreArgument, 0)
                .Loop(0, loop => loop.Local(CilOperation.LoadArgument, 0).Op(CilOperation.Pop)).Return().Build()),
            "address taken" => Body(true, [Int32], [Int32], new CilBuilder()
                .Local(CilOperation.LoadArgumentAddress, 0).Op(CilOperation.Pop)
                .Loop(0, loop => loop.Local(CilOperation.LoadArgument, 0).Op(CilOperation.Pop)).Return().Build()),
            "a long" => Body(true, [Int64], [Int32], new CilBuilder()
                .Loop(0, loop => loop.Local(CilOperation.LoadArgument, 0).Op(CilOperation.Pop)).Return().Build()),
            // A narrow parameter's local would narrow what is stored in it.
            "a byte" => Body(true, [Byte], [Int32], new CilBuilder()
                .Loop(0, loop => loop.Local(CilOperation.LoadArgument, 0).Op(CilOperation.Pop)).Return().Build()),
            "an object" => Body(true, [Object], [Int32], new CilBuilder()
                .Loop(0, loop => loop.Local(CilOperation.LoadArgument, 0).Op(CilOperation.Pop)).Return().Build()),
            "no loop" => Body(true, [Int32], [Int32], new CilBuilder()
                .Local(CilOperation.LoadArgument, 0).Op(CilOperation.Pop).Return().Build()),
            _ => Body(true, [Int32], [Int32], new CilBuilder()
                .Local(CilOperation.LoadArgument, 0).Op(CilOperation.LoadInt32, 0).Branch(CilOperation.BranchIfLessThanSigned, "end")
                .Local(CilOperation.LoadArgument, 0).Op(CilOperation.Pop).Label("end").Return().Build()),
        };

        Assert.Same(body, new LoopArgumentLocalizer(enabled: true).Rewrite(body));
    }

    // One argument qualifies and one does not: only the first moves.
    [Fact]
    public void OnlyTheArgumentsTheRuleCoversMove()
    {
        var body = Body(true, [Int32, Int32], [Int32], new CilBuilder()
            .Op(CilOperation.LoadInt32, 5).Local(CilOperation.StoreArgument, 1)
            .Loop(0, loop => loop
                .Local(CilOperation.LoadArgument, 0).Op(CilOperation.Pop)
                .Local(CilOperation.LoadArgument, 1).Op(CilOperation.Pop)).Return().Build());

        var rewritten = new LoopArgumentLocalizer(enabled: true).Rewrite(body);

        Assert.Equal([Int32, Int32], rewritten.LocalSignatureTypes);
        Assert.Equal(body.Instructions.Length + 2, rewritten.Instructions.Length);
        Assert.Contains(rewritten.Instructions, instruction => instruction is
        { Operation: CilOperation.LoadArgument, Operand: CilOperand.Index { Value: 1 } });
        Assert.Equal(1, rewritten.Instructions.Count(instruction => instruction is
        { Operation: CilOperation.LoadArgument, Operand: CilOperand.Index { Value: 0 } }));
    }

    // A body with no stack of its own still has room for the copy.
    [Fact]
    public void TheCopyHasAStackSlot()
    {
        var body = Body(true, [Int32], [Int32], new CilBuilder()
            .Loop(0, loop => loop.Local(CilOperation.LoadArgument, 0).Op(CilOperation.Pop)).Return().Build()) with
        { MaxStack = 0 };

        Assert.Equal(1, new LoopArgumentLocalizer(enabled: true).Rewrite(body).MaxStack);
    }

    [Fact]
    public void DisabledItChangesNothingAndItRejectsNoBody()
    {
        var body = Body(true, [Ints, Int32], [Int32, Int32], Sum(values: 0, count: 1));

        Assert.Same(body, new LoopArgumentLocalizer(enabled: false).Rewrite(body));
        Assert.Throws<ArgumentNullException>(() => new LoopArgumentLocalizer(enabled: true).Rewrite(null!));
    }

    // sum in local 1, the index in local 0.
    private static ImmutableArray<CilInstruction> Sum(int values, int count) => new CilBuilder()
        .Op(CilOperation.LoadInt32, 0).Local(CilOperation.StoreLocal, 1)
        .Op(CilOperation.LoadInt32, 0).Local(CilOperation.StoreLocal, 0).Branch(CilOperation.Branch, "test")
        .Label("start")
        .Local(CilOperation.LoadLocal, 1).Local(CilOperation.LoadArgument, values).Local(CilOperation.LoadLocal, 0)
        .Op(CilOperation.LoadArrayElement, new CilOperand.TypeIdentity(Int32)).Op(CilOperation.Add).Local(CilOperation.StoreLocal, 1)
        .Local(CilOperation.LoadLocal, 0).Op(CilOperation.LoadInt32, 1).Op(CilOperation.Add).Local(CilOperation.StoreLocal, 0)
        .Label("test").Local(CilOperation.LoadLocal, 0).Local(CilOperation.LoadArgument, count)
        .Branch(CilOperation.BranchIfLessThanSigned, "start")
        .Local(CilOperation.LoadLocal, 1).Return().Build();

    private static CilMethodBody Body(bool isStatic, ImmutableArray<CliTypeIdentity> parameters,
        ImmutableArray<CliTypeIdentity> locals, ImmutableArray<CilInstruction> code)
    {
        var method = new MethodDefinitionModel(new(Assembly, 0x06000001), Owner, "Run", isStatic,
            new MethodSignatureModel(Int32, parameters), 1);
        return new(method, 4, [.. locals.Select(local => local.StackKind)], code) { LocalSignatureTypes = locals };
    }
}
