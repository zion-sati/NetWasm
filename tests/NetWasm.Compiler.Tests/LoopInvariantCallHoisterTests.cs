using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Analysis.Loops;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

// A pure Math call of values the loop does not change moves ahead of the loop's
// entry jump; its arguments may combine constants, unwritten locals and
// arguments, floating division and other pure calls. The near misses stay.
public sealed class LoopInvariantCallHoisterTests
{
    private static readonly AssemblyIdentity Assembly = new("fixture");
    private static readonly CliTypeIdentity Float64 = CliTypeIdentity.Primitive("f8", CliValueKind.F8);
    private static readonly CliTypeIdentity Float32 = CliTypeIdentity.Primitive("f4", CliValueKind.F4);
    private static readonly MethodInstanceModel Sqrt = Method("System", "Math", "Sqrt", Float64);
    private static readonly MethodInstanceModel SqrtF = Method("System", "MathF", "Sqrt", Float32);

    public static TheoryData<string, bool> Shapes() => new()
    {
        { "argument", true },
        { "floating division of a local", true },
        { "floating division of a float constant", true },
        { "floating division of a converted value", true },
        { "floating division of a double conversion", true },
        { "floating division of an argument", true },
        { "floating division of a pure call", true },
        { "floating division of a sum", true },
        { "floating division of a quotient", true },
        { "nested pure call", true },
        { "single precision", true },
        { "instance method argument", true },
        { "integer division", false },
        { "division of an unknown local", false },
        { "division of a missing argument", false },
        { "written argument", false },
        { "arguments from before the loop", false },
    };

    [Theory]
    [MemberData(nameof(Shapes))]
    public void HoistsOnlyPureCallsOfLoopInvariantValues(string shape, bool hoisted)
    {
        var body = Shape(shape);

        var rewritten = new LoopInvariantCallHoister(new OperandCalledMethodResolver(), enabled: true).Rewrite(body);

        Assert.Equal(hoisted, !ReferenceEquals(body, rewritten));
        if (hoisted)
        {
            var jump = rewritten.Instructions.ToList().FindIndex(static instruction => instruction.Operation == CilOperation.Branch);
            Assert.Contains(rewritten.Instructions.Take(jump), static instruction => instruction.Operation == CilOperation.Call);
        }
    }

    private static CilMethodBody Shape(string shape) => shape switch
    {
        "argument" => Loop([Arg(0), Call(Sqrt)]),
        "floating division of a local" => Loop([Local(1), Local(1), I(CilOperation.Divide), Call(Sqrt)]),
        "floating division of a float constant" => Loop([F4(2), F4(3), I(CilOperation.Divide), I(CilOperation.ConvertFloat64), Call(Sqrt)]),
        "floating division of a converted value" => Loop([F4(3), Arg(0), I(CilOperation.ConvertFloat32), I(CilOperation.Divide),
            I(CilOperation.ConvertFloat64), Call(Sqrt)]),
        "floating division of a double conversion" => Loop([Arg(0), Arg(0), I(CilOperation.ConvertFloat64), I(CilOperation.Divide), Call(Sqrt)]),
        "floating division of an argument" => Loop([Arg(0), Arg(0), I(CilOperation.Divide), Call(Sqrt)]),
        "floating division of a pure call" => Loop([Arg(0), Arg(0), Call(Sqrt), I(CilOperation.Divide), Call(Sqrt)]),
        "floating division of a sum" => Loop([Arg(0), Arg(0), Arg(0), I(CilOperation.Add), I(CilOperation.Negate), I(CilOperation.Divide), Call(Sqrt)]),
        "floating division of a quotient" => Loop([Arg(0), Arg(0), Arg(0), I(CilOperation.Divide), I(CilOperation.Divide), Call(Sqrt)]),
        "nested pure call" => Loop([Arg(0), Call(Sqrt), Call(Sqrt)]),
        "single precision" => Loop([F4(2), Call(SqrtF), I(CilOperation.ConvertFloat64)]),
        "instance method argument" => Loop([Arg(1), Arg(1), I(CilOperation.Divide), Call(Sqrt)], isStatic: false),
        "integer division" => Loop([I(CilOperation.LoadInt32, 6), I(CilOperation.LoadInt32, 3), I(CilOperation.Divide),
            I(CilOperation.ConvertFloat64), Call(Sqrt)]),
        "division of an unknown local" => Loop([Local(1), Local(9), I(CilOperation.Divide), Call(Sqrt)]),
        "division of a missing argument" => Loop([Arg(0), Arg(5), I(CilOperation.Divide), Call(Sqrt)]),
        "written argument" => Loop([Arg(0), Call(Sqrt), I(CilOperation.StoreArgument, new CilOperand.Index(0))]),
        _ => Loop([Call(Sqrt)]),
    };

    // i = 0; goto test; start: body; pop; i = i + 1; test: if (i < 10) goto start; return.
    private static CilMethodBody Loop(CilInstruction[] body, bool isStatic = true)
    {
        var code = new List<CilInstruction> { I(CilOperation.LoadInt32, 0), I(CilOperation.StoreLocal, new CilOperand.Index(0)) };
        var jump = code.Count;
        code.Add(I(CilOperation.Branch));
        var start = code.Count;
        code.AddRange(body);
        code.Add(I(CilOperation.Pop));
        code.AddRange([Local(0), I(CilOperation.LoadInt32, 1), I(CilOperation.Add), I(CilOperation.StoreLocal, new CilOperand.Index(0))]);
        var test = code.Count;
        code.AddRange([Local(0), I(CilOperation.LoadInt32, 10), new(0, 0, CilOperation.BranchIfLessThanSigned, new CilOperand.BranchTarget(start))]);
        code.Add(I(CilOperation.Return));
        code[jump] = code[jump] with { Operand = new CilOperand.BranchTarget(test) };
        var instructions = code.Select(static (instruction, index) => instruction with { Offset = index, NextOffset = index + 1 })
            .ToImmutableArray();
        var signature = MethodSignatureModel.Create(CliTypeIdentity.FromStackKind(CliValueKind.Void), Float64, Float64);
        var method = new MethodDefinitionModel(new(Assembly, 0x06000001), new(Assembly, 0x02000001), "Run", isStatic, signature, 1);
        return new(method, 4, [CliValueKind.I4, CliValueKind.F8], instructions)
        {
            LocalSignatureTypes = [CliTypeIdentity.Primitive("i4", CliValueKind.I4), Float64],
        };
    }

    private static CilInstruction I(CilOperation operation, CilOperand? operand = null) =>
        new(0, 0, operation, operand ?? new CilOperand.None());

    private static CilInstruction I(CilOperation operation, int value) => I(operation, new CilOperand.ConstantI4(value));

    private static CilInstruction F4(float value) => I(CilOperation.LoadFloat32, new CilOperand.ConstantF4(value));

    private static CilInstruction Arg(int index) => I(CilOperation.LoadArgument, new CilOperand.Index(index));

    private static CilInstruction Local(int index) => I(CilOperation.LoadLocal, new CilOperand.Index(index));

    private static CilInstruction Call(MethodInstanceModel method) => I(CilOperation.Call, new CilOperand.MethodInstance(method));

    private static MethodInstanceModel Method(string ns, string type, string name, CliTypeIdentity value)
    {
        var signature = MethodSignatureModel.Create(value, value);
        var definition = new MethodDefinitionModel(new(Assembly, 0x06000002), new(Assembly, 0x02000002), name, true, signature, 1);
        return new(definition, CliTypeIdentity.Named(Assembly, ns, type, isValueType: false), [], signature);
    }

    private sealed class OperandCalledMethodResolver : ICalledMethodResolver
    {
        public MethodInstanceModel? Resolve(CilInstruction instruction) =>
            instruction.Operand is CilOperand.MethodInstance method ? method.Value : null;
    }
}
