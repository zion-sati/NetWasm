using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Analysis.Loops;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

// Loop shapes Roslyn does not emit but CIL allows, and those other passes leave
// in place before the vectorizer sees them: constants and calls inlining
// exposes, loops entered with values on the stack, and bounds and bodies the
// vector form cannot keep. ElementwiseLoopVectorizerCorpusTests covers the
// shapes C# writes, against desktop .NET.
public sealed class ElementwiseLoopVectorizerTests
{
    private static readonly AssemblyIdentity Assembly = new("fixture");
    private static readonly CliTypeIdentity Int32 = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
    private static readonly CliTypeIdentity Float64 = CliTypeIdentity.Primitive("f8", CliValueKind.F8);

    public static TheoryData<string> VectorizedShapes() =>
    [
        "negated constant", "complemented constant", "and of constants", "or of constants", "xor of constants",
        "pure call of an argument", "double of an argument", "scaled counter copy", "constant times the counter",
        "argument plus an element", "constant plus an argument", "double argument halved",
    ];

    [Theory]
    [MemberData(nameof(VectorizedShapes))]
    public void ShapeRunsAsVectors(string shape)
    {
        var body = Shape(shape);

        var rewritten = Vectorizer().Rewrite(body);

        Assert.NotSame(body, rewritten);
        Assert.Contains(rewritten.Instructions, static instruction => instruction.Operand is CilOperand.MethodInstance
        {
            Value.Definition.Name: "StoreUnsafe",
        });
    }

    public static TheoryData<string> ScalarShapes() =>
    [
        "no bound", "bound reads the counter", "bound subtracts nothing", "bound length of nothing",
        "bound pushes two values", "inclusive bound of an argument", "inclusive bound of int.MaxValue",
        "store of nothing", "add of nothing", "shift of nothing", "shift by an argument", "negate of nothing",
        "convert of nothing", "counter divided", "value left on the stack",
        "call without its arguments", "call of an element", "impure call", "argument halved",
        "store read at another offset", "element of a double constant", "complemented double element",
        "xor of double elements", "shifted double element",
    ];

    [Theory]
    [MemberData(nameof(ScalarShapes))]
    public void ShapeStaysScalar(string shape)
    {
        var body = Shape(shape);

        Assert.Same(body, Vectorizer().Rewrite(body));
    }

    private static ElementwiseLoopVectorizer Vectorizer() => new(new FakeVectorMethods(), new OperandCalledMethodResolver(), true);

    // a[i] = b[i] + value, over int arrays a and b in arguments 0 and 1.
    private static CilMethodBody Shape(string shape) => shape switch
    {
        "negated constant" => IntLoop([I(CilOperation.LoadInt32, 5), I(CilOperation.Negate)]),
        "complemented constant" => IntLoop([I(CilOperation.LoadInt32, 5), I(CilOperation.OnesComplement)]),
        "and of constants" => IntLoop([I(CilOperation.LoadInt32, 0xF0), I(CilOperation.LoadInt32, 0x3C), I(CilOperation.BitwiseAnd)]),
        "or of constants" => IntLoop([I(CilOperation.LoadInt32, 0xF0), I(CilOperation.LoadInt32, 0x0F), I(CilOperation.BitwiseOr)]),
        "xor of constants" => IntLoop([I(CilOperation.LoadInt32, 0xF0), I(CilOperation.LoadInt32, 0xFF), I(CilOperation.BitwiseXor)]),
        "pure call of an argument" => DoubleLoop([Arg(2), Call(Sqrt)]),
        "double of an argument" => DoubleLoop([Arg(3), I(CilOperation.ConvertFloat64)]),
        "scaled counter copy" => Loop(
            [Counter(), I(CilOperation.LoadInt32, 3), I(CilOperation.Multiply), I(CilOperation.StoreLocal, new CilOperand.Index(1)),
                .. IntBody([I(CilOperation.LoadLocal, new CilOperand.Index(1))])], Bound()),
        "constant times the counter" => Loop(
            [I(CilOperation.LoadInt32, 3), Counter(), I(CilOperation.Multiply), I(CilOperation.StoreLocal, new CilOperand.Index(1)),
                .. IntBody([I(CilOperation.LoadLocal, new CilOperand.Index(1))])], Bound()),
        // a[i] = arg3 + b[i]: the argument's kind is the element's.
        "argument plus an element" => Loop(
            [Arg(0), Counter(), Arg(3), Arg(1), Counter(), I(CilOperation.LoadArrayElement, new CilOperand.TypeIdentity(Int32)),
                I(CilOperation.Add), I(CilOperation.StoreArrayElement, new CilOperand.TypeIdentity(Int32))], Bound()),
        "constant plus an argument" => IntLoop([I(CilOperation.LoadInt32, 1), Arg(3), I(CilOperation.Add)]),
        "double argument halved" => DoubleLoop([Arg(3), I(CilOperation.ConvertFloat64), Arg(2), I(CilOperation.Divide)]),
        // An integer division can throw, so it stays in the iterations.
        "argument halved" => IntLoop([Arg(3), I(CilOperation.LoadInt32, 2), I(CilOperation.Divide)]),
        // a[i] = a[i + 1] + 1 reads what the previous vector would have written.
        "store read at another offset" => Loop(
            [Arg(0), Counter(), Arg(0), Counter(), I(CilOperation.LoadInt32, 1), I(CilOperation.Add),
                I(CilOperation.LoadArrayElement, new CilOperand.TypeIdentity(Int32)), I(CilOperation.LoadInt32, 1), I(CilOperation.Add),
                I(CilOperation.StoreArrayElement, new CilOperand.TypeIdentity(Int32))], Bound()),
        "element of a double constant" => DoubleLoop([I(CilOperation.LoadFloat64, new CilOperand.ConstantF8(1)), Counter(),
            I(CilOperation.LoadArrayElement, new CilOperand.TypeIdentity(Float64))]),
        "complemented double element" => DoubleLoop([Arg(1), Counter(), I(CilOperation.LoadArrayElement, new CilOperand.TypeIdentity(Float64)),
            I(CilOperation.OnesComplement)]),
        "xor of double elements" => DoubleElements(CilOperation.BitwiseXor),
        "shifted double element" => Loop(
            [Arg(0), Counter(), Arg(1), Counter(), I(CilOperation.LoadArrayElement, new CilOperand.TypeIdentity(Float64)),
                I(CilOperation.LoadInt32, 2), I(CilOperation.ShiftLeft), I(CilOperation.StoreArrayElement, new CilOperand.TypeIdentity(Float64))],
            Bound()),
        "no bound" => Loop(IntBody([I(CilOperation.LoadInt32, 1)]), []),
        "bound reads the counter" => Loop(IntBody([I(CilOperation.LoadInt32, 1)]), [Counter()]),
        "bound subtracts nothing" => Loop(IntBody([I(CilOperation.LoadInt32, 1)]), [I(CilOperation.Subtract)]),
        "bound length of nothing" => Loop(IntBody([I(CilOperation.LoadInt32, 1)]), [I(CilOperation.LoadArrayLength)]),
        "bound pushes two values" => Loop(IntBody([I(CilOperation.LoadInt32, 1)]),
            [I(CilOperation.LoadInt32, 5), I(CilOperation.LoadInt32, 6)]),
        "inclusive bound of an argument" => Loop(IntBody([I(CilOperation.LoadInt32, 1)]), [Arg(3)],
            CilOperation.BranchIfLessThanOrEqualSigned),
        "inclusive bound of int.MaxValue" => Loop(IntBody([I(CilOperation.LoadInt32, 1)]), [I(CilOperation.LoadInt32, int.MaxValue)],
            CilOperation.BranchIfLessThanOrEqualSigned),
        "store of nothing" => Loop([I(CilOperation.StoreArrayElement, new CilOperand.TypeIdentity(Int32))], Bound()),
        "add of nothing" => Loop([I(CilOperation.Add), .. IntBody([])], Bound()),
        "shift of nothing" => Loop([I(CilOperation.ShiftLeft), .. IntBody([])], Bound()),
        "shift by an argument" => IntLoop([I(CilOperation.LoadInt32, 1), Arg(3), I(CilOperation.ShiftLeft)]),
        "negate of nothing" => Loop([I(CilOperation.Negate), .. IntBody([])], Bound()),
        "convert of nothing" => Loop([I(CilOperation.ConvertFloat32), .. IntBody([])], Bound()),
        "counter divided" => IntLoop([Counter(), I(CilOperation.LoadInt32, 2), I(CilOperation.Divide)]),
        "value left on the stack" => Loop([I(CilOperation.LoadInt32, 7), .. IntBody([I(CilOperation.LoadInt32, 1)])], Bound()),
        "call without its arguments" => Loop([Call(Sqrt), .. IntBody([])], Bound()),
        "call of an element" => DoubleLoop([Arg(1), Counter(), I(CilOperation.LoadArrayElement, new CilOperand.TypeIdentity(Float64)), Call(Sqrt)]),
        _ => DoubleLoop([Arg(2), Call(Impure)]),
    };

    private static readonly MethodInstanceModel Sqrt = Method("System", "Math", "Sqrt", Float64, Float64);
    private static readonly MethodInstanceModel Impure = Method("Fixture", "Owner", "Next", Float64, Float64);

    private static CilMethodBody IntLoop(CilInstruction[] value) => Loop(IntBody(value), Bound());

    // a[i] = b[i] + value.
    private static CilInstruction[] IntBody(CilInstruction[] value) =>
    [
        Arg(0), Counter(), Arg(1), Counter(), I(CilOperation.LoadArrayElement, new CilOperand.TypeIdentity(Int32)),
        .. value, .. value.Length == 0 ? Array.Empty<CilInstruction>() : [I(CilOperation.Add)],
        I(CilOperation.StoreArrayElement, new CilOperand.TypeIdentity(Int32)),
    ];

    // a[i] = b[i] * value, over double arrays.
    private static CilMethodBody DoubleLoop(CilInstruction[] value) => Loop(
    [
        Arg(0), Counter(), Arg(1), Counter(), I(CilOperation.LoadArrayElement, new CilOperand.TypeIdentity(Float64)),
        .. value, I(CilOperation.Multiply), I(CilOperation.StoreArrayElement, new CilOperand.TypeIdentity(Float64)),
    ], Bound());

    // a[i] = b[i] op b[i], over double arrays.
    private static CilMethodBody DoubleElements(CilOperation operation) => Loop(
    [
        Arg(0), Counter(), Arg(1), Counter(), I(CilOperation.LoadArrayElement, new CilOperand.TypeIdentity(Float64)),
        Arg(1), Counter(), I(CilOperation.LoadArrayElement, new CilOperand.TypeIdentity(Float64)), I(operation),
        I(CilOperation.StoreArrayElement, new CilOperand.TypeIdentity(Float64)),
    ], Bound());

    private static CilInstruction[] Bound() => [Arg(0), I(CilOperation.LoadArrayLength), I(CilOperation.ConvertInt32)];

    // i = 0; goto test; start: body; i = i + 1; test: if (i < bound) goto start; return.
    private static CilMethodBody Loop(
        CilInstruction[] body,
        CilInstruction[] bound,
        CilOperation branch = CilOperation.BranchIfLessThanSigned)
    {
        var code = new List<CilInstruction> { I(CilOperation.LoadInt32, 0), I(CilOperation.StoreLocal, new CilOperand.Index(0)) };
        var jump = code.Count;
        code.Add(I(CilOperation.Branch));
        var start = code.Count;
        code.AddRange(body);
        code.AddRange([Counter(), I(CilOperation.LoadInt32, 1), I(CilOperation.Add), I(CilOperation.StoreLocal, new CilOperand.Index(0))]);
        var test = code.Count;
        code.Add(Counter());
        code.AddRange(bound);
        code.Add(new(0, 0, branch, new CilOperand.BranchTarget(start)));
        code.Add(I(CilOperation.Return));
        code[jump] = code[jump] with { Operand = new CilOperand.BranchTarget(test) };
        var instructions = code.Select(static (instruction, index) => instruction with { Offset = index, NextOffset = index + 1 })
            .ToImmutableArray();
        var method = new MethodDefinitionModel(new(Assembly, 0x06000001), new(Assembly, 0x02000001), "Run", true,
            MethodSignatureModel.Create(CliValueKind.Void), 1);
        return new(method, 4, [CliValueKind.I4, CliValueKind.I4], instructions)
        {
            LocalSignatureTypes = [Int32, Int32],
        };
    }

    private static CilInstruction I(CilOperation operation, CilOperand? operand = null) =>
        new(0, 0, operation, operand ?? new CilOperand.None());

    private static CilInstruction I(CilOperation operation, int value) => I(operation, new CilOperand.ConstantI4(value));

    private static CilInstruction Arg(int index) => I(CilOperation.LoadArgument, new CilOperand.Index(index));

    private static CilInstruction Counter() => I(CilOperation.LoadLocal, new CilOperand.Index(0));

    private static CilInstruction Call(MethodInstanceModel method) => I(CilOperation.Call, new CilOperand.MethodInstance(method));

    private static MethodInstanceModel Method(string ns, string type, string name, CliTypeIdentity result, CliTypeIdentity parameter)
    {
        var signature = MethodSignatureModel.Create(result, parameter);
        var definition = new MethodDefinitionModel(new(Assembly, 0x06000002), new(Assembly, 0x02000002), name, true, signature, 1);
        return new(definition, CliTypeIdentity.Named(Assembly, ns, type, isValueType: false), [], signature);
    }

    // Vector128 methods by name, each returning a vector of the lane.
    private sealed class FakeVectorMethods : IVectorLoopMethodResolver
    {
        public MethodInstanceModel Resolve(VectorLoopMethod method, CliTypeIdentity lane)
        {
            var vector = CliTypeIdentity.GenericInstantiation(
                CliTypeIdentity.Named(Assembly, "System.Runtime.Intrinsics", "Vector128`1", isValueType: true), [lane]);
            var signature = MethodSignatureModel.Create(vector);
            var definition = new MethodDefinitionModel(new(Assembly, 0x06000003), new(Assembly, 0x02000003), method.Name, true, signature, 1);
            return new(definition, vector, [], signature);
        }
    }

    private sealed class OperandCalledMethodResolver : ICalledMethodResolver
    {
        public MethodInstanceModel? Resolve(CilInstruction instruction) =>
            instruction.Operand is CilOperand.MethodInstance method ? method.Value : null;
    }
}
