using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

// The analyzer's entry stacks: each slot's declared type from the instruction that produced it,
// closed over the body's generic context, and unknown where paths disagree or nothing declares one.
public sealed class StaticReferenceTypeAnalyzerTests
{
    private static readonly AssemblyIdentity Assembly = new("StaticTypes");
    private static readonly CliTypeIdentity A = CliTypeIdentity.Named(Assembly, "Fixtures", "A", false);
    private static readonly CliTypeIdentity B = CliTypeIdentity.Named(Assembly, "Fixtures", "B", false);
    private static readonly CliTypeIdentity Box = CliTypeIdentity.Named(Assembly, "Fixtures", "Box`1", false);
    private static readonly CliTypeIdentity TypeParameter = CliTypeIdentity.GenericParameter(method: false, 0);
    private static readonly CliTypeIdentity MethodParameter = CliTypeIdentity.GenericParameter(method: true, 0);
    private static readonly EntityKey FieldKey = new(Assembly, 0x04000001);

    [Fact]
    public void RejectsAMissingGraph() =>
        Assert.Throws<ArgumentNullException>(() => Analyzer(new()).Analyze(null!));

    // Three paths meet: two load A and one B, so the slot is unknown after the join; where every
    // path loads A it stays A. Without a method instance an argument's type is unknown.
    [Theory]
    [InlineData(1, false)]
    [InlineData(0, true)]
    public void PathsThatDisagreeLeaveTheSlotUnknown(int middleLocal, bool agree)
    {
        var body = Body(
            null,
            (CilOperation.LoadArgument, new CilOperand.Index(0), 0),
            (CilOperation.BranchIfTrue, new CilOperand.BranchTarget(4), 1),
            (CilOperation.LoadLocal, new CilOperand.Index(0), 0),
            (CilOperation.Branch, new CilOperand.BranchTarget(9), 1),
            (CilOperation.LoadArgument, new CilOperand.Index(0), 0),
            (CilOperation.BranchIfTrue, new CilOperand.BranchTarget(8), 1),
            (CilOperation.LoadLocal, new CilOperand.Index(middleLocal), 0),
            (CilOperation.Branch, new CilOperand.BranchTarget(9), 1),
            (CilOperation.LoadLocal, new CilOperand.Index(0), 0),
            (CilOperation.Pop, new CilOperand.None(), 1),
            (CilOperation.Return, new CilOperand.None(), 0));

        var types = Analyzer(new()).Analyze(body.Graph);

        Assert.Equal([null], types[1]);
        Assert.Equal([agree ? A : null], types[9]);
    }

    [Fact]
    public void EachProducerDeclaresItsType()
    {
        var calls = new Dictionary<int, MethodInstanceModel>
        {
            [19] = Method(B, "Act", isStatic: true, CliTypeIdentity.FromStackKind(CliValueKind.Void)),
            [20] = Method(B, "Read", isStatic: true, B),
            [27] = Method(B, ".ctor", isStatic: false, CliTypeIdentity.FromStackKind(CliValueKind.Void)),
        };
        var owner = CliTypeIdentity.GenericInstantiation(Box, [A]);
        var body = Body(
            Method(owner, "Run", isStatic: false, CliTypeIdentity.FromStackKind(CliValueKind.Void), TypeParameter),
            (CilOperation.LoadArgument, new CilOperand.Index(0), 0),
            (CilOperation.Pop, new CilOperand.None(), 1),
            (CilOperation.LoadArgument, new CilOperand.Index(1), 0),
            (CilOperation.Pop, new CilOperand.None(), 1),
            (CilOperation.LoadArgument, new CilOperand.Index(5), 0),
            (CilOperation.Pop, new CilOperand.None(), 1),
            (CilOperation.LoadLocal, new CilOperand.Index(0), 0),
            (CilOperation.Pop, new CilOperand.None(), 1),
            (CilOperation.LoadLocal, new CilOperand.Index(1), 0),
            (CilOperation.Pop, new CilOperand.None(), 1),
            (CilOperation.LoadLocal, new CilOperand.Index(9), 0),
            (CilOperation.Pop, new CilOperand.None(), 1),
            (CilOperation.LoadNull, new CilOperand.None(), 0),
            (CilOperation.LoadField, new CilOperand.Entity(FieldKey), 1),
            (CilOperation.Pop, new CilOperand.None(), 1),
            (CilOperation.LoadStaticField, new CilOperand.FieldInstance(new(Field(), owner, TypeParameter)), 0),
            (CilOperation.Duplicate, new CilOperand.None(), 1),
            (CilOperation.Pop, new CilOperand.None(), 2),
            (CilOperation.Pop, new CilOperand.None(), 1),
            (CilOperation.Call, new CilOperand.None(), 0),
            (CilOperation.Call, new CilOperand.None(), 0),
            (CilOperation.Pop, new CilOperand.None(), 1),
            (CilOperation.LoadNull, new CilOperand.None(), 0),
            (CilOperation.CallIndirect, new CilOperand.CallSite(MethodSignatureModel.Create(CliValueKind.Void)), 1),
            (CilOperation.LoadNull, new CilOperand.None(), 0),
            (CilOperation.CallIndirect, new CilOperand.CallSite(MethodSignatureModel.Create(CliValueKind.I4)), 1),
            (CilOperation.Pop, new CilOperand.None(), 1),
            (CilOperation.NewObject, new CilOperand.None(), 0),
            (CilOperation.CastClass, new CilOperand.TypeIdentity(A), 1),
            (CilOperation.Pop, new CilOperand.None(), 1),
            (CilOperation.Call, new CilOperand.None(), 0),
            (CilOperation.Pop, new CilOperand.None(), 1),
            (CilOperation.NewObject, new CilOperand.None(), 0),
            (CilOperation.Pop, new CilOperand.None(), 1),
            (CilOperation.Return, new CilOperand.None(), 0));

        var types = Analyzer(calls).Analyze(body.Graph);

        Assert.Equal([owner], types[1]);
        Assert.Equal([A], types[3]);
        Assert.Equal([null], types[5]);
        Assert.Equal([null], types[7]);
        Assert.Equal([A], types[9]);
        Assert.Equal([null], types[11]);
        Assert.Equal([null], types[13]);
        Assert.Equal([B], types[14]);
        Assert.Equal([A], types[16]);
        Assert.Equal([A, A], types[17]);
        Assert.Empty(types[20]);
        Assert.Equal([B], types[21]);
        Assert.Empty(types[24]);
        Assert.Equal([null], types[26]);
        Assert.Equal([B], types[28]);
        Assert.Equal([A], types[29]);
        Assert.Equal([null], types[31]);
        Assert.Equal([null], types[33]);
    }

    private static StaticReferenceTypeAnalyzer Analyzer(Dictionary<int, MethodInstanceModel> calls) =>
        new(new Calls(calls), new Fields());

    // A body whose instructions sit one offset apart, with each instruction's entry stack height.
    private static (CilMethodBody Body, ValidatedControlFlowGraph Graph) Body(
        MethodInstanceModel? method,
        params (CilOperation Operation, CilOperand Operand, int Height)[] instructions)
    {
        var body = new CilMethodBody(
            method?.Definition ?? Method(A, "Run", isStatic: true, CliTypeIdentity.FromStackKind(CliValueKind.Void)).Definition,
            2,
            [CliValueKind.ManagedReference, CliValueKind.ManagedReference],
            [.. instructions.Select((instruction, offset) =>
                new CilInstruction(offset, offset + 1, instruction.Operation, instruction.Operand))])
        {
            MethodInstance = method,
            LocalSignatureTypes = method is null ? [A, B] : [MethodParameter, TypeParameter],
        };
        var graph = new ControlFlowGraphBuilderFactory().Create().Build(body);
        return (body, new(
            graph,
            graph.Blocks.ToImmutableDictionary(block => block.Index, block => Stack(instructions[block.StartOffset].Height)),
            body.Instructions.ToImmutableDictionary(instruction => instruction.Offset,
                instruction => Stack(instructions[instruction.Offset].Height))));

        static ImmutableArray<CliValueKind> Stack(int height) =>
            [.. Enumerable.Repeat(CliValueKind.ManagedReference, height)];
    }

    private static MethodInstanceModel Method(
        CliTypeIdentity owner, string name, bool isStatic, CliTypeIdentity returnType, params CliTypeIdentity[] parameters)
    {
        var signature = new MethodSignatureModel(returnType, [.. parameters]);
        return new(
            new MethodDefinitionModel(new EntityKey(Assembly, 0x06000001), new EntityKey(Assembly, 2), name, isStatic, signature, 0),
            owner,
            [],
            signature);
    }

    private static FieldDefinitionModel Field() => new(FieldKey, new EntityKey(Assembly, 2), "Value", B, isStatic: false);

    private sealed class Calls(Dictionary<int, MethodInstanceModel> calls) : ICalledMethodResolver
    {
        public MethodInstanceModel? Resolve(CilInstruction instruction) => calls.GetValueOrDefault(instruction.Offset);
    }

    private sealed class Fields : IFieldRepository
    {
        public FieldDefinitionModel GetField(EntityKey key) => Field();
    }
}
