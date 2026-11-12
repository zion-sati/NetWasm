using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Instructions.Objects;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class SolePredecessorResolverTests
{
    private static readonly CilInstruction Load = new(0, 6, CilOperation.LoadFunction, new CilOperand.Entity(EntryKey));
    private static readonly CilInstruction Construct = new(6, 11, CilOperation.NewObject, new CilOperand.Entity(ConstructorKey));

    [Fact]
    public void TheInstructionFallingThroughIsTheSolePredecessorWhenNothingElseEnters()
    {
        var unrelated = new CilInstruction(11, 16, CilOperation.Branch, new CilOperand.BranchTarget(0));

        Assert.Equal(Load, Resolve([Load, Construct, unrelated], Construct));
    }

    public static TheoryData<string> Unproven => ["first", "gap", "branch", "switch"];

    [Theory]
    [MemberData(nameof(Unproven))]
    public void APredecessorIsNoProofWhenAnotherPathMayEnter(string shape)
    {
        var (body, instruction) = shape switch
        {
            "first" => (ImmutableArray.Create(Construct), Construct),
            "gap" => ([Load with { NextOffset = 5 }, Construct], Construct),
            "branch" => ([Load, Construct, new(11, 16, CilOperation.Branch, new CilOperand.BranchTarget(6))], Construct),
            _ => ([Load, Construct, new(11, 16, CilOperation.Switch, new CilOperand.SwitchTargets([0, 6]))], Construct),
        };

        Assert.Null(Resolve(body, instruction));
    }

    internal static InstructionEmissionRequest Request(ImmutableArray<CilInstruction> body, CilInstruction instruction)
    {
        var request = CreateInstructionRequest(instruction.Operation, [CliValueKind.ManagedReference, CliValueKind.NativeInt],
            instruction.Operand);
        return request with { Instruction = instruction, Header = request.Header with { Instructions = body } };
    }

#pragma warning disable CA1859 // Exercise the resolver through its capability contract.
    private static CilInstruction? Resolve(ImmutableArray<CilInstruction> body, CilInstruction instruction) =>
        ((ISolePredecessorResolver)new SolePredecessorResolver()).Resolve(Request(body, instruction));
#pragma warning restore CA1859
}

// ldftn of an instance method just before newobj closes the delegate over a target that must
// not be null; desktop .NET throws ArgumentException (issue 223's class: a delegate converted
// from a null delegate). Static methods, ldvirtftn and unproven sequences are not checked.
public sealed class DelegateTargetCheckSelectorTests
{
    private static readonly CilInstruction Construct = new(6, 11, CilOperation.NewObject, new CilOperand.Entity(ConstructorKey));

    public static TheoryData<string, bool> Loads => new()
    {
        { "instance by key", true },
        { "instance by instance", true },
        { "static by key", false },
        { "static by instance", false },
        { "virtual load", false },
    };

    [Theory]
    [MemberData(nameof(Loads))]
    public void OnlyAnInstanceMethodLoadedJustBeforeNeedsANonNullTarget(string load, bool selected)
    {
        var program = new FakeProgram();
        var instance = new MethodInstanceModel(program.GetMethod(ConstructorKey),
            CliTypeIdentity.Named(Assembly, "Test", "Type", isValueType: false), [], program.GetMethod(ConstructorKey).Signature);
        var staticMethod = instance with { Definition = program.GetMethod(EntryKey) };
        var (operation, operand) = load switch
        {
            "instance by key" => (CilOperation.LoadFunction, (CilOperand)new CilOperand.Entity(ConstructorKey)),
            "instance by instance" => (CilOperation.LoadFunction, new CilOperand.MethodInstance(instance)),
            "static by key" => (CilOperation.LoadFunction, new CilOperand.Entity(EntryKey)),
            "static by instance" => (CilOperation.LoadFunction, new CilOperand.MethodInstance(staticMethod)),
            _ => (CilOperation.LoadVirtualFunction, new CilOperand.Entity(ConstructorKey)),
        };

        Assert.Equal(selected, Select(program, [new(0, 6, operation, operand), Construct]));
    }

    [Fact]
    public void AnUnprovenSequenceIsNotChecked() =>
        Assert.False(Select(new FakeProgram(),
            [new(0, 6, CilOperation.LoadFunction, new CilOperand.Entity(ConstructorKey)), Construct,
                new(11, 16, CilOperation.Branch, new CilOperand.BranchTarget(6))]));

    [Fact]
    public void AnLdftnWithoutAMethodFailsLoudly() =>
        Assert.Throws<InvalidOperationException>(() => Select(new FakeProgram(),
            [new(0, 6, CilOperation.LoadFunction, new CilOperand.None()), Construct]));

#pragma warning disable CA1859 // Exercise the selector through its capability contract.
    private static bool Select(FakeProgram program, ImmutableArray<CilInstruction> body) =>
        ((IDelegateTargetCheckSelector)new DelegateTargetCheckSelector(new SolePredecessorResolver(), program))
            .Select(SolePredecessorResolverTests.Request(body, Construct));
#pragma warning restore CA1859
}
