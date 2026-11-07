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
// a reference, and a loop entered from outside. So do runs whose branches go
// backward or arrive with different stacks, a value that is not a scalar, and
// instructions with nothing to work on.
public sealed class LoopInvariantExpressionHoisterTests
{
    private static readonly AssemblyIdentity Assembly = new("fixture");
    private static readonly CliTypeIdentity Int32 = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
    private static readonly CliTypeIdentity Int64 = CliTypeIdentity.Primitive("i8", CliValueKind.I8);
    private static readonly CliTypeIdentity Float64 = CliTypeIdentity.Primitive("f8", CliValueKind.F8);
    private static readonly MethodInstanceModel GetElement = VectorElement();
    private static readonly MethodInstanceModel SquareRoot = MathSquareRoot();
    private static readonly MethodInstanceModel AsInt64 = VectorReinterpretation();

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
        { "long choice", true },
        { "float choice", true },
        { "double choice", true },
        { "duplicated argument in an arm", true },
        { "negated argument in an arm", true },
        { "comparison in an arm", true },
        { "long operand in an arm", true },
        { "floating operand first in an arm", true },
        { "two tests for one arm", true },
        { "square root in an arm", true },
        { "reinterpreted vector in an arm", true },
        { "switch ahead of the choice", true },
        { "choice of vectors", false },
        { "straight-line duplicate and comparison", false },
        { "jump back to the body's start", false },
        { "conditional jump back to the body's start", false },
        { "jump with a different stack", false },
        { "conditional jump with a different stack", false },
        { "jump back into the choice", false },
        { "both arms jump to the end", true },
    };

    [Theory]
    [InlineData("long choice", "primitive:i8")]
    [InlineData("float choice", "primitive:f4")]
    [InlineData("double choice", "primitive:f8")]
    [InlineData("choice of arguments", "primitive:i4")]
    public void TheNewLocalHasTheExpressionsType(string shape, string type)
    {
        var body = Shape(shape);

        var rewritten = Hoister(enabled: true).Rewrite(body);

        Assert.Equal(type, rewritten.LocalSignatureTypes[^1].CanonicalName);
        Assert.Equal(rewritten.LocalSignatureTypes[^1].StackKind, rewritten.Locals[^1]);
    }

    // A lane read moves only when its constant index lies inside a 16-byte
    // vector of the element type, read through the static Vector128.GetElement.
    [Theory]
    [InlineData("i1", 15, true)]
    [InlineData("u1", 15, true)]
    [InlineData("u1", 16, false)]
    [InlineData("i2", 7, true)]
    [InlineData("u2", 7, true)]
    [InlineData("char", 7, true)]
    [InlineData("char", 8, false)]
    [InlineData("i4", 3, true)]
    [InlineData("u4", 3, true)]
    [InlineData("f4", 3, true)]
    [InlineData("f4", 4, false)]
    [InlineData("i8", 1, true)]
    [InlineData("u8", 1, true)]
    [InlineData("f8", 1, true)]
    [InlineData("f8", 2, false)]
    [InlineData("nativeint", 0, false)]
    [InlineData(null, 0, false)]
    public void ReadsOnlyALaneInsideTheVector(string? element, int lane, bool hoisted)
    {
        var kind = element switch
        {
            "f4" => CliValueKind.F4,
            "f8" => CliValueKind.F8,
            "i8" or "u8" => CliValueKind.I8,
            "nativeint" => CliValueKind.NativeInt,
            _ => CliValueKind.I4,
        };
        // A null element is a GetElement without the method argument that names it.
        var read = StaticVectorElement(CliTypeIdentity.Primitive(element ?? "i4", kind));
        if (element is null) read = read with { MethodArguments = [] };
        var body = Loop(Choice([Local(4), I(CilOperation.LoadInt32, lane), Call(read), I(CilOperation.ConvertInt32)],
            [Arg(1)]));

        var rewritten = Hoister(enabled: true).Rewrite(body);

        Assert.Equal(hoisted, !ReferenceEquals(body, rewritten));
    }

    // A protected region that begins inside the run ends the expression there: here
    // it begins at the choice's second arm, where the stack is empty, so the choice
    // never comes together and nothing moves out of the loop.
    [Fact]
    public void AProtectedRegionBeginningInsideTheRunKeepsItInTheLoop()
    {
        var body = Shape("choice of arguments");
        var length = body.Instructions.Length;
        var arm = body.Instructions.Single(instruction => instruction.Operation == CilOperation.BranchIfGreaterThanSigned)
            .Operand is CilOperand.BranchTarget { Offset: var target } ? target : -1;
        Assert.NotSame(body, Hoister(enabled: true).Rewrite(body));
        body = body with
        {
            Instructions = [.. body.Instructions, .. Enumerable.Range(length, 4)
                .Select(offset => new CilInstruction(offset, offset + 1, CilOperation.Nop, new CilOperand.None()))],
            ExceptionRegions = [new CilExceptionRegion(CilExceptionRegionKind.Catch, arm, 1, length + 2, 2, null, null)],
        };

        Assert.Same(body, Hoister(enabled: true).Rewrite(body));
    }

    // Exception regions elsewhere in the method, a catch or a filtered one, leave
    // the loop's expression free to move; a handler that holds the loop does not.
    [Theory]
    [InlineData(CilExceptionRegionKind.Catch, false, true)]
    [InlineData(CilExceptionRegionKind.Filter, false, true)]
    [InlineData(CilExceptionRegionKind.Catch, true, false)]
    public void ExceptionRegionsOutsideTheLoopLeaveItsExpressionsFreeToMove(
        CilExceptionRegionKind kind,
        bool handlerHoldsTheLoop,
        bool hoisted)
    {
        // Four instructions after the return hold the filter and the handler; the
        // try covers the load of the total after the loop.
        var body = Shape("choice of arguments");
        var length = body.Instructions.Length;
        body = body with
        {
            Instructions = [.. body.Instructions, .. Enumerable.Range(length, 4)
                .Select(offset => new CilInstruction(offset, offset + 1, CilOperation.Nop, new CilOperand.None()))],
        };
        var region = handlerHoldsTheLoop
            ? new CilExceptionRegion(kind, length + 1, 1, 0, length, null, null)
            : new CilExceptionRegion(kind, length - 2, 1, length + 2, 2, null,
                kind == CilExceptionRegionKind.Filter ? length : null);
        body = body with { ExceptionRegions = [region] };

        var rewritten = Hoister(enabled: true).Rewrite(body);

        Assert.Equal(hoisted, !ReferenceEquals(body, rewritten));
    }

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
        new(new OperandCalledMethodResolver(), new Intrinsics(new Dictionary<EntityKey, RuntimeIntrinsic>
        {
            [GetElement.Definition.Key] = RuntimeIntrinsic.SimdGetElement,
            [StaticVectorElement(Int64).Definition.Key] = RuntimeIntrinsic.SimdGetElement,
            [AsInt64.Definition.Key] = RuntimeIntrinsic.SimdReinterpret,
        }), enabled);

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
        "long choice" => Loop(Choice([Arg(0), I(CilOperation.ConvertInt64)], [I8(5)])),
        "float choice" => Loop(Choice([Arg(0), I(CilOperation.ConvertFloat32)], [F4(1.5f)])),
        "double choice" => Loop(Choice([Arg(0), I(CilOperation.ConvertFloat64)], [F8(2)])),
        "duplicated argument in an arm" => Loop(Choice([Arg(0), I(CilOperation.Duplicate), I(CilOperation.Add)], [Arg(1)])),
        "negated argument in an arm" => Loop(Choice([Arg(0), I(CilOperation.Negate)], [Arg(1)])),
        "comparison in an arm" => Loop(Choice([Arg(0), Arg(1), I(CilOperation.CompareLessThanSigned)], [Arg(1)])),
        // An int and a long add to a long, a double and an int to a double.
        "long operand in an arm" => Loop(Choice([Arg(0), I8(1), I(CilOperation.Add), I(CilOperation.ConvertInt32)], [Arg(1)])),
        "floating operand first in an arm" => Loop(Choice([F8(1), Arg(0), I(CilOperation.Add), I(CilOperation.ConvertInt32)],
            [Arg(1)])),
        // a > b || b < a ? a : b: two branches land on the same arm with the same stack.
        "two tests for one arm" => Loop([Arg(0), Arg(1), Branch(CilOperation.BranchIfGreaterThanSigned, 8), Arg(1), Arg(0),
            Branch(CilOperation.BranchIfLessThanSigned, 8), Arg(1), Branch(CilOperation.Branch, 9), Arg(0)]),
        "square root in an arm" => Loop(Choice([Arg(0), I(CilOperation.ConvertFloat64), Call(SquareRoot),
            I(CilOperation.ConvertInt32)], [Arg(1)])),
        "reinterpreted vector in an arm" => Loop(Choice([Local(4), Call(AsInt64), I(CilOperation.LoadInt32, 0), Call(GetElement),
            I(CilOperation.ConvertInt32)], [Arg(1)])),
        // switch (a) { case 0: ... }, landing just past itself and on an offset no instruction has.
        "switch ahead of the choice" => Loop(Choice(Arg(0), Arg(1)), [Arg(0),
            I(CilOperation.Switch, new CilOperand.SwitchTargets([5, 999]))]),
        "choice of vectors" => Loop(Choice(Local(4), Local(4))),
        // Each instruction here, taken as a run's first, has nothing on the stack to work on.
        "straight-line duplicate and comparison" => Loop([Arg(0), I(CilOperation.Duplicate), I(CilOperation.CompareLessThanSigned)]),
        "jump back to the body's start" => Loop([Arg(0), Branch(CilOperation.Branch, 0)]),
        "conditional jump back to the body's start" => Loop([Arg(0), Arg(1), Branch(CilOperation.BranchIfGreaterThanSigned, 0)]),
        // Both land on position 6, one with nothing on the stack and one with two values.
        "jump with a different stack" => Loop([Arg(0), Arg(1), Branch(CilOperation.BranchIfGreaterThanSigned, 6), Arg(0), Arg(1),
            Branch(CilOperation.Branch, 6), Arg(0)]),
        "conditional jump with a different stack" => Loop([Arg(0), Arg(1), Branch(CilOperation.BranchIfGreaterThanSigned, 7),
            Arg(0), Arg(0), Arg(1), Branch(CilOperation.BranchIfLessThanSigned, 7), Arg(1)]),
        // a > b ? a : b with each arm ending in a jump to the instruction after it.
        "both arms jump to the end" => Loop([Arg(0), Arg(1), Branch(CilOperation.BranchIfGreaterThanSigned, 5), Arg(1),
            Branch(CilOperation.Branch, 7), Arg(0), Branch(CilOperation.Branch, 7)]),
        // After the total's update, a jump back to position 4, the choice's second instruction.
        "jump back into the choice" => Loop(Choice(Arg(0), Arg(1)), tail: [Arg(0), Branch(CilOperation.BranchIfTrue, 4)]),
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

    private static CilInstruction F4(float value) => I(CilOperation.LoadFloat32, new CilOperand.ConstantF4(value));

    private static CilInstruction I8(long value) => I(CilOperation.LoadInt64, new CilOperand.ConstantI8(value));

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

    // Vector128.GetElement<T>(Vector128<T>, int), whose element type is its method argument.
    private static MethodInstanceModel StaticVectorElement(CliTypeIdentity element)
    {
        var vector = CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.Named(Assembly, "System.Runtime.Intrinsics", "Vector128`1", isValueType: true), [element]);
        var signature = MethodSignatureModel.Create(element, vector, Int32);
        var definition = new MethodDefinitionModel(new(Assembly, 0x06000004), new(Assembly, 0x02000004), "GetElement", true,
            signature, 1);
        return new(definition, CliTypeIdentity.Named(Assembly, "System.Runtime.Intrinsics", "Vector128", isValueType: false),
            [element], signature);
    }

    // Math.Sqrt(double).
    private static MethodInstanceModel MathSquareRoot()
    {
        var signature = MethodSignatureModel.Create(Float64, Float64);
        var definition = new MethodDefinitionModel(new(Assembly, 0x06000005), new(Assembly, 0x02000005), "Sqrt", true,
            signature, 1);
        return new(definition, CliTypeIdentity.Named(Assembly, "System", "Math", isValueType: false), [], signature);
    }

    // Vector.AsVectorInt64(Vector<long>), a reinterpretation the emitter folds away.
    private static MethodInstanceModel VectorReinterpretation()
    {
        var vector = CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.Named(Assembly, "System.Numerics", "Vector`1", isValueType: true), [Int64]);
        var signature = MethodSignatureModel.Create(vector, vector);
        var definition = new MethodDefinitionModel(new(Assembly, 0x06000006), new(Assembly, 0x02000006), "AsVectorInt64", true,
            signature, 1);
        return new(definition, CliTypeIdentity.Named(Assembly, "System.Numerics", "Vector", isValueType: false), [], signature);
    }

    private sealed class OperandCalledMethodResolver : ICalledMethodResolver
    {
        public MethodInstanceModel? Resolve(CilInstruction instruction) =>
            instruction.Operand is CilOperand.MethodInstance method ? method.Value : null;
    }

    private sealed class Intrinsics(IReadOnlyDictionary<EntityKey, RuntimeIntrinsic> values) : IRuntimeIntrinsicRegistry
    {
        public bool TryGetIntrinsic(EntityKey key, out RuntimeIntrinsic intrinsic) => values.TryGetValue(key, out intrinsic);
    }
}
