// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.ControlFlow.Draft;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.ControlFlow.Structuring;
using NetWasm.Compiler.Core;
using Xunit;
using static NetWasm.Compiler.ControlFlow.Tests.ControlFlowTestSupport;
using static NetWasm.Compiler.ControlFlow.Tests.Structuring.StructuredTree;

namespace NetWasm.Compiler.ControlFlow.Tests.Structuring;

// An else-if chain whose arms rejoin is structured flat: one labeled block that ends at the join, each condition an
// if whose arm branches out of it, so the output nests a constant depth however long the chain. Nested, a chain of N
// arms nested N levels, which Binaryen and engines walk recursively.
public sealed class ElseIfChainStructuringTests
{
    // Roslyn's shape: each condition jumps to the next when it does not hold, its arm assigns and jumps to the join.
    [Fact]
    public void ALongChainNestsAConstantDepth()
    {
        var method = Structurize(Chain(200));

        Assert.InRange(Depth(method.Body), 1, 4);
        // 199 links, each an if whose arm branches to the join, then the last condition's if, whose arms fall through.
        var chain = Assert.Single(Descendants(method.Body).OfType<StructuredLabeledBlock>());
        Assert.Equal(200, chain.Body.Regions.OfType<StructuredIf>().Count());
        Assert.Equal(199, Descendants(chain.Body).OfType<StructuredLabelBranch>().Count());
    }

    // The chain continues on whichever arm starts the next condition: here a condition jumps to its arm and falls
    // through to the next condition.
    [Fact]
    public void AChainContinuesOnTheArmThatFallsThrough()
    {
        var method = Structurize(JumpingChain(50));

        Assert.InRange(Depth(method.Body), 1, 4);
        Assert.Single(Descendants(method.Body).OfType<StructuredLabeledBlock>());
    }

    // An arm that returns never reaches the join, so it does not branch to it.
    [Fact]
    public void AnArmThatReturnsDoesNotBranchToTheJoin()
    {
        var method = Structurize(Chain(20, returns: arm => arm % 2 == 0));

        Assert.InRange(Depth(method.Body), 1, 4);
        var chain = Assert.Single(Descendants(method.Body).OfType<StructuredLabeledBlock>());
        Assert.Equal(9, Descendants(chain.Body).OfType<StructuredLabelBranch>().Count());
    }

    // The policy counts a chain's links, each condition but the last. The size-aware presets (Os, Oz) keep chains of
    // up to four conditions nested and flatten from five; the others flatten from three.
    [Theory]
    [InlineData(BranchPrefixDuplicationPolicy.SizeAware, 4, false)]
    [InlineData(BranchPrefixDuplicationPolicy.SizeAware, 5, true)]
    [InlineData(BranchPrefixDuplicationPolicy.Speed, 2, false)]
    [InlineData(BranchPrefixDuplicationPolicy.Speed, 3, true)]
    [InlineData(BranchPrefixDuplicationPolicy.Disabled, 3, true)]
    public void APresetsChainsFlattenFromItsMinimum(BranchPrefixDuplicationPolicy duplication, int conditions, bool flat)
    {
        var method = new ValidatedStructuredMethodBuilderFactory().Create(duplication).Build(Validate(Chain(conditions)));

        Assert.Equal(flat, Descendants(method.Body).OfType<StructuredLabeledBlock>().Any());
        Assert.Equal(flat, ElseIfChainPolicy.For(duplication).Flattens(conditions - 1));
    }

    // Two conditions stay an if with an else-if, as before.
    [Fact]
    public void AShortChainStaysNested()
    {
        var method = Structurize(Chain(2));

        Assert.Empty(Descendants(method.Body).OfType<StructuredLabeledBlock>());
        Assert.Equal(2, Descendants(method.Body).OfType<StructuredIf>().Count());
    }

    // An arm entered from somewhere other than its condition is not the chain's alone, so the chain ends before it and
    // the rest is structured as before.
    [Fact]
    public void AnArmEnteredFromElsewhereEndsTheChain()
    {
        var il = new Il();
        for (var arm = 0; arm < 6; arm++)
        {
            il.Label($"c{arm}");
            il.Emit(CilOperation.LoadArgument, new CilOperand.Index(0));
            il.Emit(CilOperation.LoadInt32, arm);
            il.Emit(CilOperation.BranchIfNotEqual, arm == 5 ? "else" : $"c{arm + 1}");
            il.Label($"a{arm}");
            il.Emit(CilOperation.LoadInt32, arm * 3);
            il.Emit(CilOperation.StoreLocal, new CilOperand.Index(0));
            // The third arm jumps into the fourth's.
            il.Emit(CilOperation.Branch, arm == 2 ? "a3" : "join");
        }
        il.Label("else");
        il.Emit(CilOperation.LoadInt32, -1);
        il.Emit(CilOperation.StoreLocal, new CilOperand.Index(0));
        Join(il);

        // The speed presets' minimum: a chain of two links flattens.
        var method = new ValidatedStructuredMethodBuilderFactory().Create(BranchPrefixDuplicationPolicy.Speed).Build(Validate(il.Body()));

        // Two links, then the third condition's if, structured as before with the rest nested inside it.
        var chain = Assert.Single(Descendants(method.Body).OfType<StructuredLabeledBlock>());
        Assert.Equal(3, chain.Body.Regions.OfType<StructuredIf>().Count());
        Assert.Equal(2, chain.Body.Regions.OfType<StructuredIf>()
            .Count(conditional => Descendants(conditional.WhenTrue).Concat(Descendants(conditional.WhenFalse))
                .OfType<StructuredLabelBranch>().Any()));
    }

    // Inside a loop an arm may end with a break or a continue, which leave the chain without reaching its join.
    [Fact]
    public void AChainInALoopTakesArmsThatBreakOrContinue()
    {
        var il = new Il();
        il.Label("head");
        il.Emit(CilOperation.LoadArgument, new CilOperand.Index(0));
        il.Emit(CilOperation.LoadInt32, 100);
        il.Emit(CilOperation.BranchIfGreaterThanOrEqualSigned, "exit");
        for (var arm = 0; arm < 8; arm++)
        {
            il.Label($"c{arm}");
            il.Emit(CilOperation.LoadArgument, new CilOperand.Index(0));
            il.Emit(CilOperation.LoadInt32, arm);
            il.Emit(CilOperation.BranchIfNotEqual, arm == 7 ? "else" : $"c{arm + 1}");
            switch (arm)
            {
                case 2:
                    il.Emit(CilOperation.Branch, "exit");
                    break;
                case 4:
                    il.Emit(CilOperation.LoadArgument, new CilOperand.Index(0));
                    il.Emit(CilOperation.LoadInt32, 2);
                    il.Emit(CilOperation.Add);
                    il.Emit(CilOperation.StoreArgument, new CilOperand.Index(0));
                    il.Emit(CilOperation.Branch, "head");
                    break;
                default:
                    il.Emit(CilOperation.LoadInt32, arm * 5);
                    il.Emit(CilOperation.StoreLocal, new CilOperand.Index(0));
                    il.Emit(CilOperation.Branch, "join");
                    break;
            }
        }
        il.Label("else");
        il.Emit(CilOperation.LoadInt32, -1);
        il.Emit(CilOperation.StoreLocal, new CilOperand.Index(0));
        il.Label("join");
        il.Emit(CilOperation.LoadArgument, new CilOperand.Index(0));
        il.Emit(CilOperation.LoadInt32, 1);
        il.Emit(CilOperation.Add);
        il.Emit(CilOperation.StoreArgument, new CilOperand.Index(0));
        il.Emit(CilOperation.Branch, "head");
        il.Label("exit");
        il.Emit(CilOperation.LoadLocal, new CilOperand.Index(0));
        il.Emit(CilOperation.Return);

        var method = Structurize(il.Body());

        Assert.Single(Descendants(method.Body).OfType<StructuredLabeledBlock>());
        Assert.InRange(Depth(method.Body), 1, 6);
    }

    // A debug build's condition compares and branches when the comparison is false: the chain continues on the arm
    // the branch takes.
    [Fact]
    public void ADebugBuildsChainIsFlat()
    {
        var il = new Il();
        for (var arm = 0; arm < 30; arm++)
        {
            il.Label($"c{arm}");
            il.Emit(CilOperation.LoadArgument, new CilOperand.Index(0));
            il.Emit(CilOperation.LoadInt32, arm);
            il.Emit(CilOperation.CompareEqual);
            il.Emit(CilOperation.BranchIfFalse, arm == 29 ? "else" : $"c{arm + 1}");
            il.Emit(CilOperation.LoadInt32, arm * 3);
            il.Emit(CilOperation.StoreLocal, new CilOperand.Index(0));
            il.Emit(CilOperation.Branch, "join");
        }
        il.Label("else");
        il.Emit(CilOperation.LoadInt32, -1);
        il.Emit(CilOperation.StoreLocal, new CilOperand.Index(0));
        Join(il);

        var method = Structurize(il.Body());

        Assert.InRange(Depth(method.Body), 1, 4);
        Assert.Single(Descendants(method.Body).OfType<StructuredLabeledBlock>());
    }

    // Without a final else the last condition's other arm is the join itself, which no link starts at; an arm that is
    // empty (its condition jumps straight to the join) only branches to it.
    [Fact]
    public void AChainEndsAtTheJoinAndTakesEmptyArms()
    {
        var il = new Il();
        for (var arm = 0; arm < 10; arm++)
        {
            il.Label($"c{arm}");
            il.Emit(CilOperation.LoadArgument, new CilOperand.Index(0));
            il.Emit(CilOperation.LoadInt32, arm);
            if (arm == 4)
            {
                // if (value == 4) { } else if ...: the arm is the join.
                il.Emit(CilOperation.BranchIfEqual, "join");
                continue;
            }
            il.Emit(CilOperation.BranchIfNotEqual, arm == 9 ? "join" : $"c{arm + 1}");
            il.Emit(CilOperation.LoadInt32, arm * 3);
            il.Emit(CilOperation.StoreLocal, new CilOperand.Index(0));
            il.Emit(CilOperation.Branch, "join");
        }
        Join(il);

        var method = Structurize(il.Body());

        var chain = Assert.Single(Descendants(method.Body).OfType<StructuredLabeledBlock>());
        Assert.Contains(chain.Body.Regions.OfType<StructuredIf>(), conditional =>
            conditional.WhenTrue.Regions is [StructuredLabelBranch] || conditional.WhenFalse.Regions is [StructuredLabelBranch]);
    }

    // A condition entered from before the chain as well, or one whose two targets are the same block, starts no link.
    [Fact]
    public void AConditionTheChainDoesNotOwnEndsIt()
    {
        var il = new Il();
        for (var arm = 0; arm < 8; arm++)
        {
            il.Label($"c{arm}");
            il.Emit(CilOperation.LoadArgument, new CilOperand.Index(0));
            il.Emit(CilOperation.LoadInt32, arm);
            il.Emit(CilOperation.BranchIfNotEqual, arm == 7 ? "else" : $"c{arm + 1}");
            il.Emit(CilOperation.LoadInt32, arm * 3);
            il.Emit(CilOperation.StoreLocal, new CilOperand.Index(0));
            il.Emit(CilOperation.Branch, "join");
        }
        il.Label("else");
        il.Emit(CilOperation.LoadInt32, -1);
        il.Emit(CilOperation.StoreLocal, new CilOperand.Index(0));
        Join(il);
        // The fourth condition is also reached from an arm of the third.
        var entered = il.Body();
        var fourth = il.Offset("c3");
        var thirdArm = il.Offset("c2") + 3;
        var redirected = entered with
        {
            Instructions = entered.Instructions.SetItem(thirdArm + 2, I(thirdArm + 2, CilOperation.Branch, new CilOperand.BranchTarget(fourth))),
        };
        var sameTargets = entered with
        {
            Instructions = entered.Instructions.SetItem(il.Offset("c5") + 2,
                I(il.Offset("c5") + 2, CilOperation.BranchIfNotEqual, new CilOperand.BranchTarget(il.Offset("c5") + 3))),
        };

        // The chain stops before the fourth condition; what follows is structured from it as before.
        Assert.NotEmpty(Descendants(Structurize(redirected).Body).OfType<StructuredLabeledBlock>());
        Assert.NotEmpty(Descendants(Structurize(sameTargets).Body).OfType<StructuredLabeledBlock>());
    }

    // An arm holding a loop, or one that leaves the loop around the chain other than by its break, is not a closed
    // region the chain can take; nor is a condition outside the loop's blocks a link.
    [Fact]
    public void AnArmWithALoopOrAnotherExitEndsTheChain()
    {
        var il = new Il();
        il.Label("head");
        il.Emit(CilOperation.LoadArgument, new CilOperand.Index(0));
        il.Emit(CilOperation.LoadInt32, 100);
        il.Emit(CilOperation.BranchIfGreaterThanOrEqualSigned, "exit");
        for (var arm = 0; arm < 8; arm++)
        {
            il.Label($"c{arm}");
            il.Emit(CilOperation.LoadArgument, new CilOperand.Index(0));
            il.Emit(CilOperation.LoadInt32, arm);
            if (arm == 2)
            {
                // if (value == 2) break; else if ...: the condition jumps straight to the loop's exit.
                il.Emit(CilOperation.BranchIfEqual, "exit");
                continue;
            }
            // The sixth condition's other arm leaves the loop for a condition after it.
            il.Emit(CilOperation.BranchIfNotEqual, arm == 5 ? "other" : arm == 7 ? "else" : $"c{arm + 1}");
            switch (arm)
            {
                case 3:
                    // An arm with a loop of its own.
                    il.Label("inner");
                    il.Emit(CilOperation.LoadLocal, new CilOperand.Index(0));
                    il.Emit(CilOperation.LoadInt32, 1);
                    il.Emit(CilOperation.Add);
                    il.Emit(CilOperation.StoreLocal, new CilOperand.Index(0));
                    il.Emit(CilOperation.LoadLocal, new CilOperand.Index(0));
                    il.Emit(CilOperation.LoadInt32, 10);
                    il.Emit(CilOperation.BranchIfLessThanSigned, "inner");
                    il.Emit(CilOperation.Branch, "join");
                    break;
                case 6:
                    // An arm that leaves the loop for the code after it.
                    il.Emit(CilOperation.Branch, "after");
                    break;
                default:
                    il.Emit(CilOperation.LoadInt32, arm * 5);
                    il.Emit(CilOperation.StoreLocal, new CilOperand.Index(0));
                    il.Emit(CilOperation.Branch, "join");
                    break;
            }
        }
        il.Label("else");
        il.Emit(CilOperation.LoadInt32, -1);
        il.Emit(CilOperation.StoreLocal, new CilOperand.Index(0));
        il.Label("join");
        il.Emit(CilOperation.LoadArgument, new CilOperand.Index(0));
        il.Emit(CilOperation.LoadInt32, 1);
        il.Emit(CilOperation.Add);
        il.Emit(CilOperation.StoreArgument, new CilOperand.Index(0));
        il.Emit(CilOperation.Branch, "head");
        il.Label("other");
        il.Emit(CilOperation.LoadArgument, new CilOperand.Index(0));
        il.Emit(CilOperation.LoadInt32, 7);
        il.Emit(CilOperation.BranchIfNotEqual, "exit");
        il.Label("after");
        il.Emit(CilOperation.LoadInt32, 9);
        il.Emit(CilOperation.StoreLocal, new CilOperand.Index(0));
        il.Label("exit");
        il.Emit(CilOperation.LoadLocal, new CilOperand.Index(0));
        il.Emit(CilOperation.Return);

        var method = Structurize(il.Body());

        Assert.NotEmpty(Descendants(method.Body));
    }

    // After an irreducible loop, which becomes a dispatcher, the chain is structured in the dispatcher's exit with the
    // dispatcher active; its blocks stay the dispatcher's.
    [Fact]
    public void AChainAfterADispatcherIsFlat()
    {
        var il = new Il();
        il.Emit(CilOperation.LoadArgument, new CilOperand.Index(0));
        il.Emit(CilOperation.LoadInt32, 10);
        il.Emit(CilOperation.BranchIfGreaterThanSigned, "b");
        il.Label("a");
        il.Emit(CilOperation.LoadArgument, new CilOperand.Index(0));
        il.Emit(CilOperation.LoadInt32, 1);
        il.Emit(CilOperation.Add);
        il.Emit(CilOperation.StoreArgument, new CilOperand.Index(0));
        il.Emit(CilOperation.LoadArgument, new CilOperand.Index(0));
        il.Emit(CilOperation.LoadInt32, 20);
        il.Emit(CilOperation.BranchIfLessThanSigned, "b");
        il.Emit(CilOperation.Branch, "c0");
        il.Label("b");
        il.Emit(CilOperation.LoadArgument, new CilOperand.Index(0));
        il.Emit(CilOperation.LoadInt32, 2);
        il.Emit(CilOperation.Add);
        il.Emit(CilOperation.StoreArgument, new CilOperand.Index(0));
        il.Emit(CilOperation.LoadArgument, new CilOperand.Index(0));
        il.Emit(CilOperation.LoadInt32, 30);
        il.Emit(CilOperation.BranchIfLessThanSigned, "a");
        for (var arm = 0; arm < 6; arm++)
        {
            il.Label($"c{arm}");
            il.Emit(CilOperation.LoadArgument, new CilOperand.Index(0));
            il.Emit(CilOperation.LoadInt32, arm + 30);
            il.Emit(CilOperation.BranchIfNotEqual, arm == 5 ? "else" : $"c{arm + 1}");
            il.Emit(CilOperation.LoadInt32, arm * 3);
            il.Emit(CilOperation.StoreLocal, new CilOperand.Index(0));
            il.Emit(CilOperation.Branch, "join");
        }
        il.Label("else");
        il.Emit(CilOperation.LoadInt32, -1);
        il.Emit(CilOperation.StoreLocal, new CilOperand.Index(0));
        Join(il);

        var method = Structurize(il.Body());

        Assert.NotEmpty(Descendants(method.Body));
    }

    // A condition that starts a protected region belongs to the exception-region step, so it starts no link. A protected
    // region is entered by falling into it.
    [Fact]
    public void AProtectedRegionsConditionStartsNoLink()
    {
        var il = new Il();
        il.Emit(CilOperation.LoadArgument, new CilOperand.Index(0));
        il.Emit(CilOperation.BranchIfFalse, "first");
        il.Label("try");
        il.Emit(CilOperation.LoadArgument, new CilOperand.Index(0));
        il.Emit(CilOperation.LoadInt32, 1);
        il.Emit(CilOperation.BranchIfNotEqual, "second");
        il.Emit(CilOperation.LoadInt32, 7);
        il.Emit(CilOperation.StoreLocal, new CilOperand.Index(0));
        il.Emit(CilOperation.Leave, "join");
        il.Label("second");
        il.Emit(CilOperation.LoadInt32, -1);
        il.Emit(CilOperation.StoreLocal, new CilOperand.Index(0));
        il.Emit(CilOperation.Leave, "join");
        il.Label("handler");
        il.Emit(CilOperation.EndFinally);
        il.Label("first");
        il.Emit(CilOperation.LoadInt32, 0);
        il.Emit(CilOperation.StoreLocal, new CilOperand.Index(0));
        il.Emit(CilOperation.Branch, "join");
        Join(il);

        var method = Structurize(il.Body(new CilExceptionRegion(CilExceptionRegionKind.Finally,
            il.Offset("try"), il.Offset("handler") - il.Offset("try"), il.Offset("handler"), 1, null, null)));

        Assert.Empty(Descendants(method.Body).OfType<StructuredLabeledBlock>());
    }

    // The reducible structurer, which replaces a dispatcher, follows a condition's false arm in the sequence and puts
    // its true arm in the if. In a chain whose branches skip to the next condition the true arm starts the next
    // condition, so each condition nested in the one before; the chain's other arms now go in the ifs instead.
    [Fact]
    public void TheReducibleStructurerKeepsAChainInOneSequence()
    {
        var flat = new ReducibleControlFlowStructurer(new CilConditionalBranchClassifier(), new ElseIfChainPolicy(2))
            .Structure(Validate(Chain(200)))!;
        var nested = new ReducibleControlFlowStructurer(new CilConditionalBranchClassifier(), new ElseIfChainPolicy(int.MaxValue))
            .Structure(Validate(Chain(200)))!;
        var falseArms = new ReducibleControlFlowStructurer(new CilConditionalBranchClassifier(), new ElseIfChainPolicy(int.MaxValue))
            .Structure(Validate(JumpingChain(200)))!;

        Assert.Equal(200, DraftDescendants(flat).OfType<StructuredIfDraft>().Count());
        Assert.InRange(DraftDepth(flat), 1, 5);
        Assert.True(DraftDepth(nested) > 200);
        // A chain its false arms continue was already one sequence.
        Assert.InRange(DraftDepth(falseArms), 1, 5);
    }

    // A method whose loops the general structurer gives a dispatcher, leaving two loops at once, is structured again
    // by the reducible structurer; the chain after the loops stays in one sequence there too, and the method passes
    // the structured method's validation.
    [Fact]
    public void AChainAfterLoopsLeftAtOnceStaysFlatWhenItsDispatcherIsEliminated()
    {
        var il = new Il { Locals = 2 };
        // for (i = 0; i < n; i++) for (j = 0; j < 3; j++) if (i + j == n) goto chain;
        il.Emit(CilOperation.LoadInt32, 0);
        il.Emit(CilOperation.StoreLocal, new CilOperand.Index(0));
        il.Label("outer");
        il.Emit(CilOperation.LoadLocal, new CilOperand.Index(0));
        il.Emit(CilOperation.LoadArgument, new CilOperand.Index(0));
        il.Emit(CilOperation.BranchIfGreaterThanOrEqualSigned, "c0");
        il.Emit(CilOperation.LoadInt32, 0);
        il.Emit(CilOperation.StoreLocal, new CilOperand.Index(1));
        il.Label("inner");
        il.Emit(CilOperation.LoadLocal, new CilOperand.Index(1));
        il.Emit(CilOperation.LoadInt32, 3);
        il.Emit(CilOperation.BranchIfGreaterThanOrEqualSigned, "next");
        il.Emit(CilOperation.LoadLocal, new CilOperand.Index(0));
        il.Emit(CilOperation.LoadLocal, new CilOperand.Index(1));
        il.Emit(CilOperation.Add);
        il.Emit(CilOperation.LoadArgument, new CilOperand.Index(0));
        il.Emit(CilOperation.BranchIfEqual, "c0");
        il.Emit(CilOperation.LoadLocal, new CilOperand.Index(1));
        il.Emit(CilOperation.LoadInt32, 1);
        il.Emit(CilOperation.Add);
        il.Emit(CilOperation.StoreLocal, new CilOperand.Index(1));
        il.Emit(CilOperation.Branch, "inner");
        il.Label("next");
        il.Emit(CilOperation.LoadLocal, new CilOperand.Index(0));
        il.Emit(CilOperation.LoadInt32, 1);
        il.Emit(CilOperation.Add);
        il.Emit(CilOperation.StoreLocal, new CilOperand.Index(0));
        il.Emit(CilOperation.Branch, "outer");
        for (var arm = 0; arm < 50; arm++)
        {
            il.Label($"c{arm}");
            il.Emit(CilOperation.LoadArgument, new CilOperand.Index(0));
            il.Emit(CilOperation.LoadInt32, arm);
            il.Emit(CilOperation.BranchIfNotEqual, arm == 49 ? "else" : $"c{arm + 1}");
            il.Emit(CilOperation.LoadInt32, arm * 3);
            il.Emit(CilOperation.StoreLocal, new CilOperand.Index(0));
            il.Emit(CilOperation.Branch, "join");
        }
        il.Label("else");
        il.Emit(CilOperation.LoadInt32, -1);
        il.Emit(CilOperation.StoreLocal, new CilOperand.Index(0));
        Join(il);
        var body = il.Body();

        StructuredMethod Build(DispatcherEliminationPolicy policy) => new ValidatedStructuredMethodBuilderFactory()
            .Create(dispatcherElimination: policy)
            .Build(Validate(body));
        var kept = Build(DispatcherEliminationPolicy.Disabled);
        var eliminated = Build(DispatcherEliminationPolicy.Reducible);

        Assert.Contains(Descendants(kept.Body), region => region is StructuredDispatcher);
        Assert.DoesNotContain(Descendants(eliminated.Body), region => region is StructuredDispatcher);
        Assert.Equal(53, Descendants(eliminated.Body).OfType<StructuredIf>().Count() +
            Descendants(eliminated.Body).OfType<StructuredLoop>().Count());
        Assert.InRange(Depth(eliminated.Body), 1, 8);
    }

    private static CilMethodBody Chain(int conditions, Func<int, bool>? returns = null)
    {
        var il = new Il();
        for (var arm = 0; arm < conditions; arm++)
        {
            il.Label($"c{arm}");
            il.Emit(CilOperation.LoadArgument, new CilOperand.Index(0));
            var next = arm == conditions - 1 ? "else" : $"c{arm + 1}";
            if (arm == 0)
            {
                il.Emit(CilOperation.BranchIfTrue, next);
            }
            else
            {
                il.Emit(CilOperation.LoadInt32, arm);
                il.Emit(CilOperation.BranchIfNotEqual, next);
            }
            il.Emit(CilOperation.LoadInt32, arm * 7 % 101);
            if (returns?.Invoke(arm) == true)
            {
                il.Emit(CilOperation.Return);
                continue;
            }
            il.Emit(CilOperation.StoreLocal, new CilOperand.Index(0));
            il.Emit(CilOperation.Branch, "join");
        }
        il.Label("else");
        il.Emit(CilOperation.LoadInt32, -1);
        il.Emit(CilOperation.StoreLocal, new CilOperand.Index(0));
        Join(il);
        return il.Body();
    }

    // Each condition jumps to its arm, laid out after the conditions, and falls through to the next condition.
    private static CilMethodBody JumpingChain(int conditions)
    {
        var il = new Il();
        for (var arm = 0; arm < conditions; arm++)
        {
            il.Emit(CilOperation.LoadArgument, new CilOperand.Index(0));
            il.Emit(CilOperation.LoadInt32, arm);
            il.Emit(CilOperation.BranchIfEqual, $"a{arm}");
        }
        il.Emit(CilOperation.LoadInt32, -1);
        il.Emit(CilOperation.StoreLocal, new CilOperand.Index(0));
        il.Emit(CilOperation.Branch, "join");
        for (var arm = 0; arm < conditions; arm++)
        {
            il.Label($"a{arm}");
            il.Emit(CilOperation.LoadInt32, arm * 3);
            il.Emit(CilOperation.StoreLocal, new CilOperand.Index(0));
            il.Emit(CilOperation.Branch, "join");
        }
        Join(il);
        return il.Body();
    }

    // The code after the chain, as Roslyn's: it reads the local the arms assign. A join of a load and a return alone
    // is small enough that the structurer copies it into each arm, which leaves no chain to flatten.
    private static void Join(Il il)
    {
        il.Label("join");
        il.Emit(CilOperation.LoadLocal, new CilOperand.Index(0));
        il.Emit(CilOperation.LoadInt32, 2);
        il.Emit(CilOperation.Multiply);
        il.Emit(CilOperation.LoadInt32, 1);
        il.Emit(CilOperation.Add);
        il.Emit(CilOperation.Return);
    }

    private static int DraftDepth(StructuredSequenceDraft sequence) =>
        sequence.Regions.Select(region => DraftChildren(region).Select(child => DraftDepth(child) + 1).DefaultIfEmpty(1).Max())
            .DefaultIfEmpty(0).Max();

    private static IEnumerable<StructuredRegionDraft> DraftDescendants(StructuredSequenceDraft sequence) =>
        sequence.Regions.SelectMany(region => DraftChildren(region).SelectMany(DraftDescendants).Prepend(region));

    private static IEnumerable<StructuredSequenceDraft> DraftChildren(StructuredRegionDraft region) => region switch
    {
        StructuredIfDraft conditional => [conditional.WhenTrue, conditional.WhenFalse],
        StructuredLabeledBlockDraft labeled => [labeled.Body],
        StructuredLabeledLoopDraft loop => [loop.Body],
        StructuredLoopDraft loop => [loop.Body, loop.ContinueBody, loop.ExitBody],
        _ => [],
    };
}
