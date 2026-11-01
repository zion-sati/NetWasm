using System.Collections.Immutable;
using NetWasm.Compiler.Analysis.Inlining;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Analysis.Inlining;

public sealed class TerminalContinuationInliningPlannerTests
{
    private static readonly AssemblyIdentity Assembly = new("TerminalPlannerTests");
    private static readonly CliTypeIdentity Int32 =
        CliTypeIdentity.Primitive("i4", CliValueKind.I4);
    private static readonly CliTypeIdentity Void =
        CliTypeIdentity.FromStackKind(CliValueKind.Void);
    private static readonly CliTypeIdentity Owner =
        CliTypeIdentity.Named(Assembly, "", "Owner", false);

    [Fact]
    public void NonReturnCalleeInstructionsDoNotRequireReturnStackProof()
    {
        var (request, suffix) = Request();
        request = request with
        {
            Inlining = request.Inlining with
            {
                Body = request.Inlining.Body with
                {
                    Instructions = [new(0, 1, CilOperation.Nop, new CilOperand.None()),
                    new(1, 2, CilOperation.Return, new CilOperand.None())]
                },
                InstructionEntryStacks = ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty.Add(1, []),
            },
        };

        var result = new TerminalContinuationInliningPlanner().Plan(request);

        Assert.NotNull(result);
        Assert.Equal(suffix.ToArray(), result.Suffix.ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void NonVoidCallerRequiresExactlyOneValueOfItsReturnKind(int variant)
    {
        var (request, _) = Request();
        var stacks = request.InstructionEntryStacks.ToDictionary();
        stacks[3] = variant switch
        {
            0 => [],
            1 => [CliValueKind.F4],
            _ => [CliValueKind.I4, CliValueKind.I4],
        };

        Assert.Null(new TerminalContinuationInliningPlanner().Plan(request with { InstructionEntryStacks = stacks }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectsAConditionalEntryOnlyWhenItTargetsTheTerminalSuffix(bool targetsSuffix)
    {
        var (request, _) = Request();
        var instructions = ImmutableArray.Create(
            new CilInstruction(0, 1, CilOperation.LoadArgument, new CilOperand.Index(0)),
            new CilInstruction(1, 2, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(targetsSuffix ? 4 : 2)),
            new CilInstruction(2, 3, CilOperation.LoadArgument, new CilOperand.Index(0)),
            request.CallerBody.Instructions[1] with { Offset = 3, NextOffset = 4 },
            new CilInstruction(4, 5, CilOperation.LoadArgument, new CilOperand.Index(0)),
            new CilInstruction(5, 6, CilOperation.Return, new CilOperand.None()));
        request = request with
        {
            CallerBody = request.CallerBody with { Instructions = instructions },
            CallIndex = 3,
            InstructionEntryStacks = new Dictionary<int, ImmutableArray<CliValueKind>>
            { [3] = [CliValueKind.I4], [5] = [CliValueKind.I4] },
        };

        var result = new TerminalContinuationInliningPlanner().Plan(request);

        if (targetsSuffix) Assert.Null(result);
        else
        {
            Assert.NotNull(result);
            Assert.Equal(instructions[4..].ToArray(), result.Suffix.ToArray());
            Assert.Equal(5, result.ConsumedThroughIndex);
        }
    }

    [Fact]
    public void RejectsMissingCalleeReturnProof()
    {
        var (request, _) = Request();
        request = request with { Inlining = request.Inlining with { InstructionEntryStacks = [] } };
        Assert.Null(new TerminalContinuationInliningPlanner().Plan(request));
    }

    [Fact]
    public void RejectsMissingCallerReturnProof()
    {
        var (request, _) = Request();
        var stacks = request.InstructionEntryStacks.ToDictionary();
        stacks.Remove(3);
        Assert.Null(new TerminalContinuationInliningPlanner().Plan(request with { InstructionEntryStacks = stacks }));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void VoidCallerRequiresAnEmptyReturnStack(bool instanceMetadata, bool residual)
    {
        var (request, _) = Request();
        var method = Method("VoidCaller", MethodSignatureModel.Create(Void, Int32), 5);
        var instructions = request.CallerBody.Instructions.SetItem(2,
            new(2, 3, CilOperation.Nop, new CilOperand.None()));
        var body = Body(method.Definition, instructions) with { MethodInstance = instanceMetadata ? method : null };
        var stacks = request.InstructionEntryStacks.ToDictionary();
        stacks[3] = residual ? [CliValueKind.I4] : [];

        var result = new TerminalContinuationInliningPlanner().Plan(request with
        { CallerBody = body, InstructionEntryStacks = stacks });

        if (residual) Assert.Null(result);
        else
        {
            Assert.NotNull(result);
            Assert.Equal(instructions[2..].ToArray(), result.Suffix.ToArray());
        }
    }

    [Fact]
    public void SelectsUntargetedStraightLineTerminalSuffix()
    {
        var (request, suffix) = Request();

        var result = new TerminalContinuationInliningPlanner().Plan(request);

        Assert.NotNull(result);
        Assert.Equal(suffix.ToArray(), result.Suffix.ToArray());
        Assert.Equal(request.CallerBody.Instructions.Length - 1,
            result.ConsumedThroughIndex);
    }

    [Fact]
    public void RejectsResidualCallerStack()
    {
        var (request, _) = Request();
        var stacks = request.InstructionEntryStacks.ToDictionary();
        stacks[request.CallerBody.Instructions[1].Offset] =
            [CliValueKind.I4, CliValueKind.I4];

        Assert.Null(new TerminalContinuationInliningPlanner().Plan(
            request with { InstructionEntryStacks = stacks }));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void RejectsIncomingTargetToSuffix(int target)
    {
        var (request, _) = Request();
        var instructions = request.CallerBody.Instructions.SetItem(
            0,
            new(0, 1, CilOperation.Branch, new CilOperand.BranchTarget(target)));

        Assert.Null(new TerminalContinuationInliningPlanner().Plan(request with
        {
            CallerBody = request.CallerBody with { Instructions = instructions },
        }));
    }

    [Theory]
    [InlineData(CilOperation.Branch)]
    [InlineData(CilOperation.Throw)]
    [InlineData(CilOperation.LocalAllocate)]
    [InlineData(CilOperation.Constrained)]
    public void RejectsControlTransfersAndUnsupportedPrefixGroups(
        CilOperation operation)
    {
        var (request, _) = Request();
        CilOperand operand = operation switch
        {
            CilOperation.Branch => new CilOperand.BranchTarget(3),
            _ => new CilOperand.None(),
        };
        var instructions = request.CallerBody.Instructions.SetItem(
            2,
            new(2, 3, operation, operand));

        Assert.Null(new TerminalContinuationInliningPlanner().Plan(request with
        {
            CallerBody = request.CallerBody with { Instructions = instructions },
        }));
    }

    [Fact]
    public void RejectsNonVoidInstanceAndMissingStackProof()
    {
        var (request, _) = Request();
        var nonVoid = Method(
            "NonVoid",
            MethodSignatureModel.Create(Int32, Int32),
            3);
        Assert.Null(new TerminalContinuationInliningPlanner().Plan(request with
        {
            Inlining = new(nonVoid, request.Inlining.Body),
        }));

        var instance = Method(
            "Instance",
            MethodSignatureModel.Create(Void, Int32),
            4,
            isStatic: false);
        Assert.Null(new TerminalContinuationInliningPlanner().Plan(request with
        {
            Inlining = new(instance, request.Inlining.Body),
        }));

        Assert.Null(new TerminalContinuationInliningPlanner().Plan(request with
        {
            InstructionEntryStacks =
                ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty,
        }));
    }

    [Fact]
    public void RejectsCalleeReturnWithResidualStack()
    {
        var (request, _) = Request();
        var returnOffset = request.Inlining.Body.Instructions[^1].Offset;

        Assert.Null(new TerminalContinuationInliningPlanner().Plan(request with
        {
            Inlining = request.Inlining with
            {
                InstructionEntryStacks =
                    ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty
                        .Add(returnOffset, [CliValueKind.I4]),
            },
        }));
    }

    [Fact]
    public void RejectsInvalidInputs()
    {
        var planner = new TerminalContinuationInliningPlanner();
        var (request, _) = Request();

        Assert.Throws<ArgumentNullException>(() => planner.Plan(null!));
        Assert.Throws<ArgumentNullException>(() => planner.Plan(request with { CallerBody = null! }));
        Assert.Throws<ArgumentNullException>(() => planner.Plan(request with { Inlining = null! }));
        Assert.Throws<ArgumentNullException>(() => planner.Plan(request with { InstructionEntryStacks = null! }));
        Assert.Throws<ArgumentOutOfRangeException>(() => planner.Plan(request with { CallIndex = -1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => planner.Plan(
            request with { CallIndex = request.CallerBody.Instructions.Length }));
    }

    private static (TerminalContinuationInliningRequest Request,
        ImmutableArray<CilInstruction> Suffix) Request()
    {
        var caller = Method(
            "Caller",
            MethodSignatureModel.Create(Int32, Int32),
            1);
        var callee = Method(
            "Callee",
            MethodSignatureModel.Create(Void, Int32),
            2);
        var call = new CilInstruction(
            1,
            2,
            CilOperation.Call,
            new CilOperand.MethodInstance(callee));
        var body = Body(
            caller.Definition,
            [
                new(0, 1, CilOperation.LoadArgument, new CilOperand.Index(0)),
                call,
                new(2, 3, CilOperation.LoadArgument, new CilOperand.Index(0)),
                new(3, 4, CilOperation.Return, new CilOperand.None()),
            ]);
        var calleeBody = Body(
            callee.Definition,
            [new(0, 1, CilOperation.Return, new CilOperand.None())]);
        var stacks = new Dictionary<int, ImmutableArray<CliValueKind>>
        {
            [call.Offset] = [CliValueKind.I4],
            [3] = [CliValueKind.I4],
        };
        return (
            new(
                body,
                1,
                new ManagedInliningPlan(callee, calleeBody)
                {
                    InstructionEntryStacks =
                        ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty
                            .Add(0, ImmutableArray<CliValueKind>.Empty),
                },
                stacks),
            body.Instructions[2..]);
    }

    private static MethodInstanceModel Method(
        string name,
        MethodSignatureModel signature,
        int token,
        bool isStatic = true)
    {
        var definition = new MethodDefinitionModel(
            new(Assembly, 0x06000000 + token),
            new(Assembly, 0x02000001),
            name,
            isStatic,
            signature,
            1);
        return new(definition, Owner, [], signature);
    }

    private static CilMethodBody Body(
        MethodDefinitionModel method,
        ImmutableArray<CilInstruction> instructions) => new(
            method,
            2,
            [],
            instructions)
        {
            LocalSignatureTypes = [],
        };
}
