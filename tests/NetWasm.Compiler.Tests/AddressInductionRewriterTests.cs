using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Analysis.Loops;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

// Loop shapes CIL allows around Unsafe.Add of a reference and a counter, which
// Roslyn and the passes before this one leave: what moves onto a reference of
// its own, and every shape that keeps its reads. AddressInductionCorpusTests runs
// the loops C# writes against desktop .NET.
public sealed class AddressInductionRewriterTests
{
    private static readonly AssemblyIdentity Assembly = new("fixture");
    private static readonly CliTypeIdentity Int32 = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
    private static readonly CliTypeIdentity Int64 = CliTypeIdentity.Primitive("i8", CliValueKind.I8);
    private static readonly CliTypeIdentity NativeInt = CliTypeIdentity.Primitive("i", CliValueKind.NativeInt);
    private static readonly CliTypeIdentity Reference = CliTypeIdentity.ManagedByReference(Int32);
    private static readonly CliTypeIdentity Byte = CliTypeIdentity.Primitive("u1", CliValueKind.I4);

    // Unsafe.Add<int>(ref int, int), (ref int, nint), Unsafe.Add<long>(ref long, nint) and Unsafe.As<int, int>.
    private static readonly MethodInstanceModel AddInt = Method(1, "Unsafe", "Add", [Int32], Reference, Reference, Int32);
    private static readonly MethodInstanceModel AddNative = Method(2, "Unsafe", "Add", [Int32], Reference, Reference, NativeInt);
    private static readonly MethodInstanceModel AddLong =
        Method(2, "Unsafe", "Add", [Int64], CliTypeIdentity.ManagedByReference(Int64), CliTypeIdentity.ManagedByReference(Int64), NativeInt);
    private static readonly MethodInstanceModel AddPointer = Method(3, "Unsafe", "Add", [Int32], NativeInt, NativeInt, Int32);
    private static readonly MethodInstanceModel AddNonGeneric = Method(4, "Unsafe", "Add", [], Reference, Reference, Int32);
    private static readonly MethodInstanceModel AddLongIndex = Method(5, "Unsafe", "Add", [Int32], Reference, Reference, Int64);
    private static readonly MethodInstanceModel As = Method(6, "Unsafe", "As", [Int32, Int32], Reference, Reference);
    private static readonly MethodInstanceModel VectorAdd = Method(7, "Vector128", "Add", [Int32], NativeInt, NativeInt, NativeInt);
    private static readonly MethodInstanceModel Combine = Method(8, "Program", "Combine", [], Int32, Int32, Int32);
    private static readonly MethodInstanceModel Thrower = Method(9, "ThrowHelper", "Throw", [], CliTypeIdentity.FromStackKind(CliValueKind.Void));
    private static readonly MethodInstanceModel Exception = Method(12, "ThrowHelper", "Create", [], CliTypeIdentity.FromStackKind(CliValueKind.ManagedReference));
    private static readonly MethodInstanceModel Constructor =
        Method(13, "Exception", ".ctor", [], CliTypeIdentity.FromStackKind(CliValueKind.Void)) is var constructor
            ? constructor with { Definition = constructor.Definition with { IsStatic = false } }
            : null!;
    private static readonly MethodInstanceModel Unresolved = Method(10, "Unsafe", "Add", [Int32], Reference, Reference, Int32);
    private static readonly MethodInstanceModel IsNull = Method(11, "Unsafe", "IsNullRef", [Int32], Int32, Reference);
    private static readonly MethodInstanceModel AddByte = Method(14, "Unsafe", "Add", [Byte],
        CliTypeIdentity.ManagedByReference(Byte), CliTypeIdentity.ManagedByReference(Byte), Int32);

    public static TheoryData<string> AdvancedShapes() =>
    [
        "a for loop", "a do-while", "a native counter", "a native counter stepped without a conversion",
        "an int counter sign-extended", "an int counter zero-extended", "a counter that counts down",
        "a counter stepped by a subtraction", "nops between the parts", "two references", "a reference argument",
        "a call on its way to a throw", "a call on its way to a throw of null", "an allocation thrown",
        "a Vector128 intrinsic", "an address intrinsic", "a jump out of the loop before the head",
        "a branch inside the loop to its exit", "another jump to its condition", "a long element",
        "a read of the counter elsewhere", "a counter written outside the loop", "a read at the counter plus a constant",
        "a native read at the counter plus a constant", "a native read at the counter plus a converted constant",
        "an int counter plus a constant zero-extended",
    ];

    [Theory]
    [MemberData(nameof(AdvancedShapes))]
    public void ReadsMoveOntoAReferenceOfTheirOwn(string shape)
    {
        var body = Shape(shape);

        var rewritten = Rewriter().Rewrite(body);

        Assert.NotSame(body, rewritten);
        Assert.Equal(body.MaxStack + 2, rewritten.MaxStack);
        Assert.Equal(CliValueKind.ManagedAddress, rewritten.Locals[^1]);
        Assert.Equal(CliTypeShape.ManagedByReference, rewritten.LocalSignatureTypes[^1].Shape);
        _ = Validator().Validate(new ControlFlowGraphBuilderFactory().Create().Build(rewritten));
    }

    public static TheoryData<string> IndexedShapes() =>
    [
        "no Unsafe.Add", "a protected region", "an unconditional back edge", "a loop at the first instruction",
        "a jump before the head elsewhere", "a jump before the head to itself", "an inner loop", "a branch back to the head",
        "a branch into the body", "a switch into the condition", "a branch to the head of a do-while",
        "a conditional branch before a do-while to its head", "a call", "an allocation", "a call before the back edge",
        "a call before something else", "a counter written twice", "a counter whose address is taken", "a long counter",
        "a counter multiplied", "a counter stepped by a local", "a counter stepped by nothing", "a counter stepped by int.MinValue",
        "a counter stepped from another local", "a branch into the increment", "an int counter stepped with a conversion",
        "an increment at the head", "a reference written in the loop", "a reference whose address is taken",
        "a reference of an int", "the receiver", "a reference of an int argument", "a reference from a field",
        "a read entered part way", "a read of another counter", "a read of a constant", "an int counter into a native int",
        "a converted int counter into an int", "a converted native counter", "a native counter into an int",
        "Unsafe.Add of a pointer", "a non-generic Unsafe.Add", "Unsafe.Add of a long", "an unresolved Unsafe.Add",
        "a read at the head", "a read at the counter times a constant", "a read at another local plus a constant",
        "a read at the counter plus a local", "a read of a constant plus nothing", "an int counter plus a converted constant",
        "a read whose index is pushed before the head", "a read a forward branch enters part way", "a byte element",
        "a branch back to the condition from inside it",
    ];

    [Theory]
    [MemberData(nameof(IndexedShapes))]
    public void ShapeKeepsItsReads(string shape)
    {
        var body = Shape(shape);

        Assert.Same(body, Rewriter().Rewrite(body));
    }

    [Fact]
    public void DisabledRewriterKeepsTheReads()
    {
        var body = Shape("a for loop");

        Assert.Same(body, new AddressInductionRewriter(new Resolver(), new Intrinsics(), false).Rewrite(body));
    }

    [Fact]
    public void ConstructorRequiresItsCollaborators()
    {
        Assert.Throws<ArgumentNullException>(() => new AddressInductionRewriter(null!, new Intrinsics(), true));
        Assert.Throws<ArgumentNullException>(() => new AddressInductionRewriter(new Resolver(), null!, true));
        Assert.Throws<ArgumentNullException>(() => Rewriter().Rewrite(null!));
    }

    // The jump to the condition sets p first, the read becomes p, and p moves just before i.
    [Fact]
    public void ForLoopSetsTheReferenceBeforeItsJumpAndMovesItWithTheCounter()
    {
        var rewritten = Rewriter().Rewrite(Shape("a for loop"));

        var p = rewritten.Locals.Length - 1;
        Assert.Equal(
        [
            "ldc.i4 0", "stloc 0", "ldarg 0", "ldloc 0", "call Add", $"stloc {p}", "br 20",
            "ldloc 1", $"ldloc {p}", "ldobj", "add", "stloc 1",
            $"ldloc {p}", "ldc.i4 1", "call Add", $"stloc {p}", "ldloc 0", "ldc.i4 1", "add", "stloc 0",
            "ldloc 0", "ldarg 2", "blt 7", "ldloc 1", "ret",
        ], Text(rewritten));
    }

    // Unsafe.Add(ref a, i + 3) reads Unsafe.Add(ref p, 3); the read at i reads p.
    [Fact]
    public void ReadsAtTheCounterPlusAConstantAddItToTheReference()
    {
        var rewritten = Rewriter().Rewrite(Shape("a read at the counter plus a constant"));

        var p = rewritten.Locals.Length - 1;
        var code = Text(rewritten);
        Assert.Equal(["ldloc 1", $"ldloc {p}", "ldobj", "add", "stloc 1", "ldloc 1", $"ldloc {p}", "ldc.i4 3", "call Add", "ldobj"],
            code[7..17]);
    }

    // A native-int read of an int counter sign-extends it into p, and p moves by a native step.
    [Fact]
    public void IntCounterReadAsANativeIntSignExtendsIntoTheReference()
    {
        var rewritten = Rewriter().Rewrite(Shape("an int counter zero-extended"));

        var p = rewritten.Locals.Length - 1;
        var code = Text(rewritten);
        Assert.Equal(["ldarg 0", "ldloc 0", "conv.i", "call Add", $"stloc {p}"], code[2..7]);
        Assert.Equal([$"ldloc {p}", "ldc.i4 1", "conv.i", "call Add", $"stloc {p}"], code[^14..^9]);
    }

    // A do-while sets p after the instruction that falls into its head, which the back edge does not re-enter.
    [Fact]
    public void DoWhileSetsTheReferenceAfterTheInstructionBeforeItsHead()
    {
        var rewritten = Rewriter().Rewrite(Shape("a do-while"));

        var p = rewritten.Locals.Length - 1;
        var code = Text(rewritten);
        Assert.Equal(["ldc.i4 0", "stloc 0", "ldarg 0", "ldloc 0", "call Add", $"stloc {p}", "ldloc 1", $"ldloc {p}"], code[..8]);
        Assert.Equal("blt 6", code[^3]);
    }

    // A jump from outside to the condition goes to the jump before the head, through the prologue.
    [Fact]
    public void OtherJumpsToTheConditionGoThroughThePrologue()
    {
        var rewritten = Rewriter().Rewrite(Shape("another jump to its condition"));

        var code = Text(rewritten);
        var prologue = code.IndexOf("ldarg 0");
        Assert.Equal($"brtrue {prologue}", code[1]);
    }

    // Reads of one reference share p; another reference, or another element type, has its own.
    [Fact]
    public void EachReferenceAndElementTypeHasItsOwnLocal()
    {
        var body = Shape("two references");

        var rewritten = Rewriter().Rewrite(body);

        Assert.Equal(body.Locals.Length + 2, rewritten.Locals.Length);
        // Two prologues and two advances; the three reads are gone.
        Assert.Equal(4, Text(rewritten).Count(static line => line == "call Add"));
    }

    // A jump back to a loop's condition that falls into a do-while: the first loop redirects it, so the second keeps its reads.
    [Fact]
    public void AnInstructionTwoLoopsReplaceGoesToTheFirst()
    {
        var body = Shape("a jump back to a loop's condition before a do-while");

        var rewritten = Rewriter().Rewrite(body);

        Assert.Equal(body.Locals.Length + 1, rewritten.Locals.Length);
    }

    private static CilMethodBody Shape(string shape)
    {
        var code = new CilBuilder();
        var regions = ImmutableArray<CilExceptionRegion>.Empty;
        var isStatic = !shape.Contains("receiver", StringComparison.Ordinal) && shape != "a reference argument";
        CilBuilder Read(CilBuilder c) => c.Local(CilOperation.LoadArgument, 0).Local(CilOperation.LoadLocal, 0).Op(CilOperation.Call, Call(AddInt));
        CilBuilder Total(CilBuilder c, Func<CilBuilder, CilBuilder> read) =>
            read(c.Local(CilOperation.LoadLocal, 1)).Op(CilOperation.LoadObject, new CilOperand.TypeIdentity(Int32)).Op(CilOperation.Add)
                .Local(CilOperation.StoreLocal, 1);
        CilBuilder Step(CilBuilder c, int counter = 0) =>
            c.Local(CilOperation.LoadLocal, counter).Op(CilOperation.LoadInt32, 1).Op(CilOperation.Add).Local(CilOperation.StoreLocal, counter);
        // for (i = 0; i < n; i++) { body; step }, with what goes before the jump and what jumps.
        CilBuilder For(Func<CilBuilder, CilBuilder> body, Func<CilBuilder, CilBuilder>? step = null, int counter = 0,
            Func<CilBuilder, CilBuilder>? before = null, string jump = "test", CilOperation back = CilOperation.BranchIfLessThanSigned)
        {
            (before ?? (static c => c))(code.Op(CilOperation.LoadInt32, 0).Local(CilOperation.StoreLocal, counter))
                .Branch(CilOperation.Branch, jump).Label("start");
            body(code);
            (step ?? (c => Step(c, counter)))(code);
            return code.Label("test").Local(CilOperation.LoadLocal, counter).Local(CilOperation.LoadArgument, isStatic ? 2 : 3)
                .Branch(back, "start").Label("end").Local(CilOperation.LoadLocal, 1);
        }
        CilBuilder Native(CilBuilder c, int step, bool convert = true)
        {
            c.Local(CilOperation.LoadLocal, 3).Op(CilOperation.LoadInt32, step);
            if (convert) c.Op(CilOperation.ConvertNativeInt);
            return c.Op(CilOperation.Add).Local(CilOperation.StoreLocal, 3);
        }
        CilBuilder NativeLoop(Func<CilBuilder, CilBuilder> read, Func<CilBuilder, CilBuilder>? step = null)
        {
            code.Op(CilOperation.LoadInt32, 0).Op(CilOperation.ConvertNativeInt).Local(CilOperation.StoreLocal, 3)
                .Branch(CilOperation.Branch, "test").Label("start");
            Total(code, read);
            (step ?? (c => Native(c, 4)))(code);
            return code.Label("test").Local(CilOperation.LoadLocal, 3).Local(CilOperation.LoadArgument, 2).Op(CilOperation.ConvertNativeInt)
                .Branch(CilOperation.BranchIfLessThanSigned, "start").Local(CilOperation.LoadLocal, 1);
        }
        CilBuilder NativeRead(CilBuilder c) =>
            c.Local(CilOperation.LoadArgument, 0).Local(CilOperation.LoadLocal, 3).Op(CilOperation.Call, Call(AddNative));
        CilBuilder Throws(CilBuilder c, Func<CilBuilder, CilBuilder> raise) =>
            raise(c.Local(CilOperation.LoadLocal, 1).Branch(CilOperation.BranchIfTrue, "fine")).Label("fine");
        CilBuilder ReadWith(MethodInstanceModel add, Func<CilBuilder, CilBuilder> index) =>
            For(c => Total(c, r => index(r.Local(CilOperation.LoadArgument, 0)).Op(CilOperation.Call, Call(add))));

        switch (shape)
        {
            case "a for loop":
                For(c => Total(c, Read));
                break;
            case "a do-while":
                code.Op(CilOperation.LoadInt32, 0).Local(CilOperation.StoreLocal, 0).Label("start");
                Step(Total(code, Read), 0).Local(CilOperation.LoadLocal, 0).Local(CilOperation.LoadArgument, 2)
                    .Branch(CilOperation.BranchIfLessThanSigned, "start").Local(CilOperation.LoadLocal, 1);
                break;
            case "a native counter":
                NativeLoop(NativeRead);
                break;
            case "a native counter stepped without a conversion":
                NativeLoop(NativeRead, c => Native(c, 4, convert: false));
                break;
            case "an int counter sign-extended":
                For(c => Total(c, r => r.Local(CilOperation.LoadArgument, 0).Local(CilOperation.LoadLocal, 0)
                    .Op(CilOperation.ConvertNativeInt).Op(CilOperation.Call, Call(AddNative))));
                break;
            case "an int counter zero-extended":
                For(c => Total(c, r => r.Local(CilOperation.LoadArgument, 0).Local(CilOperation.LoadLocal, 0)
                    .Op(CilOperation.ConvertNativeUInt).Op(CilOperation.Call, Call(AddNative))));
                break;
            case "a counter that counts down":
                For(c => Total(c, Read), c => c.Local(CilOperation.LoadLocal, 0).Op(CilOperation.LoadInt32, -1).Op(CilOperation.Add)
                    .Local(CilOperation.StoreLocal, 0), back: CilOperation.BranchIfGreaterThanSigned);
                break;
            case "a counter stepped by a subtraction":
                For(c => Total(c, Read), c => c.Local(CilOperation.LoadLocal, 0).Op(CilOperation.LoadInt32, 2).Op(CilOperation.Subtract)
                    .Local(CilOperation.StoreLocal, 0), back: CilOperation.BranchIfGreaterThanSigned);
                break;
            case "nops between the parts":
                For(c => Total(c, r => r.Local(CilOperation.LoadArgument, 0).Op(CilOperation.Nop).Local(CilOperation.LoadLocal, 0)
                    .Op(CilOperation.Nop).Op(CilOperation.Call, Call(AddInt))),
                    c => c.Local(CilOperation.LoadLocal, 0).Op(CilOperation.Nop).Op(CilOperation.LoadInt32, 1).Op(CilOperation.Nop)
                        .Op(CilOperation.Add).Op(CilOperation.Nop).Local(CilOperation.StoreLocal, 0));
                break;
            case "two references":
                For(c => Total(Total(Total(c, Read), r => r.Local(CilOperation.LoadArgument, 1).Local(CilOperation.LoadLocal, 0)
                    .Op(CilOperation.Call, Call(AddInt))), Read));
                break;
            case "a reference argument":
                For(c => Total(c, r => r.Local(CilOperation.LoadArgument, 1).Local(CilOperation.LoadLocal, 0).Op(CilOperation.Call, Call(AddInt))));
                break;
            case "a call on its way to a throw":
                For(c => Throws(Total(c, Read), t => t.Op(CilOperation.Call, Call(Exception)).Op(CilOperation.Throw)));
                break;
            case "a call on its way to a throw of null":
                For(c => Throws(Total(c, Read), t => t.Op(CilOperation.Call, Call(Thrower)).Op(CilOperation.LoadNull).Op(CilOperation.Throw)));
                break;
            case "an allocation thrown":
                For(c => Throws(Total(c, Read), t => t.Op(CilOperation.NewObject, Call(Constructor)).Op(CilOperation.Throw)));
                break;
            case "a Vector128 intrinsic":
                For(c => Total(c, Read).Local(CilOperation.LoadLocal, 3).Local(CilOperation.LoadLocal, 3).Op(CilOperation.Call, Call(VectorAdd))
                    .Local(CilOperation.StoreLocal, 3));
                break;
            case "an address intrinsic":
                For(c => Total(c, r => Read(r).Op(CilOperation.Call, Call(As))));
                break;
            case "a jump out of the loop before the head":
                code.Local(CilOperation.LoadLocal, 1).Branch(CilOperation.BranchIfTrue, "after");
                For(c => Total(c, Read)).Op(CilOperation.Pop).Label("after").Local(CilOperation.LoadLocal, 1);
                break;
            case "a branch inside the loop to its exit":
                For(c => Total(c, Read).Local(CilOperation.LoadLocal, 1).Branch(CilOperation.BranchIfTrue, "end"));
                break;
            case "another jump to its condition":
                code.Local(CilOperation.LoadLocal, 1).Branch(CilOperation.BranchIfTrue, "test");
                For(c => Total(c, Read));
                break;
            case "a long element":
                For(c => c.Local(CilOperation.LoadLocal, 2).Local(CilOperation.LoadLocal, 0).Op(CilOperation.ConvertNativeInt)
                    .Op(CilOperation.Call, Call(AddLong)).Op(CilOperation.LoadObject, new CilOperand.TypeIdentity(Int64)).Op(CilOperation.Pop));
                break;
            // A byte's address is one add, as p's advance would be.
            case "a byte element":
                For(c => c.Local(CilOperation.LoadArgument, 0).Local(CilOperation.LoadLocal, 0).Op(CilOperation.Call, Call(AddByte))
                    .Op(CilOperation.LoadObject, new CilOperand.TypeIdentity(Byte)).Op(CilOperation.Pop));
                break;
            case "a read of the counter elsewhere":
                For(c => Total(c, Read).Local(CilOperation.LoadLocal, 0).Local(CilOperation.StoreLocal, 1));
                break;
            case "a counter written outside the loop":
                For(c => Total(c, Read));
                code.Op(CilOperation.Pop).Op(CilOperation.LoadInt32, 5).Local(CilOperation.StoreLocal, 0).Local(CilOperation.LoadLocal, 1);
                break;
            case "a read at the counter plus a constant":
                For(c => Total(Total(c, Read), r => r.Local(CilOperation.LoadArgument, 0).Local(CilOperation.LoadLocal, 0)
                    .Op(CilOperation.LoadInt32, 3).Op(CilOperation.Add).Op(CilOperation.Call, Call(AddInt))));
                break;
            case "a native read at the counter plus a constant":
                NativeLoop(c => c.Local(CilOperation.LoadArgument, 0).Local(CilOperation.LoadLocal, 3).Op(CilOperation.LoadInt32, 2)
                    .Op(CilOperation.Add).Op(CilOperation.Call, Call(AddNative)));
                break;
            case "a native read at the counter plus a converted constant":
                NativeLoop(c => c.Local(CilOperation.LoadArgument, 0).Local(CilOperation.LoadLocal, 3).Op(CilOperation.LoadInt32, 2)
                    .Op(CilOperation.ConvertNativeInt).Op(CilOperation.Add).Op(CilOperation.Call, Call(AddNative)));
                break;
            case "an int counter plus a constant zero-extended":
                For(c => Total(c, r => r.Local(CilOperation.LoadArgument, 0).Local(CilOperation.LoadLocal, 0).Op(CilOperation.LoadInt32, -1)
                    .Op(CilOperation.Add).Op(CilOperation.ConvertNativeUInt).Op(CilOperation.Call, Call(AddNative))));
                break;
            case "a read at the counter times a constant":
                ReadWith(AddInt, static r => r.Local(CilOperation.LoadLocal, 0).Op(CilOperation.LoadInt32, 2).Op(CilOperation.Multiply));
                break;
            case "a read at another local plus a constant":
                ReadWith(AddInt, static r => r.Local(CilOperation.LoadLocal, 1).Op(CilOperation.LoadInt32, 2).Op(CilOperation.Add));
                break;
            case "a read at the counter plus a local":
                ReadWith(AddInt, static r => r.Local(CilOperation.LoadLocal, 0).Local(CilOperation.LoadLocal, 1).Op(CilOperation.Add));
                break;
            case "a read of a constant plus nothing":
                code.Op(CilOperation.LoadInt32, 0).Local(CilOperation.StoreLocal, 0).Local(CilOperation.LoadArgument, 0)
                    .Local(CilOperation.LoadLocal, 0).Label("start").Op(CilOperation.LoadInt32, 1).Op(CilOperation.Add)
                    .Op(CilOperation.Call, Call(AddInt)).Op(CilOperation.Pop);
                Step(code, 0).Local(CilOperation.LoadArgument, 0).Local(CilOperation.LoadLocal, 0).Local(CilOperation.LoadLocal, 0)
                    .Local(CilOperation.LoadArgument, 2).Branch(CilOperation.BranchIfLessThanSigned, "start").Op(CilOperation.Pop)
                    .Op(CilOperation.Pop).Local(CilOperation.LoadLocal, 1);
                break;
            case "a read whose index is pushed before the head":
                code.Op(CilOperation.LoadInt32, 0).Local(CilOperation.StoreLocal, 0).Local(CilOperation.LoadArgument, 0)
                    .Local(CilOperation.LoadLocal, 0).Label("start").Op(CilOperation.Nop).Op(CilOperation.Nop)
                    .Op(CilOperation.Call, Call(AddInt)).Op(CilOperation.Pop);
                Step(code, 0).Local(CilOperation.LoadArgument, 0).Local(CilOperation.LoadLocal, 0).Local(CilOperation.LoadLocal, 0)
                    .Local(CilOperation.LoadArgument, 2).Branch(CilOperation.BranchIfLessThanSigned, "start").Op(CilOperation.Pop)
                    .Op(CilOperation.Pop).Local(CilOperation.LoadLocal, 1);
                break;
            case "an int counter plus a converted constant":
                ReadWith(AddNative, static r => r.Local(CilOperation.LoadLocal, 0).Op(CilOperation.LoadInt32, 2)
                    .Op(CilOperation.ConvertNativeInt).Op(CilOperation.Add));
                break;
            case "no Unsafe.Add":
                For(c => Total(c, r => r.Local(CilOperation.LoadArgument, 0)));
                break;
            case "a protected region":
                For(c => Total(c, Read));
                regions = [new CilExceptionRegion(CilExceptionRegionKind.Finally, 0, 1, 1, 1, null, null)];
                break;
            case "an unconditional back edge":
                code.Op(CilOperation.LoadInt32, 0).Local(CilOperation.StoreLocal, 0).Label("start");
                Step(Total(code, Read), 0).Branch(CilOperation.Branch, "start");
                break;
            case "a loop at the first instruction":
                code.Label("start");
                Step(Total(code, Read), 0).Local(CilOperation.LoadLocal, 0).Local(CilOperation.LoadArgument, 2)
                    .Branch(CilOperation.BranchIfLessThanSigned, "start").Local(CilOperation.LoadLocal, 1);
                break;
            case "a jump before the head elsewhere":
                For(c => Total(c, Read), jump: "end");
                break;
            case "a jump before the head to itself":
                For(c => Total(c, Read), jump: "start");
                break;
            case "an inner loop":
                For(c => Total(c, Read).Label("inner").Local(CilOperation.LoadLocal, 1).Branch(CilOperation.BranchIfTrue, "inner"));
                break;
            case "a branch back to the head":
                For(c => Total(c, Read).Local(CilOperation.LoadLocal, 1).Branch(CilOperation.BranchIfTrue, "start"));
                break;
            case "a branch into the body":
                code.Local(CilOperation.LoadLocal, 1).Branch(CilOperation.BranchIfTrue, "body");
                For(c => Total(c.Label("body"), Read));
                break;
            case "a switch into the condition":
                code.Local(CilOperation.LoadLocal, 1).Op(CilOperation.Switch, new CilOperand.SwitchTargets([]));
                var switchAt = 1;
                For(c => Total(c, Read));
                var built = code.Build();
                var test = built.First(static instruction => instruction.Operation == CilOperation.BranchIfLessThanSigned).Offset - 2;
                return Body(built.SetItem(switchAt, built[switchAt] with { Operand = new CilOperand.SwitchTargets([test]) }), regions, isStatic);
            case "a branch to the head of a do-while":
                code.Op(CilOperation.LoadInt32, 0).Local(CilOperation.StoreLocal, 0).Local(CilOperation.LoadLocal, 1)
                    .Branch(CilOperation.BranchIfTrue, "start").Op(CilOperation.Nop).Label("start");
                Step(Total(code, Read), 0).Local(CilOperation.LoadLocal, 0).Local(CilOperation.LoadArgument, 2)
                    .Branch(CilOperation.BranchIfLessThanSigned, "start").Local(CilOperation.LoadLocal, 1);
                break;
            case "a conditional branch before a do-while to its head":
                code.Op(CilOperation.LoadInt32, 0).Local(CilOperation.StoreLocal, 0).Local(CilOperation.LoadLocal, 1)
                    .Branch(CilOperation.BranchIfTrue, "start").Label("start");
                Step(Total(code, Read), 0).Local(CilOperation.LoadLocal, 0).Local(CilOperation.LoadArgument, 2)
                    .Branch(CilOperation.BranchIfLessThanSigned, "start").Local(CilOperation.LoadLocal, 1);
                break;
            case "a call":
                For(c => Total(c, Read).Local(CilOperation.LoadLocal, 1).Op(CilOperation.LoadInt32, 1).Op(CilOperation.Call, Call(Combine))
                    .Local(CilOperation.StoreLocal, 1));
                break;
            case "an allocation":
                For(c => Total(c, Read).Local(CilOperation.LoadLocal, 1).Op(CilOperation.Box, new CilOperand.TypeIdentity(Int32))
                    .Op(CilOperation.Pop));
                break;
            case "a call before the back edge":
                code.Op(CilOperation.LoadInt32, 0).Local(CilOperation.StoreLocal, 0).Label("start");
                Step(Total(code, Read), 0).Local(CilOperation.LoadLocal, 0).Op(CilOperation.Call, Call(IsNull))
                    .Branch(CilOperation.BranchIfTrue, "start").Local(CilOperation.LoadLocal, 1);
                break;
            case "a call before something else":
                For(c => Throws(Total(c, Read), t => t.Op(CilOperation.Call, Call(Thrower)).Op(CilOperation.LoadNull).Op(CilOperation.Pop)));
                break;
            case "a counter written twice":
                For(c => Total(c, Read).Op(CilOperation.LoadInt32, 0).Local(CilOperation.StoreLocal, 0));
                break;
            case "a counter whose address is taken":
                For(c => Total(c, Read).Local(CilOperation.LoadLocalAddress, 0).Op(CilOperation.Pop));
                break;
            case "a long counter":
                For(c => Total(c, r => r.Local(CilOperation.LoadArgument, 0).Local(CilOperation.LoadLocal, 4).Op(CilOperation.Call, Call(AddLongIndex))),
                    c => c.Local(CilOperation.LoadLocal, 4).Op(CilOperation.LoadInt32, 1).Op(CilOperation.Add).Local(CilOperation.StoreLocal, 4),
                    counter: 4);
                break;
            case "a counter multiplied":
                For(c => Total(c, Read), c => c.Local(CilOperation.LoadLocal, 0).Op(CilOperation.LoadInt32, 2).Op(CilOperation.Multiply)
                    .Local(CilOperation.StoreLocal, 0));
                break;
            case "a counter stepped by a local":
                For(c => Total(c, Read), c => c.Local(CilOperation.LoadLocal, 0).Local(CilOperation.LoadLocal, 1).Op(CilOperation.Add)
                    .Local(CilOperation.StoreLocal, 0));
                break;
            case "a counter stepped by nothing":
                For(c => Total(c, Read), c => c.Local(CilOperation.LoadLocal, 0).Op(CilOperation.LoadInt32, 0).Op(CilOperation.Add)
                    .Local(CilOperation.StoreLocal, 0));
                break;
            case "a counter stepped by int.MinValue":
                For(c => Total(c, Read), c => c.Local(CilOperation.LoadLocal, 0).Op(CilOperation.LoadInt32, int.MinValue)
                    .Op(CilOperation.Subtract).Local(CilOperation.StoreLocal, 0));
                break;
            case "a counter stepped from another local":
                For(c => Total(c, Read), c => c.Local(CilOperation.LoadLocal, 1).Op(CilOperation.LoadInt32, 1).Op(CilOperation.Add)
                    .Local(CilOperation.StoreLocal, 0));
                break;
            case "a branch into the increment":
                For(c => Total(c, Read).Local(CilOperation.LoadLocal, 1).Branch(CilOperation.BranchIfTrue, "add"),
                    c => c.Local(CilOperation.LoadLocal, 0).Op(CilOperation.LoadInt32, 1).Label("add").Op(CilOperation.Add)
                        .Local(CilOperation.StoreLocal, 0));
                break;
            case "an int counter stepped with a conversion":
                For(c => Total(c, Read), c => c.Local(CilOperation.LoadLocal, 0).Op(CilOperation.LoadInt32, 1).Op(CilOperation.ConvertNativeInt)
                    .Op(CilOperation.Add).Local(CilOperation.StoreLocal, 0));
                break;
            case "an increment at the head":
                code.Op(CilOperation.LoadInt32, 0).Local(CilOperation.StoreLocal, 0).Op(CilOperation.LoadInt32, 1).Label("start")
                    .Op(CilOperation.Add).Local(CilOperation.StoreLocal, 0);
                Total(code, Read).Local(CilOperation.LoadLocal, 0).Op(CilOperation.LoadInt32, 1).Local(CilOperation.LoadLocal, 0)
                    .Local(CilOperation.LoadArgument, 2).Branch(CilOperation.BranchIfLessThanSigned, "start").Op(CilOperation.Pop)
                    .Op(CilOperation.Pop).Local(CilOperation.LoadLocal, 1);
                break;
            case "a reference written in the loop":
                For(c => Total(c, Read).Local(CilOperation.LoadArgument, 1).Local(CilOperation.StoreArgument, 0));
                break;
            case "a reference whose address is taken":
                For(c => Total(c, Read).Local(CilOperation.LoadArgumentAddress, 0).Op(CilOperation.Pop));
                break;
            case "a reference of an int":
                For(c => Total(c, r => r.Local(CilOperation.LoadLocal, 1).Local(CilOperation.LoadLocal, 0).Op(CilOperation.Call, Call(AddInt))));
                break;
            case "the receiver":
                For(c => Total(c, Read));
                break;
            case "a reference of an int argument":
                For(c => Total(c, r => r.Local(CilOperation.LoadArgument, 2).Local(CilOperation.LoadLocal, 0).Op(CilOperation.Call, Call(AddInt))));
                break;
            case "a reference from a field":
                For(c => Total(c, r => r.Local(CilOperation.LoadArgument, 0).Op(CilOperation.LoadObject, new CilOperand.TypeIdentity(Reference))
                    .Local(CilOperation.LoadLocal, 0).Op(CilOperation.Call, Call(AddInt))));
                break;
            case "a read entered part way":
                For(c => Total(c, r => r.Local(CilOperation.LoadArgument, 0).Label("index").Local(CilOperation.LoadLocal, 0)
                    .Op(CilOperation.Call, Call(AddInt))).Local(CilOperation.LoadLocal, 1).Branch(CilOperation.BranchIfFalse, "end")
                    .Local(CilOperation.LoadArgument, 0).Branch(CilOperation.Branch, "index"));
                break;
            // Taken, the branch reaches the counter's load with the reference it pushed first.
            case "a read a forward branch enters part way":
                For(c => Total(c, r => r.Local(CilOperation.LoadArgument, 0).Local(CilOperation.LoadLocal, 1)
                    .Branch(CilOperation.BranchIfFalse, "index").Op(CilOperation.Pop).Local(CilOperation.LoadArgument, 0)
                    .Label("index").Local(CilOperation.LoadLocal, 0).Op(CilOperation.Call, Call(AddInt))));
                break;
            // A loop inside the condition: the loop is not innermost.
            case "a branch back to the condition from inside it":
                code.Op(CilOperation.LoadInt32, 0).Local(CilOperation.StoreLocal, 0).Branch(CilOperation.Branch, "test").Label("start");
                Step(Total(code, Read), 0).Label("test").Local(CilOperation.LoadLocal, 1).Branch(CilOperation.BranchIfFalse, "check")
                    .Local(CilOperation.LoadLocal, 1).Op(CilOperation.LoadInt32, 1).Op(CilOperation.Subtract).Local(CilOperation.StoreLocal, 1)
                    .Branch(CilOperation.Branch, "test")
                    .Label("check").Local(CilOperation.LoadLocal, 0).Local(CilOperation.LoadArgument, 2)
                    .Branch(CilOperation.BranchIfLessThanSigned, "start").Local(CilOperation.LoadLocal, 1);
                break;
            case "a read of another counter":
                For(c => Total(c, r => r.Local(CilOperation.LoadArgument, 0).Local(CilOperation.LoadLocal, 1).Op(CilOperation.Call, Call(AddInt))));
                break;
            case "a read of a constant":
                ReadWith(AddInt, static r => r.Op(CilOperation.LoadInt32, 1));
                break;
            case "an int counter into a native int":
                ReadWith(AddNative, static r => r.Local(CilOperation.LoadLocal, 0));
                break;
            case "a converted int counter into an int":
                ReadWith(AddInt, static r => r.Local(CilOperation.LoadLocal, 0).Op(CilOperation.ConvertNativeInt));
                break;
            case "a converted native counter":
                NativeLoop(c => c.Local(CilOperation.LoadArgument, 0).Local(CilOperation.LoadLocal, 3).Op(CilOperation.ConvertNativeUInt)
                    .Op(CilOperation.Call, Call(AddNative)));
                break;
            case "a native counter into an int":
                NativeLoop(c => c.Local(CilOperation.LoadArgument, 0).Local(CilOperation.LoadLocal, 3).Op(CilOperation.Call, Call(AddInt)));
                break;
            case "Unsafe.Add of a pointer":
                ReadWith(AddPointer, static r => r.Local(CilOperation.LoadLocal, 0));
                break;
            case "a non-generic Unsafe.Add":
                ReadWith(AddNonGeneric, static r => r.Local(CilOperation.LoadLocal, 0));
                break;
            case "Unsafe.Add of a long":
                ReadWith(AddLongIndex, static r => r.Local(CilOperation.LoadLocal, 0));
                break;
            case "an unresolved Unsafe.Add":
                ReadWith(Unresolved, static r => r.Local(CilOperation.LoadLocal, 0));
                break;
            case "a read at the head":
                code.Op(CilOperation.LoadInt32, 0).Local(CilOperation.StoreLocal, 0).Local(CilOperation.LoadArgument, 0).Label("start")
                    .Local(CilOperation.LoadLocal, 0).Op(CilOperation.Call, Call(AddInt)).Op(CilOperation.Pop);
                Step(code, 0).Local(CilOperation.LoadArgument, 0).Local(CilOperation.LoadLocal, 0).Local(CilOperation.LoadArgument, 2)
                    .Branch(CilOperation.BranchIfLessThanSigned, "start").Op(CilOperation.Pop).Local(CilOperation.LoadLocal, 1);
                break;
            case "a jump back to a loop's condition before a do-while":
                For(c => Total(c, Read)).Op(CilOperation.Pop).Local(CilOperation.LoadLocal, 1).Branch(CilOperation.BranchIfTrue, "test")
                    .Label("second");
                Step(Total(code, r => r.Local(CilOperation.LoadArgument, 1).Local(CilOperation.LoadLocal, 0).Op(CilOperation.Call, Call(AddInt))), 0)
                    .Local(CilOperation.LoadLocal, 0).Local(CilOperation.LoadArgument, 2).Branch(CilOperation.BranchIfLessThanSigned, "second")
                    .Local(CilOperation.LoadLocal, 1);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(shape), shape, null);
        }
        return Body(code.Return().Build(), regions, isStatic);
    }

    // Run(ref int a, ref int b, int n): locals int i, int total, ref long r, nint j, long k.
    private static CilMethodBody Body(ImmutableArray<CilInstruction> instructions, ImmutableArray<CilExceptionRegion> regions, bool isStatic)
    {
        var signature = MethodSignatureModel.Create(Int32, Reference, Reference, Int32);
        var method = new MethodDefinitionModel(new(Assembly, 0x06000001), new(Assembly, 0x02000001), "Run", isStatic, signature, 1);
        CliTypeIdentity[] locals = [Int32, Int32, CliTypeIdentity.ManagedByReference(Int64), NativeInt, Int64];
        if (instructions[^1].Operation != CilOperation.Return) instructions = instructions.Add(new(instructions.Length, instructions.Length + 1,
            CilOperation.Return, new CilOperand.None()));
        return new(method, 4, [.. locals.Select(static local => local.StackKind)], instructions)
        {
            LocalSignatureTypes = [.. locals],
            ExceptionRegions = regions,
        };
    }

    private static List<string> Text(CilMethodBody body) =>
    [
        .. body.Instructions.Select(static instruction => (instruction.Operation, instruction.Operand) switch
        {
            (CilOperation.LoadInt32, CilOperand.ConstantI4 { Value: var value }) => $"ldc.i4 {value}",
            (CilOperation.StoreLocal, CilOperand.Index { Value: var local }) => $"stloc {local}",
            (CilOperation.LoadLocal, CilOperand.Index { Value: var local }) => $"ldloc {local}",
            (CilOperation.LoadArgument, CilOperand.Index { Value: var argument }) => $"ldarg {argument}",
            (CilOperation.Call, CilOperand.MethodInstance { Value.Definition.Name: var name }) => $"call {name}",
            (CilOperation.Branch, CilOperand.BranchTarget { Offset: var target }) => $"br {target}",
            (CilOperation.BranchIfTrue, CilOperand.BranchTarget { Offset: var target }) => $"brtrue {target}",
            (CilOperation.BranchIfLessThanSigned, CilOperand.BranchTarget { Offset: var target }) => $"blt {target}",
            (CilOperation.LoadObject, _) => "ldobj",
            (CilOperation.Add, _) => "add",
            (CilOperation.ConvertNativeInt, _) => "conv.i",
            (CilOperation.ConvertNativeUInt, _) => "conv.u",
            (CilOperation.Return, _) => "ret",
            var other => other.Operation.ToString(),
        }),
    ];

    private static AddressInductionRewriter Rewriter() => new(new Resolver(), new Intrinsics(), true);

    private static ITypedStackValidator Validator()
    {
        var program = new Program();
        return new TypedStackValidatorFactory(new StackTypeCompatibilityValidator()).Create(program, program, program);
    }

    private static CilOperand.MethodInstance Call(MethodInstanceModel method) => new(method);

    private static MethodInstanceModel Method(int id, string type, string name, CliTypeIdentity[] arguments, CliTypeIdentity result,
        params CliTypeIdentity[] parameters)
    {
        var signature = MethodSignatureModel.Create(result, parameters);
        var definition = new MethodDefinitionModel(new(Assembly, 0x06000100 + id), new(Assembly, 0x02000100 + id), name, true, signature, 1);
        return new(definition, CliTypeIdentity.Named(Assembly, type == "Vector128" ? "System.Runtime.Intrinsics" : "System.Runtime.CompilerServices",
            type, isValueType: false), [.. arguments], signature);
    }

    private sealed class Resolver : ICalledMethodResolver
    {
        public MethodInstanceModel? Resolve(CilInstruction instruction) =>
            instruction.Operand is CilOperand.MethodInstance { Value: var method } && method != Unresolved ? method : null;
    }

    private sealed class Intrinsics : IRuntimeIntrinsicRegistry
    {
        public bool TryGetIntrinsic(EntityKey method, out RuntimeIntrinsic intrinsic)
        {
            intrinsic = (method.MetadataToken - 0x06000100) switch
            {
                1 or 2 or 3 or 4 or 5 or 10 or 14 => RuntimeIntrinsic.UnsafeAdd,
                6 => RuntimeIntrinsic.UnsafeAs,
                7 => RuntimeIntrinsic.SimdAdd,
                11 => RuntimeIntrinsic.StringLength,
                _ => default,
            };
            return method.MetadataToken - 0x06000100 is 1 or 2 or 3 or 4 or 5 or 6 or 7 or 10 or 11 or 14;
        }
    }

    private sealed class Program : ITypeRepository, IFieldRepository, IMethodRepository
    {
        public TypeDefinitionModel GetTypeDefinition(EntityKey key) => throw new NotSupportedException();

        public FieldDefinitionModel GetField(EntityKey key) => throw new NotSupportedException();

        public MethodDefinitionModel GetMethod(EntityKey key) => throw new NotSupportedException();
    }
}
