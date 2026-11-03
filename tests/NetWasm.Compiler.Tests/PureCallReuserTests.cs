using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

// A pure Math call made again on the same arguments takes the first call's
// result when nothing between the two changes what they read and no jump lands
// between them. The near misses keep both calls.
public sealed class PureCallReuserTests
{
    private static readonly AssemblyIdentity Assembly = new("fixture");
    private static readonly CliTypeIdentity Float64 = CliTypeIdentity.Primitive("f8", CliValueKind.F8);
    private static readonly MethodInstanceModel Sin = Method("System", "Math", "Sin", 0x06000002);
    private static readonly MethodInstanceModel Cos = Method("System", "Math", "Cos", 0x06000003);
    private static readonly MethodInstanceModel Sqrt = Method("System", "Math", "Sqrt", 0x06000004);
    private static readonly MethodInstanceModel Other = Method("Fixture", "Effects", "Next", 0x06000005);
    private static readonly CilOperand.FieldInstance Field = new(new(
        new FieldDefinitionModel(new(Assembly, 0x04000001), new(Assembly, 0x02000001), "x", CliValueKind.F8, IsStatic: true),
        CliTypeIdentity.Named(Assembly, "Fixture", "Statics", isValueType: false), Float64));

    public static TheoryData<string, int> Shapes() => new()
    {
        // The shape and how many calls the rewrite leaves.
        { "argument", 1 },
        { "static field", 1 },
        { "arithmetic", 1 },
        { "three times", 1 },
        { "nested", 2 },
        { "other local stored between", 1 },
        { "branch away between", 1 },
        { "different arguments", 2 },
        { "different method", 2 },
        { "local stored between", 2 },
        { "argument stored between", 2 },
        { "static field stored between", 2 },
        { "other call between", 3 },
        { "jump lands between", 2 },
        { "address taken", 2 },
        { "integer arithmetic", 2 },
    };

    [Theory]
    [MemberData(nameof(Shapes))]
    public void ReusesOnlyRepeatsWithUnchangedArguments(string shape, int calls)
    {
        var body = Shape(shape);

        var rewritten = new PureCallReuser(new OperandCalledMethodResolver(), enabled: true).Rewrite(body);

        Assert.Equal(calls, rewritten.Instructions.Count(static instruction => instruction.Operation == CilOperation.Call));
        Assert.Equal(calls == Calls(body), ReferenceEquals(body, rewritten));
        if (!ReferenceEquals(body, rewritten))
        {
            var local = Assert.Single(rewritten.Locals.Skip(body.Locals.Length));
            Assert.Equal(CliValueKind.F8, local);
            Assert.Contains(rewritten.Instructions, static instruction => instruction.Operation == CilOperation.Duplicate);
        }
        Assert.Same(body, new PureCallReuser(new OperandCalledMethodResolver(), enabled: false).Rewrite(body));
    }

    private static int Calls(CilMethodBody body) =>
        body.Instructions.Count(static instruction => instruction.Operation == CilOperation.Call);

    private static CilMethodBody Shape(string shape) => shape switch
    {
        "argument" => Body([Arg(0), Call(Sin), Arg(0), Call(Sin), I(CilOperation.Multiply)]),
        "static field" => Body([Static(), Call(Sin), Static(), Call(Sin), I(CilOperation.Multiply)]),
        "arithmetic" => Body([Arg(0), Arg(1), I(CilOperation.Divide), Call(Sin), Arg(0), Arg(1), I(CilOperation.Divide), Call(Sin),
            I(CilOperation.Add)]),
        "three times" => Body([Arg(0), Call(Sin), Arg(0), Call(Sin), I(CilOperation.Multiply), Arg(0), Call(Sin), I(CilOperation.Add)]),
        "nested" => Body([Arg(0), Call(Sin), Call(Sqrt), Arg(0), Call(Sin), Call(Sqrt), I(CilOperation.Add)]),
        "other local stored between" => Body([Arg(0), Call(Sin), Arg(1), I(CilOperation.StoreLocal, new CilOperand.Index(1)),
            Arg(0), Call(Sin), I(CilOperation.Multiply)]),
        "branch away between" => Body([Arg(0), Call(Sin), Arg(1), I(CilOperation.BranchIfTrue, new CilOperand.BranchTarget(-1)),
            Arg(0), Call(Sin), I(CilOperation.Multiply)]),
        "different arguments" => Body([Arg(0), Call(Sin), Arg(1), Call(Sin), I(CilOperation.Multiply)]),
        "different method" => Body([Arg(0), Call(Sin), Arg(0), Call(Cos), I(CilOperation.Multiply)]),
        "local stored between" => Body([Local(1), Call(Sin), Arg(0), I(CilOperation.StoreLocal, new CilOperand.Index(1)),
            Local(1), Call(Sin), I(CilOperation.Multiply)]),
        "argument stored between" => Body([Arg(0), Call(Sin), Arg(1), I(CilOperation.StoreArgument, new CilOperand.Index(0)),
            Arg(0), Call(Sin), I(CilOperation.Multiply)]),
        "static field stored between" => Body([Static(), Call(Sin), Arg(1), I(CilOperation.StoreStaticField, Field),
            Static(), Call(Sin), I(CilOperation.Multiply)]),
        "other call between" => Body([Static(), Call(Sin), Call(Other), I(CilOperation.Pop), Static(), Call(Sin), I(CilOperation.Multiply)]),
        "jump lands between" => Body([Arg(0), Call(Sin), Arg(0), Call(Sin), I(CilOperation.Multiply)], jumpTo: 2),
        "address taken" => Body([Local(1), Call(Sin), Local(1), Call(Sin), I(CilOperation.Multiply),
            I(CilOperation.LoadLocalAddress, new CilOperand.Index(1)), I(CilOperation.Pop)]),
        _ => Body([I(CilOperation.LoadInt32, 7), Local(0), I(CilOperation.Remainder), I(CilOperation.ConvertFloat64), Call(Sin),
            I(CilOperation.LoadInt32, 7), Local(0), I(CilOperation.Remainder), I(CilOperation.ConvertFloat64), Call(Sin),
            I(CilOperation.Multiply)]),
    };

    // The code, its value popped, then a return; a branch target of -1 is the
    // return, and jumpTo makes a jump at the start land on that instruction.
    private static CilMethodBody Body(CilInstruction[] code, int? jumpTo = null)
    {
        var all = new List<CilInstruction>();
        if (jumpTo is not null) all.Add(I(CilOperation.Branch));
        var start = all.Count;
        all.AddRange(code);
        all.Add(I(CilOperation.Pop));
        all.Add(I(CilOperation.Return));
        var end = all.Count - 1;
        if (jumpTo is { } target) all[0] = all[0] with { Operand = new CilOperand.BranchTarget(start + target) };
        var instructions = all.Select((instruction, index) => instruction with
        {
            Offset = index,
            NextOffset = index + 1,
            Operand = instruction.Operand is CilOperand.BranchTarget { Offset: -1 } ? new CilOperand.BranchTarget(end) : instruction.Operand,
        }).ToImmutableArray();
        var signature = MethodSignatureModel.Create(CliTypeIdentity.FromStackKind(CliValueKind.Void), Float64, Float64);
        var method = new MethodDefinitionModel(new(Assembly, 0x06000001), new(Assembly, 0x02000001), "Run", true, signature, 1);
        return new(method, 6, [CliValueKind.I4, CliValueKind.F8], instructions)
        {
            LocalSignatureTypes = [CliTypeIdentity.Primitive("i4", CliValueKind.I4), Float64],
        };
    }

    private static CilInstruction I(CilOperation operation, CilOperand? operand = null) =>
        new(0, 0, operation, operand ?? new CilOperand.None());

    private static CilInstruction I(CilOperation operation, int value) => I(operation, new CilOperand.ConstantI4(value));

    private static CilInstruction Arg(int index) => I(CilOperation.LoadArgument, new CilOperand.Index(index));

    private static CilInstruction Local(int index) => I(CilOperation.LoadLocal, new CilOperand.Index(index));

    private static CilInstruction Static() => I(CilOperation.LoadStaticField, Field);

    private static CilInstruction Call(MethodInstanceModel method) => I(CilOperation.Call, new CilOperand.MethodInstance(method));

    private static MethodInstanceModel Method(string ns, string type, string name, int token)
    {
        var signature = MethodSignatureModel.Create(Float64, Float64);
        var definition = new MethodDefinitionModel(new(Assembly, token), new(Assembly, 0x02000002), name, true, signature, 1);
        return new(definition, CliTypeIdentity.Named(Assembly, ns, type, isValueType: false), [], signature);
    }

    private sealed class OperandCalledMethodResolver : ICalledMethodResolver
    {
        public MethodInstanceModel? Resolve(CilInstruction instruction) =>
            instruction.Operand is CilOperand.MethodInstance method ? method.Value : null;
    }
}
