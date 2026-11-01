using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests;

public sealed class ClosedTypeIsValueTypeCallRewriterTests
{
    private static readonly AssemblyIdentity Assembly = new("CoreLib");
    private static readonly EntityKey CallerType = Key(0x02000001);
    private static readonly EntityKey TypeType = Key(0x02000002);
    private static readonly EntityKey OtherType = Key(0x02000003);
    private static readonly MethodDefinitionModel Caller = new(
        Key(0x06000001),
        CallerType,
        "Run",
        IsStatic: true,
        MethodSignatureModel.Create(CliValueKind.Void),
        1);

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public void FoldsClosedTypeFacts(bool isValueType, int expected)
    {
        var body = Body(Type(isValueType));

        var rewritten = Rewriter().Rewrite(body);

        Assert.NotSame(body, rewritten);
        Assert.Equal(CilOperation.Nop, rewritten.Instructions[0].Operation);
        Assert.IsType<CilOperand.None>(rewritten.Instructions[0].Operand);
        Assert.Equal(CilOperation.Nop, rewritten.Instructions[1].Operation);
        Assert.IsType<CilOperand.None>(rewritten.Instructions[1].Operand);
        Assert.Equal(CilOperation.LoadInt32, rewritten.Instructions[2].Operation);
        Assert.Equal(
            expected,
            Assert.IsType<CilOperand.ConstantI4>(rewritten.Instructions[2].Operand).Value);
    }

    [Fact]
    public void FoldsVirtualGetterCall()
    {
        var body = Body(Type(isValueType: true), callOperation: CilOperation.CallVirtual);

        var rewritten = Rewriter().Rewrite(body);

        Assert.Equal(CilOperation.LoadInt32, rewritten.Instructions[2].Operation);
    }

    [Fact]
    public void RejectsNullBody()
    {
        Assert.Throws<ArgumentNullException>(() => Rewriter().Rewrite(null!));
    }

    [Fact]
    public void LeavesShortBodyUnchanged()
    {
        var body = new CilMethodBody(Caller, 0, [], []);

        Assert.Same(body, Rewriter().Rewrite(body));
    }

    [Fact]
    public void LeavesOpenTypeUnchanged()
    {
        var body = Body(CliTypeIdentity.GenericParameter(method: false, 0));

        Assert.Same(body, Rewriter().Rewrite(body));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void LeavesMalformedInstructionSequenceUnchanged(int malformedPart)
    {
        var instructions = Body(Type(isValueType: true)).Instructions.ToArray();
        instructions[0] = malformedPart switch
        {
            0 => instructions[0] with { Operation = CilOperation.Nop },
            1 => instructions[0] with { Operand = new CilOperand.None() },
            4 => instructions[0] with { NextOffset = 99 },
            _ => instructions[0],
        };
        instructions[1] = malformedPart switch
        {
            2 => instructions[1] with { Operation = CilOperation.Nop },
            5 => instructions[1] with { NextOffset = 99 },
            _ => instructions[1],
        };
        instructions[2] = malformedPart == 3
            ? instructions[2] with { Operation = CilOperation.Nop }
            : instructions[2];
        var body = Body([.. instructions]);

        Assert.Same(body, Rewriter().Rewrite(body));
    }

    [Fact]
    public void LeavesBranchEntryIntoSequenceUnchanged()
    {
        var instructions = Body(Type(isValueType: true)).Instructions.Add(
            new(3, 4, CilOperation.Branch, new CilOperand.BranchTarget(2)));
        var body = Body(instructions);

        Assert.Same(body, Rewriter().Rewrite(body));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LeavesExceptionRegionEntryIntoSequenceUnchanged(bool filterEntry)
    {
        var body = Body(Type(isValueType: true)) with
        {
            ExceptionRegions =
            [
                new(
                    CilExceptionRegionKind.Catch,
                    TryOffset: 10,
                    TryLength: 1,
                    HandlerOffset: 20,
                    HandlerLength: 1,
                    CatchType: null,
                    FilterOffset: filterEntry ? 1 : null),
            ],
        };
        if (!filterEntry)
        {
            body = body with
            {
                ExceptionRegions =
                [
                    body.ExceptionRegions[0] with
                    {
                        TryOffset = 1,
                    },
                ],
            };
        }

        Assert.Same(body, Rewriter().Rewrite(body));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void LeavesNonGetterTargetUnchanged(int malformedPart)
    {
        var target = Getter() with
        {
            Definition = malformedPart switch
            {
                1 => Getter().Definition with { IsStatic = true },
                2 => Getter().Definition with { Name = "get_IsEnum" },
                3 => Getter().Definition with { DeclaringType = OtherType },
                _ => Getter().Definition,
            },
            Signature = malformedPart switch
            {
                4 => MethodSignatureModel.Create(CliValueKind.I8),
                5 => MethodSignatureModel.Create(CliValueKind.I4, CliValueKind.I4),
                _ => Getter().Signature,
            },
        };
        var body = Body(Type(isValueType: true));
        var rewriter = new ClosedTypeIsValueTypeCallRewriter(
            new FixedMethodResolver(malformedPart == 0 ? null : target),
            new Symbols());

        Assert.Same(body, rewriter.Rewrite(body));
    }

    private static ClosedTypeIsValueTypeCallRewriter Rewriter() => new(
        new FixedMethodResolver(Getter()),
        new Symbols());

    private static CilMethodBody Body(CliTypeIdentity type, CilOperation callOperation = CilOperation.Call) =>
        Body(
        [
            new(0, 1, CilOperation.LoadTypeToken, new CilOperand.TypeIdentity(type)),
            new(1, 2, CilOperation.MaterializeType, new CilOperand.None()),
            new(2, 3, callOperation, new CilOperand.None()),
        ]);

    private static CilMethodBody Body(ImmutableArray<CilInstruction> instructions) =>
        new(Caller, 1, [], instructions);

    private static CliTypeIdentity Type(bool isValueType) => CliTypeIdentity.Named(
        Assembly,
        "Fixtures",
        isValueType ? "Value" : "Reference",
        isValueType);

    private static MethodInstanceModel Getter()
    {
        var signature = MethodSignatureModel.Create(CliValueKind.I4);
        var definition = new MethodDefinitionModel(
            Key(0x06000002),
            TypeType,
            "get_IsValueType",
            IsStatic: false,
            signature,
            1);
        return new(
            definition,
            CliTypeIdentity.Named(Assembly, "System", "Type", isValueType: false),
            [],
            signature);
    }

    private static EntityKey Key(int token) => new(Assembly, token);

    private sealed class FixedMethodResolver(MethodInstanceModel? method) :
        ICalledMethodResolver
    {
        public MethodInstanceModel? Resolve(CilInstruction instruction) => method;
    }

    private sealed class Symbols : ISymbolFormatter
    {
        public string Format(EntityKey key) => key == TypeType
            ? "System.Type"
            : key == OtherType
                ? "Fixtures.Other"
                : "Fixtures.Caller";

        public string Format(MethodDefinitionModel method) =>
            $"{Format(method.DeclaringType)}::{method.Name}";
    }
}
