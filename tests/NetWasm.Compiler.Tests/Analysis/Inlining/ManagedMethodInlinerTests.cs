using System.Collections.Immutable;
using NetWasm.Compiler.Analysis.Inlining;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Analysis.Inlining;

public sealed class ManagedMethodInlinerTests
{
    private static readonly AssemblyIdentity Assembly = new("InlinerTests");
    private static readonly CliTypeIdentity Int32 =
        CliTypeIdentity.Primitive("i4", CliValueKind.I4);
    private static readonly CliTypeIdentity Void =
        CliTypeIdentity.FromStackKind(CliValueKind.Void);
    private static readonly CliTypeIdentity Int32Reference =
        CliTypeIdentity.ManagedByReference(Int32);
    private static readonly CliTypeIdentity Owner =
        CliTypeIdentity.Named(Assembly, "", "Owner", false);
    private static readonly CliTypeIdentity Value =
        CliTypeIdentity.Named(Assembly, "", "Value", true);
    private static readonly CliTypeIdentity ValueReference =
        CliTypeIdentity.ManagedByReference(Value);

    [Fact]
    public void InlinesArgumentsAndLocalsInEvaluationOrder()
    {
        var caller = Method("Caller", MethodSignatureModel.Create(CliValueKind.Void), 1);
        var callee = Method(
            "Leaf",
            MethodSignatureModel.Create(
                Void,
                Int32Reference,
                Int32),
            2);
        var calleeBody = Body(
            callee.Definition,
            [
                new(0, 1, CilOperation.LoadArgument, new CilOperand.Index(1)),
                new(1, 2, CilOperation.StoreLocal, new CilOperand.Index(0)),
                new(2, 3, CilOperation.LoadArgument, new CilOperand.Index(0)),
                new(3, 4, CilOperation.LoadLocal, new CilOperand.Index(0)),
                new(4, 5, CilOperation.StoreObject, new CilOperand.TypeIdentity(Int32)),
                new(5, 6, CilOperation.Return, new CilOperand.None()),
            ],
            [Int32]);
        var call = new CilInstruction(
            2,
            3,
            CilOperation.Call,
            new CilOperand.MethodInstance(callee));
        var callerBody = Body(
            caller.Definition,
            [
                new(0, 1, CilOperation.LoadArgumentAddress, new CilOperand.Index(0)),
                new(1, 2, CilOperation.LoadInt32, new CilOperand.ConstantI4(7)),
                call,
                new(3, 4, CilOperation.Return, new CilOperand.None()),
            ]);

        var result = Inline(Create(call, callee, calleeBody), caller, callerBody);

        Assert.Equal(
            [
                CilOperation.LoadArgumentAddress,
                CilOperation.LoadInt32,
                CilOperation.StoreLocal,
                CilOperation.StoreLocal,
                CilOperation.LoadLocal,
                CilOperation.StoreLocal,
                CilOperation.LoadLocal,
                CilOperation.LoadLocal,
                CilOperation.StoreObject,
                CilOperation.Return,
            ],
            result.Instructions.Select(static instruction => instruction.Operation));
        Assert.Equal([1, 0, 1, 2, 0, 2], result.Instructions
            .Where(static instruction => instruction.Operand is CilOperand.Index)
            .Skip(1)
            .Select(static instruction =>
                Assert.IsType<CilOperand.Index>(instruction.Operand).Value));
        Assert.Equal(
            new[] { CliValueKind.ManagedAddress, CliValueKind.I4, CliValueKind.I4 },
            result.Locals.ToArray());
        Assert.Equal(
            new[] { Int32Reference, Int32, Int32 },
            result.LocalSignatureTypes.ToArray());
        Assert.Equal(callerBody.MaxStack + calleeBody.MaxStack, result.MaxStack);
        Assert.All(result.Instructions[2..^1], instruction =>
            Assert.Equal(call.SourceOffset, instruction.SourceOffset));
    }

    [Fact]
    public void CapturesInstanceReceiverBeforeExplicitArguments()
    {
        var caller = Method("Caller", MethodSignatureModel.Create(Void), 1);
        var callee = Method(
            "InstanceLeaf",
            MethodSignatureModel.Create(Void, Int32),
            2,
            isStatic: false);
        var calleeBody = Body(
            callee.Definition,
            [
                new(0, 1, CilOperation.LoadArgument, new CilOperand.Index(0)),
                new(1, 2, CilOperation.Pop, new CilOperand.None()),
                new(2, 3, CilOperation.LoadArgument, new CilOperand.Index(1)),
                new(3, 4, CilOperation.Pop, new CilOperand.None()),
                new(4, 5, CilOperation.Return, new CilOperand.None()),
            ]);
        var call = new CilInstruction(
            2,
            3,
            CilOperation.CallVirtual,
            new CilOperand.MethodInstance(callee));
        var callerBody = Body(
            caller.Definition,
            [
                new(0, 1, CilOperation.LoadNull, new CilOperand.None()),
                new(1, 2, CilOperation.LoadInt32, new CilOperand.ConstantI4(7)),
                call,
                new(3, 4, CilOperation.Return, new CilOperand.None()),
            ]);

        var result = Inline(Create(call, callee, calleeBody), caller, callerBody);

        Assert.Equal(
            [
                CilOperation.LoadNull,
                CilOperation.LoadInt32,
                CilOperation.StoreLocal,
                CilOperation.StoreLocal,
                CilOperation.LoadLocal,
                CilOperation.Pop,
                CilOperation.LoadLocal,
                CilOperation.Pop,
                CilOperation.Return,
            ],
            result.Instructions.Select(static instruction => instruction.Operation));
        Assert.Equal(
            [1, 0, 0, 1],
            result.Instructions
                .Where(static instruction => instruction.Operand is CilOperand.Index)
                .Select(static instruction =>
                    Assert.IsType<CilOperand.Index>(instruction.Operand).Value));
        Assert.Equal(
            new[] { CliValueKind.ManagedReference, CliValueKind.I4 },
            result.Locals.ToArray());
        Assert.Equal(new[] { Owner, Int32 }, result.LocalSignatureTypes.ToArray());
    }

    [Theory]
    [InlineData(CilOperation.LoadArgument, CilOperation.LoadLocal, 0)]
    [InlineData(CilOperation.LoadArgumentAddress, CilOperation.LoadLocalAddress, 0)]
    [InlineData(CilOperation.StoreArgument, CilOperation.StoreLocal, 0)]
    [InlineData(CilOperation.LoadLocal, CilOperation.LoadLocal, 1)]
    [InlineData(CilOperation.LoadLocalAddress, CilOperation.LoadLocalAddress, 1)]
    [InlineData(CilOperation.StoreLocal, CilOperation.StoreLocal, 1)]
    public void RewritesEveryArgumentAndLocalOperation(
        CilOperation source,
        CilOperation expected,
        int expectedIndex)
    {
        var caller = Method("Caller", MethodSignatureModel.Create(CliValueKind.Void), 1);
        var callee = Method(
            "Leaf",
            MethodSignatureModel.Create(Void, Int32),
            2);
        var calleeBody = Body(
            callee.Definition,
            [
                new(0, 1, source, new CilOperand.Index(0)),
                new(1, 2, CilOperation.Return, new CilOperand.None()),
            ],
            [Int32]);
        var call = new CilInstruction(
            0,
            1,
            CilOperation.Call,
            new CilOperand.MethodInstance(callee));
        var callerBody = Body(
            caller.Definition,
            [call, new(1, 2, CilOperation.Return, new CilOperand.None())]);

        var result = Inline(Create(call, callee, calleeBody), caller, callerBody);

        Assert.Equal(expected, result.Instructions[1].Operation);
        Assert.Equal(
            expectedIndex,
            Assert.IsType<CilOperand.Index>(result.Instructions[1].Operand).Value);
    }

    [Fact]
    public void RemapsCallerBranchesSwitchesAndTerminalBoundary()
    {
        var caller = Method("Caller", MethodSignatureModel.Create(CliValueKind.Void), 1);
        var callee = Method(
            "Leaf",
            MethodSignatureModel.Create(Void, Int32),
            2);
        var calleeBody = Body(
            callee.Definition,
            [
                new(0, 1, CilOperation.Nop, new CilOperand.None()),
                new(1, 2, CilOperation.Nop, new CilOperand.None()),
                new(2, 3, CilOperation.Return, new CilOperand.None()),
            ]);
        var call = new CilInstruction(
            2,
            3,
            CilOperation.Call,
            new CilOperand.MethodInstance(callee));
        var callerBody = Body(
            caller.Definition,
            [
                new(0, 1, CilOperation.Branch, new CilOperand.BranchTarget(2)),
                new(1, 2, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
                call,
                new(3, 4, CilOperation.Switch, new CilOperand.SwitchTargets([2, 5])),
                new(4, 5, CilOperation.Return, new CilOperand.None()),
            ]);

        var result = Inline(Create(call, callee, calleeBody), caller, callerBody);

        Assert.Equal(2, Assert.IsType<CilOperand.BranchTarget>(
            result.Instructions[0].Operand).Offset);
        var switchTargets = Assert.IsType<CilOperand.SwitchTargets>(
            result.Instructions[5].Operand).Offsets;
        Assert.Equal(2, switchTargets[0]);
        Assert.Equal(7, switchTargets[1]);
        Assert.Equal(
            Enumerable.Range(0, result.Instructions.Length),
            result.Instructions.Select(static instruction => instruction.Offset));
        Assert.Equal(
            Enumerable.Range(1, result.Instructions.Length),
            result.Instructions.Select(static instruction => instruction.NextOffset));
    }

    [Fact]
    public void RemapsCalleeBranchesSwitchesAndMultipleReturnsToContinuation()
    {
        var caller = Method("Caller", MethodSignatureModel.Create(CliValueKind.Void), 1);
        var callee = Method(
            "BranchingLeaf",
            MethodSignatureModel.Create(Int32, Int32),
            2);
        var calleeBody = Body(
            callee.Definition,
            [
                new(0, 1, CilOperation.LoadArgument, new CilOperand.Index(0)),
                new(1, 2, CilOperation.Switch, new CilOperand.SwitchTargets([4, 6])),
                new(2, 3, CilOperation.LoadInt32, new CilOperand.ConstantI4(10)),
                new(3, 4, CilOperation.Return, new CilOperand.None()),
                new(4, 5, CilOperation.LoadInt32, new CilOperand.ConstantI4(20)),
                new(5, 6, CilOperation.Return, new CilOperand.None()),
                new(6, 7, CilOperation.LoadInt32, new CilOperand.ConstantI4(30)),
                new(7, 8, CilOperation.Return, new CilOperand.None()),
            ]);
        var call = new CilInstruction(
            1,
            2,
            CilOperation.Call,
            new CilOperand.MethodInstance(callee));
        var callerBody = Body(
            caller.Definition,
            [
                new(0, 1, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
                call,
                new(2, 3, CilOperation.StoreLocal, new CilOperand.Index(0)),
                new(3, 4, CilOperation.Return, new CilOperand.None()),
            ],
            [Int32]);

        var result = Inline(Create(call, callee, calleeBody), caller, callerBody);

        Assert.Equal(
            [
                CilOperation.LoadInt32,
                CilOperation.StoreLocal,
                CilOperation.LoadLocal,
                CilOperation.Switch,
                CilOperation.LoadInt32,
                CilOperation.Branch,
                CilOperation.LoadInt32,
                CilOperation.Branch,
                CilOperation.LoadInt32,
                CilOperation.StoreLocal,
                CilOperation.Return,
            ],
            result.Instructions.Select(static instruction => instruction.Operation));
        Assert.Equal(
            [6, 8],
            Assert.IsType<CilOperand.SwitchTargets>(result.Instructions[3].Operand)
                .Offsets.ToArray());
        Assert.Equal(9, Assert.IsType<CilOperand.BranchTarget>(
            result.Instructions[5].Operand).Offset);
        Assert.Equal(9, Assert.IsType<CilOperand.BranchTarget>(
            result.Instructions[7].Operand).Offset);
        Assert.All(result.Instructions[1..9], instruction =>
            Assert.Equal(call.SourceOffset, instruction.SourceOffset));
    }

    [Fact]
    public void DuplicatesTerminalCallerSuffixAtVoidCalleeReturns()
    {
        var caller = Method(
            "Caller",
            MethodSignatureModel.Create(Int32, Int32),
            1);
        var callee = Method(
            "BranchingLeaf",
            MethodSignatureModel.Create(Void, Int32),
            2);
        var calleeBody = Body(
            callee.Definition,
            [
                new(0, 1, CilOperation.LoadArgument, new CilOperand.Index(0)),
                new(1, 2, CilOperation.BranchIfFalse, new CilOperand.BranchTarget(4)),
                new(2, 3, CilOperation.Nop, new CilOperand.None()),
                new(3, 4, CilOperation.Return, new CilOperand.None()),
                new(4, 5, CilOperation.Nop, new CilOperand.None()),
                new(5, 6, CilOperation.Return, new CilOperand.None()),
            ]);
        var call = new CilInstruction(
            1,
            2,
            CilOperation.Call,
            new CilOperand.MethodInstance(callee));
        var callerBody = Body(
            caller.Definition,
            [
                new(0, 1, CilOperation.LoadArgument, new CilOperand.Index(0)),
                call,
                new(2, 3, CilOperation.LoadArgument, new CilOperand.Index(0)),
                new(3, 4, CilOperation.Return, new CilOperand.None()),
            ]);
        var entryStacks = new Dictionary<int, ImmutableArray<CliValueKind>>
        {
            [call.Offset] = [CliValueKind.I4],
            [3] = [CliValueKind.I4],
        };

        var result = Create(call, callee, calleeBody).Inline(
            caller,
            callerBody,
            entryStacks);

        Assert.Equal(
            [
                CilOperation.LoadArgument,
                CilOperation.StoreLocal,
                CilOperation.LoadLocal,
                CilOperation.BranchIfFalse,
                CilOperation.Nop,
                CilOperation.LoadArgument,
                CilOperation.Return,
                CilOperation.Nop,
                CilOperation.LoadArgument,
                CilOperation.Return,
            ],
            result.Instructions.Select(static instruction => instruction.Operation));
        Assert.Equal(7, Assert.IsType<CilOperand.BranchTarget>(
            result.Instructions[3].Operand).Offset);
        Assert.Equal(
            [0, 0],
            result.Instructions
                .Where(static instruction =>
                    instruction.Operation == CilOperation.LoadArgument)
                .Skip(1)
                .Select(static instruction =>
                    Assert.IsType<CilOperand.Index>(instruction.Operand).Value));
        Assert.Equal(2, result.Instructions.Count(static instruction =>
            instruction.Operation == CilOperation.Return));
    }

    [Fact]
    public void RemapsBranchesAndSwitchesTargetingDistinctCalleeReturns()
    {
        var caller = Method(
            "Caller",
            MethodSignatureModel.Create(Int32, Int32),
            1);
        var callee = Method(
            "BranchingLeaf",
            MethodSignatureModel.Create(Void, Int32),
            2);
        var calleeBody = Body(
            callee.Definition,
            [
                new(0, 1, CilOperation.LoadArgument, new CilOperand.Index(0)),
                new(1, 2, CilOperation.Switch, new CilOperand.SwitchTargets([3, 5])),
                new(2, 3, CilOperation.Branch, new CilOperand.BranchTarget(5)),
                new(3, 4, CilOperation.Return, new CilOperand.None()),
                new(4, 5, CilOperation.Branch, new CilOperand.BranchTarget(3)),
                new(5, 6, CilOperation.Return, new CilOperand.None()),
            ]);
        var call = new CilInstruction(
            1,
            2,
            CilOperation.Call,
            new CilOperand.MethodInstance(callee));
        var callerBody = Body(
            caller.Definition,
            [
                new(0, 1, CilOperation.LoadArgument, new CilOperand.Index(0)),
                call,
                new(2, 3, CilOperation.LoadArgument, new CilOperand.Index(0)),
                new(3, 4, CilOperation.Return, new CilOperand.None()),
            ]);
        var entryStacks = new Dictionary<int, ImmutableArray<CliValueKind>>
        {
            [call.Offset] = [CliValueKind.I4],
            [3] = [CliValueKind.I4],
        };

        var result = Create(call, callee, calleeBody).Inline(
            caller,
            callerBody,
            entryStacks);

        Assert.Equal(
            [5, 8],
            Assert.IsType<CilOperand.SwitchTargets>(result.Instructions[3].Operand)
                .Offsets.ToArray());
        Assert.Equal(8, Assert.IsType<CilOperand.BranchTarget>(
            result.Instructions[4].Operand).Offset);
        Assert.Equal(5, Assert.IsType<CilOperand.BranchTarget>(
            result.Instructions[7].Operand).Offset);
        Assert.Equal(CilOperation.LoadArgument, result.Instructions[5].Operation);
        Assert.Equal(CilOperation.Return, result.Instructions[6].Operation);
        Assert.Equal(CilOperation.LoadArgument, result.Instructions[8].Operation);
        Assert.Equal(CilOperation.Return, result.Instructions[9].Operation);
    }

    [Fact]
    public void FreezesOriginalCallSelectionBeforeTerminalSuffixCloning()
    {
        var caller = Method(
            "Caller",
            MethodSignatureModel.Create(Void, Int32),
            1);
        var first = Method("First", MethodSignatureModel.Create(Void), 2);
        var terminal = Method(
            "Terminal",
            MethodSignatureModel.Create(Void, Int32),
            3);
        var suffix = Method("Suffix", MethodSignatureModel.Create(Void), 4);
        var firstCall = new CilInstruction(
            0,
            1,
            CilOperation.Call,
            new CilOperand.MethodInstance(first));
        var terminalCall = new CilInstruction(
            2,
            3,
            CilOperation.Call,
            new CilOperand.MethodInstance(terminal));
        var suffixCall = new CilInstruction(
            3,
            4,
            CilOperation.Call,
            new CilOperand.MethodInstance(suffix));
        var callerBody = Body(
            caller.Definition,
            [
                firstCall,
                new(1, 2, CilOperation.LoadArgument, new CilOperand.Index(0)),
                terminalCall,
                suffixCall,
                new(4, 5, CilOperation.Return, new CilOperand.None()),
            ]);
        var firstBody = Body(
            first.Definition,
            [
                new(0, 1, CilOperation.Nop, new CilOperand.None()),
                new(1, 2, CilOperation.Return, new CilOperand.None()),
            ]);
        var terminalBody = Body(
            terminal.Definition,
            [
                new(0, 1, CilOperation.LoadArgument, new CilOperand.Index(0)),
                new(1, 2, CilOperation.BranchIfFalse, new CilOperand.BranchTarget(3)),
                new(2, 3, CilOperation.Return, new CilOperand.None()),
                new(3, 4, CilOperation.Return, new CilOperand.None()),
            ]);
        var resolver = new MultipleResolver(new Dictionary<int, ManagedInliningPlan>
        {
            [firstCall.Offset] = Plan(first, firstBody),
            [terminalCall.Offset] = Plan(terminal, terminalBody),
        });
        var entryStacks = new Dictionary<int, ImmutableArray<CliValueKind>>
        {
            [terminalCall.Offset] = [CliValueKind.I4],
            [4] = [],
        };

        var result = new ManagedMethodInliner(
            resolver,
            new TerminalContinuationInliningPlanner()).Inline(
                caller,
                callerBody,
                entryStacks);

        Assert.DoesNotContain(suffixCall.Offset, resolver.RequestedOffsets);
        var clonedCalls = result.Instructions.Where(instruction =>
            instruction.Operation == CilOperation.Call &&
            instruction.SourceOffset == suffixCall.SourceOffset).ToArray();
        Assert.Equal(2, clonedCalls.Length);
        Assert.Equal(2, clonedCalls.Select(static instruction => instruction.Offset)
            .Distinct().Count());
    }

    // The callee reads its receiver, then its argument.
    private static readonly ImmutableArray<CilInstruction> ValueLeaf =
    [
        new(0, 1, CilOperation.LoadArgument, new CilOperand.Index(0)),
        new(1, 2, CilOperation.Pop, new CilOperand.None()),
        new(2, 3, CilOperation.LoadArgument, new CilOperand.Index(1)),
        new(3, 4, CilOperation.Pop, new CilOperand.None()),
        new(4, 5, CilOperation.Return, new CilOperand.None()),
    ];

    [Fact]
    public void ValueTypeBodiesReadALocalReceiverDirectly()
    {
        var caller = Method("Caller", MethodSignatureModel.Create(Void), 1);
        var callee = Method("Leaf", MethodSignatureModel.Create(Void, Int32), 2, isStatic: false, declaringType: Value);
        var call = new CilInstruction(2, 3, CilOperation.Call, new CilOperand.MethodInstance(callee));
        var callerBody = Body(caller.Definition,
        [
            new(0, 1, CilOperation.LoadLocalAddress, new CilOperand.Index(0)),
            new(1, 2, CilOperation.LoadInt32, new CilOperand.ConstantI4(7)),
            call,
            new(3, 4, CilOperation.Return, new CilOperand.None()),
        ], [Value]);

        var result = Create(call, callee, Body(callee.Definition, ValueLeaf)).Inline(caller, callerBody,
            Stacks([], [CliValueKind.ManagedAddress], [CliValueKind.ManagedAddress, CliValueKind.I4], []));

        Assert.Equal(
            [
                CilOperation.Nop, CilOperation.LoadInt32, CilOperation.StoreLocal,
                CilOperation.LoadLocalAddress, CilOperation.Pop, CilOperation.LoadLocal, CilOperation.Pop,
                CilOperation.Return,
            ],
            result.Instructions.Select(static instruction => instruction.Operation));
        Assert.Equal(new CilOperand.Index(0), result.Instructions[3].Operand);
        Assert.Equal(new CilOperand.Index(2), result.Instructions[2].Operand);
        Assert.Equal(new[] { Value, ValueReference, Int32 }, result.LocalSignatureTypes.ToArray());
    }

    [Theory]
    // The receiver is reached from a branch, or replaced or exposed by the callee.
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void ValueTypeBodiesSpillOtherReceivers(int shape)
    {
        var caller = Method("Caller", MethodSignatureModel.Create(Void), 1);
        var callee = Method("Leaf", MethodSignatureModel.Create(Void, Int32), 2, isStatic: false, declaringType: Value);
        var call = new CilInstruction(2, 3, CilOperation.Call, new CilOperand.MethodInstance(callee));
        ImmutableArray<CilInstruction> callerInstructions =
        [
            new(0, 1, shape == 0 ? CilOperation.LoadLocal : CilOperation.LoadLocalAddress, new CilOperand.Index(0)),
            new(1, 2, CilOperation.LoadInt32, new CilOperand.ConstantI4(7)),
            call,
            new(3, 4, CilOperation.Return, new CilOperand.None()),
        ];
        if (shape is 1 or 2)
        {
            // A branch to the argument or to the call itself.
            callerInstructions = callerInstructions.Add(new(4, 5, CilOperation.Branch, new CilOperand.BranchTarget(shape)));
        }
        var calleeBody = Body(callee.Definition, shape switch
        {
            3 => ValueLeaf.SetItem(1, new(1, 2, CilOperation.StoreArgument, new CilOperand.Index(0))).RemoveAt(0)
                .Insert(0, new(0, 1, CilOperation.LoadNull, new CilOperand.None())),
            4 => ValueLeaf.SetItem(0, new(0, 1, CilOperation.LoadArgumentAddress, new CilOperand.Index(0))),
            _ => ValueLeaf,
        });
        var callerBody = Body(caller.Definition, callerInstructions, [shape == 0 ? ValueReference : Value]);

        var result = Create(call, callee, calleeBody).Inline(caller, callerBody,
            Stacks([], [CliValueKind.ManagedAddress], [CliValueKind.ManagedAddress, CliValueKind.I4], [], []));

        Assert.Equal(callerInstructions[0].Operation, result.Instructions[0].Operation);
        Assert.Equal(
            [new CilOperand.Index(2), new CilOperand.Index(1)],
            result.Instructions.Skip(2).Take(2).Select(static instruction => instruction.Operand));
        Assert.All(result.Instructions.Skip(2).Take(2), static instruction => Assert.Equal(CilOperation.StoreLocal, instruction.Operation));
    }

    [Theory]
    [InlineData("local")]
    [InlineData("argument")]
    [InlineData("address")]
    public void InlinedBodiesReadPlainLoadsFromTheCallerSlot(string shape)
    {
        var caller = Method("Caller", shape == "argument"
            ? MethodSignatureModel.Create(Void, Int32)
            : MethodSignatureModel.Create(Void), 1);
        var callee = shape == "address"
            ? Method("Leaf", MethodSignatureModel.Create(Void, Int32), 2, isStatic: false, declaringType: Value)
            : Method("Leaf", MethodSignatureModel.Create(Void, Int32, Int32), 2);
        var call = new CilInstruction(2, 3, CilOperation.Call, new CilOperand.MethodInstance(callee));
        var load = new CilInstruction(0, 1, shape == "argument" ? CilOperation.LoadArgument : CilOperation.LoadLocal,
            new CilOperand.Index(0));
        var callerBody = Body(caller.Definition,
        [
            load,
            new(1, 2, CilOperation.LoadInt32, new CilOperand.ConstantI4(5)),
            call,
            new(3, 4, CilOperation.Return, new CilOperand.None()),
        ], shape switch { "argument" => [], "address" => [ValueReference], _ => [Int32] });
        var first = shape == "address" ? CliValueKind.ManagedAddress : CliValueKind.I4;

        var result = Create(call, callee, Body(callee.Definition, ValueLeaf)).Inline(caller, callerBody,
            Stacks([], [first], [first, CliValueKind.I4], []));

        // The first argument is read where the callee reads it; the second is spilled.
        Assert.Equal(
            [
                CilOperation.Nop, CilOperation.LoadInt32, CilOperation.StoreLocal, load.Operation, CilOperation.Pop,
                CilOperation.LoadLocal, CilOperation.Pop, CilOperation.Return,
            ],
            result.Instructions.Select(static instruction => instruction.Operation));
        Assert.Equal(load.Operand, result.Instructions[3].Operand);
        Assert.Equal(result.Instructions[2].Operand, result.Instructions[5].Operand);
    }

    [Theory]
    [InlineData(CilOperation.LoadLocal)]
    [InlineData(CilOperation.LoadArgument)]
    public void InlinedBodiesReadValueFieldsThroughTheCallerSlot(CilOperation load)
    {
        var fromLocal = load == CilOperation.LoadLocal;
        var caller = Method("Caller", fromLocal ? MethodSignatureModel.Create(Void) : MethodSignatureModel.Create(Void, Value), 1);
        var callee = Method("Leaf", MethodSignatureModel.Create(Void, Value), 2);
        var field = new CilOperand.Entity(new(Assembly, 0x04000001));
        var address = fromLocal ? CilOperation.LoadLocalAddress : CilOperation.LoadArgumentAddress;
        var call = new CilInstruction(4, 5, CilOperation.Call, new CilOperand.MethodInstance(callee));
        var callerBody = Body(caller.Definition,
        [
            // A field read through the slot's address leaves no address behind.
            new(0, 1, address, new CilOperand.Index(0)),
            new(1, 2, CilOperation.LoadField, field),
            new(2, 3, CilOperation.Pop, new CilOperand.None()),
            new(3, 4, load, new CilOperand.Index(0)),
            call,
            new(5, 6, CilOperation.Return, new CilOperand.None()),
        ], fromLocal ? [Value] : []);
        var calleeBody = Body(callee.Definition,
        [
            new(0, 1, CilOperation.LoadArgumentAddress, new CilOperand.Index(0)),
            new(1, 2, CilOperation.LoadField, field),
            new(2, 3, CilOperation.Pop, new CilOperand.None()),
            new(3, 4, CilOperation.Return, new CilOperand.None()),
        ]);

        var result = Create(call, callee, calleeBody).Inline(caller, callerBody,
            Stacks([], [CliValueKind.ManagedAddress], [CliValueKind.I4], [], [CliValueKind.ValueType], []));

        // The value is not copied: the body reads its field from the caller's slot.
        Assert.DoesNotContain(result.Instructions, static instruction => instruction.Operation == CilOperation.StoreLocal);
        Assert.Equal(CilOperation.Nop, result.Instructions[3].Operation);
        Assert.Equal(new CilInstruction(-1, -1, address, new CilOperand.Index(0)),
            result.Instructions[4] with { Offset = -1, NextOffset = -1, OriginalOffset = null });
        Assert.Equal(CilOperation.LoadField, result.Instructions[5].Operation);
    }

    [Theory]
    [InlineData("stored")]
    [InlineData("exposed")]
    [InlineData("other-type")]
    [InlineData("entered")]
    [InlineData("replaced")]
    [InlineData("callee-address")]
    public void InlinedBodiesSpillLoadsOfSlotsThatCanChange(string shape)
    {
        var caller = Method("Caller", MethodSignatureModel.Create(Void), 1);
        var callee = Method("Leaf", MethodSignatureModel.Create(Void, Int32, Int32), 2);
        var call = new CilInstruction(shape == "stored" ? 4 : 2, shape == "stored" ? 5 : 3, CilOperation.Call,
            new CilOperand.MethodInstance(callee));
        ImmutableArray<CilInstruction> instructions = shape == "stored"
            ?
            [
                new(0, 1, CilOperation.LoadLocal, new CilOperand.Index(0)),
                new(1, 2, CilOperation.LoadInt32, new CilOperand.ConstantI4(5)),
                new(2, 3, CilOperation.Duplicate, new CilOperand.None()),
                new(3, 4, CilOperation.StoreLocal, new CilOperand.Index(0)),
                call,
                new(5, 6, CilOperation.Return, new CilOperand.None()),
            ]
            :
            [
                new(0, 1, CilOperation.LoadLocal, new CilOperand.Index(0)),
                new(1, 2, CilOperation.LoadInt32, new CilOperand.ConstantI4(5)),
                call,
                new(3, 4, CilOperation.Return, new CilOperand.None()),
                .. shape switch
                {
                    "exposed" => (ImmutableArray<CilInstruction>)
                    [
                        new(4, 5, CilOperation.LoadLocalAddress, new CilOperand.Index(0)),
                        new(5, 6, CilOperation.Pop, new CilOperand.None()),
                    ],
                    "entered" => [new(4, 5, CilOperation.Branch, new CilOperand.BranchTarget(1))],
                    _ => [],
                },
            ];
        var calleeInstructions = shape switch
        {
            "replaced" => ValueLeaf.SetItem(0, new(0, 1, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)))
                .SetItem(1, new(1, 2, CilOperation.StoreArgument, new CilOperand.Index(0))),
            "callee-address" => ValueLeaf.SetItem(0, new(0, 1, CilOperation.LoadArgumentAddress, new CilOperand.Index(0))),
            _ => ValueLeaf,
        };
        var callerBody = Body(caller.Definition, instructions,
            [shape == "other-type" ? CliTypeIdentity.Primitive("u4", CliValueKind.I4) : Int32]);
        var stacks = shape == "stored"
            ? Stacks([], [CliValueKind.I4], [CliValueKind.I4, CliValueKind.I4],
                [CliValueKind.I4, CliValueKind.I4, CliValueKind.I4], [CliValueKind.I4, CliValueKind.I4], [])
            : Stacks([], [CliValueKind.I4], [CliValueKind.I4, CliValueKind.I4], [], []);

        var result = Create(call, callee, Body(callee.Definition, calleeInstructions)).Inline(caller, callerBody, stacks);

        // The load stays and the first argument is spilled to local 1.
        Assert.Equal(CilOperation.LoadLocal, result.Instructions[0].Operation);
        Assert.Contains(result.Instructions, static instruction =>
            instruction is { Operation: CilOperation.StoreLocal, Operand: CilOperand.Index { Value: 1 } });
    }

    [Fact]
    public void ConstructionInPlaceSpillsLoadsOfItsDestination()
    {
        var caller = Method("Caller", MethodSignatureModel.Create(Void), 1);
        var constructor = Method(".ctor", MethodSignatureModel.Create(Void, Value), 2, isStatic: false, declaringType: Value);
        var construct = new CilInstruction(1, 2, CilOperation.NewObject, new CilOperand.MethodInstance(constructor));
        var callerBody = Body(caller.Definition,
        [
            new(0, 1, CilOperation.LoadLocal, new CilOperand.Index(0)),
            construct,
            new(2, 3, CilOperation.StoreLocal, new CilOperand.Index(0)),
            new(3, 4, CilOperation.Return, new CilOperand.None()),
        ], [Value]);

        var result = Create(construct, constructor, Body(constructor.Definition, ValueLeaf)).Inline(caller, callerBody,
            Stacks([], [CliValueKind.ValueType], [CliValueKind.ValueType], []));

        // `v = new Value(v)` is built in v, which is zeroed before the body
        // reads its argument, so the argument is copied out first.
        Assert.Equal(
            [
                CilOperation.LoadLocal, CilOperation.StoreLocal, CilOperation.LoadLocalAddress,
                CilOperation.InitializeObject, CilOperation.LoadLocalAddress, CilOperation.Pop, CilOperation.LoadLocal,
                CilOperation.Pop, CilOperation.Nop, CilOperation.Return,
            ],
            result.Instructions.Select(static instruction => instruction.Operation));
        Assert.Equal(new CilOperand.Index(2), result.Instructions[1].Operand);
        Assert.Equal(new CilOperand.Index(2), result.Instructions[6].Operand);
    }

    [Theory]
    // The receiver slot holds no managed address, there are no stack facts, or there is no receiver slot.
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ValueTypeCallsWithoutAnAddressReceiverStayCalls(int shape)
    {
        var caller = Method("Caller", MethodSignatureModel.Create(Void), 1);
        var callee = Method("Leaf", MethodSignatureModel.Create(Void, Int32), 2, isStatic: false, declaringType: Value);
        var call = new CilInstruction(2, 3, CilOperation.Call, new CilOperand.MethodInstance(callee));
        var callerBody = Body(caller.Definition,
        [
            new(0, 1, CilOperation.LoadLocal, new CilOperand.Index(0)),
            new(1, 2, CilOperation.LoadInt32, new CilOperand.ConstantI4(7)),
            call,
            new(3, 4, CilOperation.Return, new CilOperand.None()),
        ], [CliTypeIdentity.FromStackKind(CliValueKind.NativeInt)]);
        ImmutableArray<CliValueKind> callStack = shape switch
        {
            0 => [CliValueKind.NativeInt, CliValueKind.I4],
            _ => [CliValueKind.I4],
        };
        var stacks = shape == 1
            ? ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty
            : Stacks([], [CliValueKind.NativeInt], callStack, []);

        var result = Create(call, callee, Body(callee.Definition, ValueLeaf)).Inline(caller, callerBody, stacks);

        Assert.Same(callerBody, result);
    }

    [Theory]
    // A dup reads the receiver, and a call consumes it to push another address.
    [InlineData(true)]
    [InlineData(false)]
    public void ValueTypeReceiversAreForwardedOnlyFromAnUntouchedSlot(bool duplicate)
    {
        var caller = Method("Caller", MethodSignatureModel.Create(Void), 1);
        var callee = duplicate
            ? Method("Leaf", MethodSignatureModel.Create(Void, ValueReference), 2, isStatic: false, declaringType: Value)
            : Method("Leaf", MethodSignatureModel.Create(Void), 2, isStatic: false, declaringType: Value);
        var other = Method("Other", MethodSignatureModel.Create(ValueReference, ValueReference, Int32), 3);
        var calleeBody = Body(callee.Definition, duplicate ? ValueLeaf :
        [
            new(0, 1, CilOperation.LoadArgument, new CilOperand.Index(0)),
            new(1, 2, CilOperation.Pop, new CilOperand.None()),
            new(2, 3, CilOperation.Return, new CilOperand.None()),
        ]);
        ImmutableArray<CilInstruction> instructions = duplicate
            ?
            [
                new(0, 1, CilOperation.LoadLocalAddress, new CilOperand.Index(0)),
                new(1, 2, CilOperation.Duplicate, new CilOperand.None()),
                new(2, 3, CilOperation.Call, new CilOperand.MethodInstance(callee)),
                new(3, 4, CilOperation.Return, new CilOperand.None()),
            ]
            :
            [
                new(0, 1, CilOperation.LoadLocalAddress, new CilOperand.Index(0)),
                new(1, 2, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
                new(2, 3, CilOperation.Call, new CilOperand.MethodInstance(other)),
                new(3, 4, CilOperation.Call, new CilOperand.MethodInstance(callee)),
                new(4, 5, CilOperation.Return, new CilOperand.None()),
            ];
        var stacks = duplicate
            ? Stacks([], [CliValueKind.ManagedAddress], [CliValueKind.ManagedAddress, CliValueKind.ManagedAddress], [])
            : Stacks([], [CliValueKind.ManagedAddress], [CliValueKind.ManagedAddress, CliValueKind.I4], [CliValueKind.ManagedAddress], []);
        var call = instructions.Last(static instruction => instruction.Operation == CilOperation.Call);

        var result = Create(call, callee, calleeBody).Inline(caller, Body(caller.Definition, instructions, [Value]), stacks);

        Assert.Equal(CilOperation.LoadLocalAddress, result.Instructions[0].Operation);
        Assert.Contains(result.Instructions, static instruction => instruction is
        { Operation: CilOperation.StoreLocal, Operand: CilOperand.Index { Value: 1 } });
    }

    [Fact]
    public void ValueTypeReceiversAreFoundOnlyWithinTheirBlock()
    {
        var caller = Method("Caller", MethodSignatureModel.Create(Void), 1);
        var callee = Method("Leaf", MethodSignatureModel.Create(Void), 2, isStatic: false, declaringType: Value);
        var calleeBody = Body(callee.Definition,
        [
            new(0, 1, CilOperation.LoadArgument, new CilOperand.Index(0)),
            new(1, 2, CilOperation.Pop, new CilOperand.None()),
            new(2, 3, CilOperation.Return, new CilOperand.None()),
        ]);
        // The receiver comes from before the body starts, or from an instruction without stack facts.
        foreach (var (instructions, stacks) in new[]
        {
            (ImmutableArray.Create(new CilInstruction(0, 1, CilOperation.Call, new CilOperand.MethodInstance(callee)),
                new CilInstruction(1, 2, CilOperation.Return, new CilOperand.None())),
                Stacks([CliValueKind.ManagedAddress], [])),
            (ImmutableArray.Create(new CilInstruction(0, 1, CilOperation.LoadLocalAddress, new CilOperand.Index(0)),
                new CilInstruction(1, 2, CilOperation.Call, new CilOperand.MethodInstance(callee)),
                new CilInstruction(2, 3, CilOperation.Return, new CilOperand.None())),
                ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty.Add(1, [CliValueKind.ManagedAddress])),
        })
        {
            var call = instructions.Single(static instruction => instruction.Operation == CilOperation.Call);
            var result = Create(call, callee, calleeBody).Inline(caller, Body(caller.Definition, instructions, [Value]), stacks);

            Assert.Contains(result.Instructions, static instruction => instruction.Operation == CilOperation.StoreLocal);
        }
    }

    [Fact]
    public void ValueTypeConstructionInitializesAFreshLocal()
    {
        var caller = Method("Caller", MethodSignatureModel.Create(Void), 1);
        var constructor = Method(".ctor", MethodSignatureModel.Create(Void, Int32), 2, isStatic: false, declaringType: Value);
        var field = new CilOperand.Entity(new(Assembly, 0x04000001));
        var body = Body(constructor.Definition,
        [
            new(0, 1, CilOperation.LoadArgument, new CilOperand.Index(0)),
            new(1, 2, CilOperation.LoadArgument, new CilOperand.Index(1)),
            new(2, 3, CilOperation.StoreField, field),
            new(3, 4, CilOperation.Return, new CilOperand.None()),
        ]);
        var construct = new CilInstruction(1, 2, CilOperation.NewObject, new CilOperand.MethodInstance(constructor));
        var callerBody = Body(caller.Definition,
        [
            new(0, 1, CilOperation.LoadInt32, new CilOperand.ConstantI4(7)),
            construct,
            new(2, 3, CilOperation.Pop, new CilOperand.None()),
            new(3, 4, CilOperation.Return, new CilOperand.None()),
        ]);

        var result = Inline(Create(construct, constructor, body), caller, callerBody);

        Assert.Equal(
            [
                CilOperation.LoadInt32, CilOperation.StoreLocal, CilOperation.LoadLocalAddress, CilOperation.InitializeObject,
                CilOperation.LoadLocalAddress, CilOperation.LoadLocal, CilOperation.StoreField, CilOperation.LoadLocal,
                CilOperation.Pop, CilOperation.Return,
            ],
            result.Instructions.Select(static instruction => instruction.Operation));
        Assert.Equal(
            [2, 0, 0, 2, 0],
            result.Instructions.Where(static instruction => instruction.Operand is CilOperand.Index)
                .Select(static instruction => Assert.IsType<CilOperand.Index>(instruction.Operand).Value));
        Assert.Equal(new CilOperand.TypeIdentity(Value), result.Instructions[3].Operand);
        Assert.Equal(new[] { Value, ValueReference, Int32 }, result.LocalSignatureTypes.ToArray());
        Assert.Equal(callerBody.MaxStack + body.MaxStack + 1, result.MaxStack);

        // A constructor that replaces or exposes its receiver stays a construction.
        foreach (var operation in new[] { CilOperation.StoreArgument, CilOperation.LoadArgumentAddress })
        {
            var writes = body with { Instructions = body.Instructions.SetItem(0, new(0, 1, operation, new CilOperand.Index(0))) };
            Assert.Same(callerBody, Inline(Create(construct, constructor, writes), caller, callerBody));
        }
    }

    [Theory]
    [InlineData("stored")]
    [InlineData("argument")]
    [InlineData("region")]
    [InlineData("entry")]
    [InlineData("effect")]
    [InlineData("other-type")]
    [InlineData("touched")]
    [InlineData("unstored")]
    public void ValueTypeConstructionBuildsInTheLocalItIsStoredTo(string shape)
    {
        var caller = Method("Caller", MethodSignatureModel.Create(Void), 1);
        var constructor = Method(".ctor", MethodSignatureModel.Create(Void, Int32), 2, isStatic: false, declaringType: Value);
        var field = new CilOperand.Entity(new(Assembly, 0x04000001));
        var body = Body(constructor.Definition,
        [
            new(0, 1, CilOperation.LoadArgument, new CilOperand.Index(0)),
            new(1, 2, CilOperation.LoadArgument, new CilOperand.Index(1)),
            new(2, 3, CilOperation.StoreField, field),
            new(3, 4, CilOperation.Return, new CilOperand.None()),
        ]);
        var construct = new CilInstruction(1, 2, CilOperation.NewObject, new CilOperand.MethodInstance(constructor));
        CilInstruction Store(int offset, int local) => new(offset, offset + 1, CilOperation.StoreLocal, new CilOperand.Index(local));
        CilInstruction Constant(int offset) => new(offset, offset + 1, CilOperation.LoadInt32, new CilOperand.ConstantI4(3));
        var ret = new CilInstruction(5, 6, CilOperation.Return, new CilOperand.None());
        // A constructed value, then an unrelated store, then the value's store.
        ImmutableArray<CilInstruction> instructions = shape switch
        {
            "touched" =>
            [
                Constant(0), construct, new(2, 3, CilOperation.LoadLocal, new CilOperand.Index(0)),
                new(3, 4, CilOperation.Pop, new CilOperand.None()), Store(4, 0), ret,
            ],
            "unstored" => [Constant(0), construct, new(2, 3, CilOperation.Pop, new CilOperand.None())],
            _ =>
            [
                Constant(0), construct,
                shape switch
                {
                    "effect" => new(2, 3, CilOperation.LoadStaticField, field),
                    // An argument with the destination's index is not the destination.
                    "argument" => new(2, 3, CilOperation.LoadArgument, new CilOperand.Index(0)),
                    _ => Constant(2),
                },
                Store(3, 1), Store(4, 0), ret,
                .. shape == "entry" ? [new CilInstruction(6, 7, CilOperation.Branch, new CilOperand.BranchTarget(2))] : ImmutableArray<CilInstruction>.Empty,
            ],
        };
        var callerBody = Body(caller.Definition, instructions, [shape == "other-type" ? CliTypeIdentity.Named(Assembly, "", "Other", true) : Value, Int32]) with
        {
            ExceptionRegions = shape == "region"
                ? [new(CilExceptionRegionKind.Finally, 0, 5, 5, 1, null, null)]
                : [],
        };
        var stacks = shape == "touched"
            ? Stacks([], [CliValueKind.I4], [CliValueKind.ValueType], [CliValueKind.ValueType, CliValueKind.ValueType], [CliValueKind.ValueType], [])
            : Stacks([], [CliValueKind.I4], [CliValueKind.ValueType], [CliValueKind.ValueType, CliValueKind.I4], [CliValueKind.ValueType], [], []);

        var result = Create(construct, constructor, body).Inline(caller, callerBody, stacks);

        // The constructor receives an argument local and the receiver; a fresh
        // instance local precedes them unless the value is built in local 0.
        var fresh = shape is not ("stored" or "argument");
        Assert.Equal(callerBody.LocalSignatureTypes.Length + (fresh ? 3 : 2), result.LocalSignatureTypes.Length);
        var instance = fresh ? 2 : 0;
        Assert.Equal(new CilOperand.Index(instance), result.Instructions[2].Operand);
        Assert.Equal(CilOperation.InitializeObject, result.Instructions[3].Operation);
        Assert.Equal(fresh, result.Instructions.Any(instruction =>
            instruction is { Operation: CilOperation.LoadLocal, Operand: CilOperand.Index { Value: var local } } && local == instance));
        if (!fresh)
        {
            Assert.DoesNotContain(result.Instructions, instruction =>
                instruction is { Operation: CilOperation.StoreLocal, Operand: CilOperand.Index { Value: 0 } });
            Assert.Equal(CilOperation.Nop, result.Instructions[^2].Operation);
        }
    }

    [Fact]
    public void MarksCallsThatABackwardBranchSpans()
    {
        var caller = Method("Caller", MethodSignatureModel.Create(CliValueKind.Void), 1);
        var callee = Method("Callee", MethodSignatureModel.Create(CliValueKind.Void), 2);
        var call = new CilOperand.MethodInstance(callee);
        var body = Body(
            caller.Definition,
            [
                new(0, 1, CilOperation.Call, call),
                new(1, 2, CilOperation.Call, call),
                new(2, 3, CilOperation.Call, call),
                new(3, 4, CilOperation.Branch, new CilOperand.BranchTarget(1)),
                new(4, 5, CilOperation.Call, call),
                new(5, 6, CilOperation.Switch, new CilOperand.SwitchTargets([7, 4])),
                new(6, 7, CilOperation.Call, call),
                new(7, 8, CilOperation.Return, new CilOperand.None()),
            ]);
        var resolver = new LoopRecordingResolver();

        Inline(new ManagedMethodInliner(resolver, new TerminalContinuationInliningPlanner()), caller, body);

        // The branch at 3 loops over 1 to 3, and the switch at 5 back to 4.
        Assert.Equal(
            [(0, false), (1, true), (2, true), (4, true), (6, false)],
            resolver.Requests);
    }

    private sealed class LoopRecordingResolver : IManagedInliningResolver
    {
        public List<(int Offset, bool InLoop)> Requests { get; } = [];

        public ManagedInliningPlan? Resolve(ManagedInliningRequest request)
        {
            Requests.Add((request.Call.Offset, request.InLoop));
            return null;
        }
    }

    [Fact]
    public void PreservesBodiesWithoutCalls()
    {
        var caller = Method("Caller", MethodSignatureModel.Create(CliValueKind.Void), 1);
        var body = Body(
            caller.Definition,
            [new(0, 1, CilOperation.Return, new CilOperand.None())]);
        var unused = new RecordingResolver(null);

        Assert.Same(body, Inline(
            new ManagedMethodInliner(unused, new TerminalContinuationInliningPlanner()),
            caller,
            body));
        // Only calls are offered to the resolver.
        Assert.Equal(0, unused.Calls);

        var protectedBody = body with
        {
            ExceptionRegions =
            [
                new(CilExceptionRegionKind.Catch, 0, 1, 0, 1, null, null),
            ],
        };
        Assert.Same(
            protectedBody,
            Inline(
                new ManagedMethodInliner(
                    new UnusedResolver(),
                    new TerminalContinuationInliningPlanner()),
                caller,
                protectedBody));
    }

    [Fact]
    public void InlinesInsideExceptionRegionsAndRemapsTheirBounds()
    {
        var caller = Method("Caller", MethodSignatureModel.Create(CliValueKind.Void), 1);
        var callee = Method("Leaf", MethodSignatureModel.Create(Int32, Int32), 2);
        var calleeBody = Body(
            callee.Definition,
            [
                new(0, 1, CilOperation.LoadArgument, new CilOperand.Index(0)),
                new(1, 2, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
                new(2, 3, CilOperation.Add, new CilOperand.None()),
                new(3, 4, CilOperation.Return, new CilOperand.None()),
            ]);
        var call = new CilInstruction(2, 3, CilOperation.Call, new CilOperand.MethodInstance(callee));
        var callerBody = Body(
            caller.Definition,
            [
                new(0, 1, CilOperation.Nop, new CilOperand.None()),
                new(1, 2, CilOperation.LoadInt32, new CilOperand.ConstantI4(7)),
                call,
                new(3, 4, CilOperation.Pop, new CilOperand.None()),
                new(4, 5, CilOperation.Leave, new CilOperand.BranchTarget(6)),
                new(5, 6, CilOperation.EndFinally, new CilOperand.None()),
                new(6, 7, CilOperation.Return, new CilOperand.None()),
            ]) with
        {
            ExceptionRegions =
            [
                new(CilExceptionRegionKind.Finally, 0, 5, 5, 1, null, null),
                new(CilExceptionRegionKind.Filter, 1, 3, 5, 1, null, 4),
            ],
        };
        var inliner = new ManagedMethodInliner(
            new RecordingResolver(Plan(callee, calleeBody), call.Offset),
            new UnusedTerminalContinuations());

        var result = Inline(inliner, caller, callerBody);

        Assert.Equal(
            [
                CilOperation.Nop, CilOperation.LoadInt32, CilOperation.StoreLocal, CilOperation.LoadLocal,
                CilOperation.LoadInt32, CilOperation.Add, CilOperation.Pop, CilOperation.Leave,
                CilOperation.EndFinally, CilOperation.Return,
            ],
            result.Instructions.Select(static instruction => instruction.Operation));
        Assert.Equal(new CilOperand.BranchTarget(9), result.Instructions[7].Operand);
        Assert.Equal(
            new CilExceptionRegion[]
            {
                new(CilExceptionRegionKind.Finally, 0, 8, 8, 1, null, null),
                new(CilExceptionRegionKind.Filter, 1, 6, 8, 1, null, 7),
            },
            result.ExceptionRegions.ToArray());
    }

    [Fact]
    public void ReturnsBranchToANopBeforeATryThatFollowsTheCall()
    {
        var caller = Method("Caller", MethodSignatureModel.Create(CliValueKind.Void), 1);
        var callee = Method("Guard", MethodSignatureModel.Create(Void, Int32), 2);
        var calleeBody = Body(
            callee.Definition,
            [
                new(0, 1, CilOperation.LoadArgument, new CilOperand.Index(0)),
                new(1, 2, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(3)),
                new(2, 3, CilOperation.Return, new CilOperand.None()),
                new(3, 4, CilOperation.Return, new CilOperand.None()),
            ]);
        var call = new CilInstruction(1, 2, CilOperation.Call, new CilOperand.MethodInstance(callee));
        var callerBody = Body(
            caller.Definition,
            [
                new(0, 1, CilOperation.LoadInt32, new CilOperand.ConstantI4(7)),
                call,
                new(2, 3, CilOperation.Nop, new CilOperand.None()),
                new(3, 4, CilOperation.Leave, new CilOperand.BranchTarget(5)),
                new(4, 5, CilOperation.EndFinally, new CilOperand.None()),
                new(5, 6, CilOperation.Return, new CilOperand.None()),
            ]) with
        {
            ExceptionRegions = [new(CilExceptionRegionKind.Finally, 2, 2, 4, 1, null, null)],
        };
        var inliner = new ManagedMethodInliner(
            new RecordingResolver(Plan(callee, calleeBody), call.Offset),
            new UnusedTerminalContinuations());

        var result = Inline(inliner, caller, callerBody);

        Assert.Equal(
            [
                CilOperation.LoadInt32, CilOperation.StoreLocal, CilOperation.LoadLocal, CilOperation.BranchIfTrue,
                CilOperation.Branch, CilOperation.Nop, CilOperation.Nop, CilOperation.Leave,
                CilOperation.EndFinally, CilOperation.Return,
            ],
            result.Instructions.Select(static instruction => instruction.Operation));
        Assert.Equal(new CilOperand.BranchTarget(5), result.Instructions[3].Operand);
        Assert.Equal(new CilOperand.BranchTarget(5), result.Instructions[4].Operand);
        Assert.Equal(
            [new CilExceptionRegion(CilExceptionRegionKind.Finally, 6, 2, 8, 1, null, null)],
            result.ExceptionRegions.ToArray());
    }

    [Fact]
    public void KeepsConstrainedCallsAndCallsBeyondTheBudgets()
    {
        var caller = Method("Caller", MethodSignatureModel.Create(CliValueKind.Void), 1);
        var callee = Method("Leaf", MethodSignatureModel.Create(CliValueKind.Void), 2);
        var call = new CilInstruction(1, 2, CilOperation.Call, new CilOperand.MethodInstance(callee));
        CilMethodBody Caller(CilOperation first) => Body(
            caller.Definition,
            [
                new(0, 1, first, first == CilOperation.Constrained ? new CilOperand.TypeIdentity(Owner) : new CilOperand.None()),
                call,
                new(2, 3, CilOperation.Return, new CilOperand.None()),
            ]);
        var small = Body(callee.Definition, [new(0, 1, CilOperation.Return, new CilOperand.None())]);
        var constrained = new RecordingResolver(Plan(callee, small), call.Offset);
        var constrainedBody = Caller(CilOperation.Constrained);
        Assert.Same(constrainedBody,
            Inline(new ManagedMethodInliner(constrained, new TerminalContinuationInliningPlanner()), caller, constrainedBody));
        Assert.Equal(0, constrained.Calls);

        var large = Body(callee.Definition,
            [.. Enumerable.Range(0, ManagedMethodInliner.MaximumInstructions).Select(static index =>
                new CilInstruction(index, index + 1, CilOperation.Nop, new CilOperand.None())),
                new(ManagedMethodInliner.MaximumInstructions, ManagedMethodInliner.MaximumInstructions + 1, CilOperation.Return, new CilOperand.None())]);
        var withLocals = Body(callee.Definition, [new(0, 1, CilOperation.Return, new CilOperand.None())],
            [.. Enumerable.Repeat(Int32, ManagedMethodInliner.MaximumLocals + 1)]);
        foreach (var calleeBody in new[] { large, withLocals })
        {
            var unchanged = Caller(CilOperation.Nop);
            Assert.Same(unchanged, Inline(
                new ManagedMethodInliner(new RecordingResolver(Plan(callee, calleeBody), call.Offset), new TerminalContinuationInliningPlanner()),
                caller, unchanged));
        }
    }

    [Fact]
    public void InlinesOneLevelPerRound()
    {
        var caller = Method("Caller", MethodSignatureModel.Create(CliValueKind.Void), 1);
        var callee = Method("Leaf", MethodSignatureModel.Create(CliValueKind.Void), 2);
        var calleeBody = Body(
            callee.Definition,
            [
                new(0, 1, CilOperation.Call, new CilOperand.MethodInstance(callee)),
                new(1, 2, CilOperation.Return, new CilOperand.None()),
            ]);
        var resolver = new EveryCallResolver(Plan(callee, calleeBody));
        var callerBody = Body(
            caller.Definition,
            [
                new(0, 1, CilOperation.Call, new CilOperand.MethodInstance(callee)),
                new(1, 2, CilOperation.Return, new CilOperand.None()),
            ]);

        var result = new ManagedMethodInliner(resolver, new TerminalContinuationInliningPlanner()).Inline(
            caller, callerBody, ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty, nested: true);

        // The callee's own call waits for the pipeline's next round.
        Assert.Equal(1, resolver.Calls);
        Assert.True(resolver.Nested);
        Assert.Single(result.Instructions, static instruction => instruction.Operation == CilOperation.Call);
    }

    [Fact]
    public void RejectsMalformedIndexesAndCallerBranches()
    {
        var caller = Method("Caller", MethodSignatureModel.Create(CliValueKind.Void), 1);
        var callee = Method(
            "Leaf",
            MethodSignatureModel.Create(Void, Int32),
            2);
        var call = new CilInstruction(
            1,
            2,
            CilOperation.Call,
            new CilOperand.MethodInstance(callee));
        var malformedCallee = Body(
            callee.Definition,
            [
                new(0, 1, CilOperation.LoadArgument, new CilOperand.None()),
                new(1, 2, CilOperation.Return, new CilOperand.None()),
            ]);
        var callerBody = Body(
            caller.Definition,
            [
                new(0, 1, CilOperation.Nop, new CilOperand.None()),
                call,
                new(2, 3, CilOperation.Return, new CilOperand.None()),
            ]);
        Assert.Throws<InvalidOperationException>(() =>
            Inline(Create(call, callee, malformedCallee), caller, callerBody));

        var validCallee = Body(
            callee.Definition,
            [new(0, 1, CilOperation.Return, new CilOperand.None())]);
        var invalidBranchBody = callerBody with
        {
            Instructions =
            [
                new(0, 1, CilOperation.Branch, new CilOperand.BranchTarget(99)),
                call,
                new(2, 3, CilOperation.Return, new CilOperand.None()),
            ],
        };
        Assert.Throws<InvalidOperationException>(() =>
            Inline(Create(call, callee, validCallee), caller, invalidBranchBody));
    }

    [Fact]
    public void RejectsMissingDependencyAndInputs()
    {
        var caller = Method("Caller", MethodSignatureModel.Create(CliValueKind.Void), 1);
        var body = Body(
            caller.Definition,
            [new(0, 1, CilOperation.Return, new CilOperand.None())]);

        Assert.Throws<ArgumentNullException>(() => new ManagedMethodInliner(
            null!,
            new TerminalContinuationInliningPlanner()));
        Assert.Throws<ArgumentNullException>(() => new ManagedMethodInliner(
            new RecordingResolver(null),
            null!));
        var inliner = new ManagedMethodInliner(
            new RecordingResolver(null),
            new TerminalContinuationInliningPlanner());
        Assert.Throws<ArgumentNullException>(() => inliner.Inline(
            null!,
            body,
            ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty));
        Assert.Throws<ArgumentNullException>(() => inliner.Inline(
            caller,
            null!,
            ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty));
        Assert.Throws<ArgumentNullException>(() => inliner.Inline(
            caller,
            body,
            null!));
    }

    private static ManagedMethodInliner Create(
        CilInstruction call,
        MethodInstanceModel method,
        CilMethodBody body) => new(
            new RecordingResolver(Plan(method, body), call.Offset),
            new TerminalContinuationInliningPlanner());

    private static ManagedInliningPlan Plan(
        MethodInstanceModel method,
        CilMethodBody body) => new(method, body)
        {
            InstructionEntryStacks = body.Instructions
                .Where(static instruction =>
                    instruction.Operation == CilOperation.Return)
                .ToImmutableDictionary(
                    static instruction => instruction.Offset,
                    _ => method.Signature.ReturnType == CliValueKind.Void
                        ? ImmutableArray<CliValueKind>.Empty
                        : [method.Signature.ReturnType]),
        };

    private static CilMethodBody Inline(
        ManagedMethodInliner inliner,
        MethodInstanceModel caller,
        CilMethodBody body) => inliner.Inline(
            caller,
            body,
            ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty);

    private static MethodInstanceModel Method(
        string name,
        MethodSignatureModel signature,
        int token,
        bool isStatic = true,
        CliTypeIdentity? declaringType = null)
    {
        var definition = new MethodDefinitionModel(
            new(Assembly, 0x06000000 + token),
            new(Assembly, 0x02000001),
            name,
            isStatic,
            signature,
            1);
        return new(
            definition,
            declaringType ?? Owner,
            [],
            signature);
    }

    private static ImmutableDictionary<int, ImmutableArray<CliValueKind>> Stacks(
        params ImmutableArray<CliValueKind>[] stacks) =>
        stacks.Select((stack, offset) => (stack, offset)).ToImmutableDictionary(pair => pair.offset, pair => pair.stack);

    private static CilMethodBody Body(
        MethodDefinitionModel method,
        ImmutableArray<CilInstruction> instructions,
        ImmutableArray<CliTypeIdentity> locals = default)
    {
        locals = locals.IsDefault ? [] : locals;
        return new(
            method,
            2,
            [.. locals.Select(static type => type.StackKind)],
            instructions)
        {
            LocalSignatureTypes = locals,
        };
    }

    private sealed class RecordingResolver(
        ManagedInliningPlan? plan,
        int callOffset = -1) : IManagedInliningResolver
    {
        public int Calls { get; private set; }

        public ManagedInliningPlan? Resolve(ManagedInliningRequest request)
        {
            Calls++;
            return request.Call.Offset == callOffset ? plan : null;
        }
    }

    private sealed class UnusedTerminalContinuations : ITerminalContinuationInliningPlanner
    {
        public TerminalContinuationInliningPlan? Plan(TerminalContinuationInliningRequest request) =>
            throw new InvalidOperationException("Exception regions forbid moving caller instructions.");
    }

    private sealed class EveryCallResolver(ManagedInliningPlan plan) : IManagedInliningResolver
    {
        public int Calls { get; private set; }
        public bool Nested { get; private set; }

        public ManagedInliningPlan? Resolve(ManagedInliningRequest request)
        {
            Calls++;
            Nested = request.Nested;
            return plan;
        }
    }

    private sealed class UnusedResolver : IManagedInliningResolver
    {
        public ManagedInliningPlan? Resolve(ManagedInliningRequest request) =>
            throw new InvalidOperationException();
    }

    private sealed class MultipleResolver(
        IReadOnlyDictionary<int, ManagedInliningPlan> plans) :
        IManagedInliningResolver
    {
        public List<int> RequestedOffsets { get; } = [];

        public ManagedInliningPlan? Resolve(ManagedInliningRequest request)
        {
            RequestedOffsets.Add(request.Call.Offset);
            return plans.GetValueOrDefault(request.Call.Offset);
        }
    }
}
