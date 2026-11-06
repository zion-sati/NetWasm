using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Analysis.Loops;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

// A branching computation of values the loop does not change moves ahead of
// the loop's entry jump into a new local, which the loop reads instead, also
// after a branch out of the loop. The near misses stay: no branch, a value the
// loop changes, a slot whose address is taken, an instruction that can throw, a
// temporary seen elsewhere or read before it is written, a jump into the
// middle, a lane outside the vector, a computation only some iterations run,
// a reference, and a loop entered from outside.
public sealed class LoopInvariantExpressionHoisterTests
{
    private static readonly AssemblyIdentity Assembly = new("fixture");
    private static readonly CliTypeIdentity Int32 = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
    private static readonly CliTypeIdentity Int64 = CliTypeIdentity.Primitive("i8", CliValueKind.I8);
    private static readonly CliTypeIdentity Float64 = CliTypeIdentity.Primitive("f8", CliValueKind.F8);
    private static readonly MethodInstanceModel GetElement = VectorElement();

    public static TheoryData<string, bool> Shapes() => new()
    {
        { "choice of arguments", true },
        { "choice through a temporary", true },
        { "floating division in an arm", true },
        { "vector lane search", true },
        { "choice after a conditional break", true },
        { "straight-line arithmetic", false },
        { "local the loop writes", false },
        { "loop counter", false },
        { "address-taken local", false },
        { "integer division in an arm", false },
        { "checked addition in an arm", false },
        { "array element in an arm", false },
        { "temporary read after the loop", true },
        { "temporary read before it is written", false },
        { "jump into the middle", false },
        { "lane outside the vector", false },
        { "vector read through a reference", false },
        { "jump into the body from outside", false },
        { "choice in a conditional arm", false },
    };

    [Theory]
    [MemberData(nameof(Shapes))]
    public void HoistsOnlyBranchingComputationsThatCannotThrowOfValuesTheLoopKeeps(string shape, bool hoisted)
    {
        var body = Shape(shape);

        var rewritten = Hoister(enabled: true).Rewrite(body);

        Assert.Equal(hoisted, !ReferenceEquals(body, rewritten));
        if (!hoisted) return;
        // The computation, branch included, now runs before the entry jump and
        // ends in the new local, which the loop reads.
        var instructions = rewritten.Instructions;
        var local = rewritten.Locals.Length - 1;
        var store = Store(rewritten, local);
        Assert.Equal(body.Locals.Length + 1, rewritten.Locals.Length);
        Assert.Equal(CilOperation.Branch, instructions[store + 1].Operation);
        Assert.Contains(instructions.Take(store), static instruction => instruction.Operand is CilOperand.BranchTarget);
        Assert.Contains(instructions.Skip(store + 2), instruction =>
            instruction.Operation == CilOperation.LoadLocal && instruction.Operand == new CilOperand.Index(local));
        if (shape == "temporary read after the loop")
        {
            // The temporary the loop's end reads keeps its store in the loop.
            Assert.Contains(instructions.Skip(store + 2), static instruction =>
                instruction.Operation == CilOperation.StoreLocal && instruction.Operand == new CilOperand.Index(2));
        }
    }

    [Fact]
    public void LeavesTheBodyAloneWhenDisabled()
    {
        var body = Shape("choice of arguments");

        Assert.Same(body, Hoister(enabled: false).Rewrite(body));
    }

    // The hoisted branches land inside the copy, and a branch past its end on the store that keeps its value.
    [Fact]
    public void BranchesOfTheCopyLandOnTheCopy()
    {
        var rewritten = Hoister(enabled: true).Rewrite(Shape("choice of arguments"));
        var instructions = rewritten.Instructions;
        var store = Store(rewritten, rewritten.Locals.Length - 1);
        // The copy holds both arguments at once on top of the entry jump's stack.
        Assert.Equal(4 + 2, rewritten.MaxStack);

        Assert.All(instructions.Take(store).Where(static instruction => instruction.Operand is CilOperand.BranchTarget),
            instruction => Assert.InRange(((CilOperand.BranchTarget)instruction.Operand).Offset, instruction.Offset + 1, store));
    }

    private static int Store(CilMethodBody body, int local) => body.Instructions.ToList().FindIndex(instruction =>
        instruction.Operation == CilOperation.StoreLocal && instruction.Operand == new CilOperand.Index(local));

    private static LoopInvariantExpressionHoister Hoister(bool enabled) =>
        new(new OperandCalledMethodResolver(), new Intrinsics(GetElement.Definition.Key, RuntimeIntrinsic.SimdGetElement), enabled);

    // Each body leaves one value that the loop adds to local 1; local 2 is a
    // temporary, local 3 a local the loop leaves alone, local 4 a vector,
    // local 5 a long temporary and local 6 a reference to a vector, whose
    // elements the loop could change through another reference.
    private static CilMethodBody Shape(string shape) => shape switch
    {
        "choice of arguments" => Loop(Choice(Arg(0), Arg(1))),
        "choice through a temporary" => Loop([Arg(0), Arg(1), Branch(CilOperation.BranchIfGreaterThanSigned, 5), Arg(1),
            Branch(CilOperation.Branch, 6), Arg(0), Store(2), Local(2)]),
        "floating division in an arm" => Loop(Choice([Arg(0), I(CilOperation.ConvertFloat64), F8(2), I(CilOperation.Divide),
            I(CilOperation.ConvertInt32)], [Arg(1)])),
        "vector lane search" => Loop([Local(4), I(CilOperation.LoadInt32, 0), Call(GetElement), Store(5), Local(5),
            Branch(CilOperation.BranchIfTrue, 10), Local(4), I(CilOperation.LoadInt32, 1), Call(GetElement), Store(5), Local(5),
            I(CilOperation.ConvertInt32)]),
        "straight-line arithmetic" => Loop([Arg(0), Arg(1), I(CilOperation.Add)]),
        "local the loop writes" => Loop(Choice(Local(3), Arg(1)), tail: [Local(3), I(CilOperation.LoadInt32, 1),
            I(CilOperation.Add), Store(3)]),
        "loop counter" => Loop(Choice(Local(0), Arg(1))),
        "address-taken local" => Loop(Choice(Local(3), Arg(1)), [I(CilOperation.LoadLocalAddress, new CilOperand.Index(3)),
            I(CilOperation.Pop)]),
        "integer division in an arm" => Loop(Choice([Arg(0), Arg(1), I(CilOperation.Divide)], [Arg(1)])),
        "checked addition in an arm" => Loop(Choice([Arg(0), Arg(1), I(CilOperation.AddChecked)], [Arg(1)])),
        "array element in an arm" => Loop(Choice([Arg(2), I(CilOperation.LoadInt32, 0),
            I(CilOperation.LoadArrayElement, new CilOperand.TypeIdentity(Int32))], [Arg(1)])),
        "temporary read after the loop" => Loop([Arg(0), Arg(1), Branch(CilOperation.BranchIfGreaterThanSigned, 5), Arg(1),
            Branch(CilOperation.Branch, 6), Arg(0), Store(2), Local(2)], after: [Local(2), I(CilOperation.Pop)]),
        "temporary read before it is written" => Loop([Local(2), Arg(1), Branch(CilOperation.BranchIfGreaterThanSigned, 5),
            Arg(1), Branch(CilOperation.Branch, 6), Arg(0), Store(2), Local(2)]),
        "jump into the middle" => Loop(Choice(Arg(0), Arg(1)), jumpIntoRegion: true),
        "vector read through a reference" => Loop([Local(6), I(CilOperation.LoadInt32, 0), Call(GetElement), Store(5),
            Local(5), Branch(CilOperation.BranchIfTrue, 10), Local(6), I(CilOperation.LoadInt32, 1), Call(GetElement), Store(5),
            Local(5), I(CilOperation.ConvertInt32)]),
        "jump into the body from outside" => Loop(Choice(Arg(0), Arg(1)), entry: true),
        // i > 5 ? (a > b ? a : b) : 0
        "choice in a conditional arm" => Loop([Local(0), I(CilOperation.LoadInt32, 5),
            Branch(CilOperation.BranchIfLessThanOrEqualSigned, 10), .. Shifted(Choice(Arg(0), Arg(1)), 3),
            Branch(CilOperation.Branch, 11), I(CilOperation.LoadInt32, 0)]),
        // if (i > 5) break; a > b ? a : b, where position 19 is the load of the total after the loop.
        "choice after a conditional break" => Loop([Local(0), I(CilOperation.LoadInt32, 5),
            Branch(CilOperation.BranchIfGreaterThanSigned, 19), .. Shifted(Choice(Arg(0), Arg(1)), 3)]),
        "lane outside the vector" => Loop([Local(4), I(CilOperation.LoadInt32, 2), Call(GetElement), Store(5), Local(5),
            Branch(CilOperation.BranchIfTrue, 10), Local(4), I(CilOperation.LoadInt32, 1), Call(GetElement), Store(5), Local(5),
            I(CilOperation.ConvertInt32)]),
        _ => throw new ArgumentOutOfRangeException(nameof(shape)),
    };

    // a > b ? first : second, as Roslyn compiles it: the targets are body
    // positions, resolved by Loop.
    private static CilInstruction[] Choice(CilInstruction[] first, CilInstruction[] second)
    {
        var code = new List<CilInstruction> { Arg(0), Arg(1) };
        var test = code.Count;
        code.Add(I(CilOperation.BranchIfGreaterThanSigned));
        code.AddRange(second);
        var skip = code.Count;
        code.Add(I(CilOperation.Branch));
        var firstStart = code.Count;
        code.AddRange(first);
        code[test] = Branch(CilOperation.BranchIfGreaterThanSigned, firstStart);
        code[skip] = Branch(CilOperation.Branch, code.Count);
        return [.. code];
    }

    private static CilInstruction[] Choice(CilInstruction first, CilInstruction second) => Choice([first], [second]);

    // Code whose branches name its own positions, placed at a body position.
    private static IEnumerable<CilInstruction> Shifted(CilInstruction[] code, int by) =>
        code.Select(instruction => instruction.Operand is CilOperand.BranchTarget { Offset: var position }
            ? instruction with { Operand = new CilOperand.BranchTarget(position + by) }
            : instruction);

    // [if (arg 0) goto start;] i = 0; goto test; start: [extra]; total += body; [tail]; i = i + 1;
    // test: if (i < 10) goto start; [after]; return. A branch in the body names a
    // body position, offset by the body's start.
    private static CilMethodBody Loop(
        CilInstruction[] body,
        CilInstruction[]? extra = null,
        CilInstruction[]? after = null,
        bool jumpIntoRegion = false,
        CilInstruction[]? tail = null,
        bool entry = false)
    {
        extra ??= [];
        var code = new List<CilInstruction>();
        if (entry)
        {
            code.AddRange([Arg(0), Branch(CilOperation.BranchIfTrue, -1)]);
        }
        code.AddRange([I(CilOperation.LoadInt32, 0), Store(0)]);
        var jump = code.Count;
        code.Add(I(CilOperation.Branch));
        var start = code.Count;
        if (jumpIntoRegion)
        {
            // A jump over the first two instructions of the body lands inside the expression.
            code.Add(Arg(0));
            code.Add(Branch(CilOperation.BranchIfTrue, -1));
        }
        code.AddRange(extra);
        var bodyStart = code.Count;
        code.AddRange(body.Select(instruction => instruction.Operand is CilOperand.BranchTarget { Offset: var position }
            ? instruction with { Operand = new CilOperand.BranchTarget(bodyStart + position) }
            : instruction));
        if (jumpIntoRegion)
        {
            code[start + 1] = Branch(CilOperation.BranchIfTrue, bodyStart + 2);
        }
        if (entry)
        {
            code[1] = Branch(CilOperation.BranchIfTrue, start);
        }
        code.AddRange([Local(1), I(CilOperation.Add), Store(1)]);
        code.AddRange(tail ?? []);
        code.AddRange([Local(0), I(CilOperation.LoadInt32, 1), I(CilOperation.Add), Store(0)]);
        var test = code.Count;
        code.AddRange([Local(0), I(CilOperation.LoadInt32, 10), Branch(CilOperation.BranchIfLessThanSigned, start)]);
        code.AddRange(after ?? []);
        code.Add(Local(1));
        code.Add(I(CilOperation.Return));
        code[jump] = Branch(CilOperation.Branch, test);
        var instructions = code.Select(static (instruction, index) => instruction with { Offset = index, NextOffset = index + 1 })
            .ToImmutableArray();
        var signature = MethodSignatureModel.Create(Int32, Int32, Int32, CliTypeIdentity.SzArray(Int32));
        var method = new MethodDefinitionModel(new(Assembly, 0x06000001), new(Assembly, 0x02000001), "Run", true, signature, 1);
        var vector = CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.Named(Assembly, "System.Numerics", "Vector`1", isValueType: true), [Int64]);
        return new(method, 4, [CliValueKind.I4, CliValueKind.I4, CliValueKind.I4, CliValueKind.I4, CliValueKind.V128,
            CliValueKind.I8, CliValueKind.ManagedAddress], instructions)
        {
            LocalSignatureTypes = [Int32, Int32, Int32, Int32, vector, Int64, CliTypeIdentity.ManagedByReference(vector)],
        };
    }

    private static CilInstruction I(CilOperation operation, CilOperand? operand = null) =>
        new(0, 0, operation, operand ?? new CilOperand.None());

    private static CilInstruction I(CilOperation operation, int value) => I(operation, new CilOperand.ConstantI4(value));

    private static CilInstruction F8(double value) => I(CilOperation.LoadFloat64, new CilOperand.ConstantF8(value));

    private static CilInstruction Branch(CilOperation operation, int target) => I(operation, new CilOperand.BranchTarget(target));

    private static CilInstruction Arg(int index) => I(CilOperation.LoadArgument, new CilOperand.Index(index));

    private static CilInstruction Local(int index) => I(CilOperation.LoadLocal, new CilOperand.Index(index));

    private static CilInstruction Store(int index) => I(CilOperation.StoreLocal, new CilOperand.Index(index));

    private static CilInstruction Call(MethodInstanceModel method) => I(CilOperation.Call, new CilOperand.MethodInstance(method));

    // Vector<long>.get_Item(int).
    private static MethodInstanceModel VectorElement()
    {
        var vector = CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.Named(Assembly, "System.Numerics", "Vector`1", isValueType: true), [Int64]);
        var signature = MethodSignatureModel.Create(Int64, Int32);
        var definition = new MethodDefinitionModel(new(Assembly, 0x06000003), new(Assembly, 0x02000003), "get_Item", false,
            signature, 1);
        return new(definition, vector, [], signature);
    }

    private sealed class OperandCalledMethodResolver : ICalledMethodResolver
    {
        public MethodInstanceModel? Resolve(CilInstruction instruction) =>
            instruction.Operand is CilOperand.MethodInstance method ? method.Value : null;
    }

    private sealed class Intrinsics(EntityKey method, RuntimeIntrinsic value) : IRuntimeIntrinsicRegistry
    {
        public bool TryGetIntrinsic(EntityKey key, out RuntimeIntrinsic intrinsic)
        {
            intrinsic = value;
            return key == method;
        }
    }
}
