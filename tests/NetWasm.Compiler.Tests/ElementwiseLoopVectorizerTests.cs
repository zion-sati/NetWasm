using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Analysis.Loops;
using NetWasm.Compiler.Analysis.Loops.Elementwise;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

// Loop shapes Roslyn does not emit but CIL allows, and those other passes leave
// in place before the vectorizer sees them: constants and calls inlining
// exposes, loops entered with values on the stack, and bounds and bodies the
// vector form cannot keep, and walks that use their reference as a number or
// read their element as another type. ElementwiseLoopVectorizerCorpusTests
// covers the shapes C# writes, against desktop .NET.
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
        "xor of double elements", "shifted double element", "double fold", "int fold into a native int",
        "long plus int elements", "int fold of elements divided by three",
    ];

    [Theory]
    [MemberData(nameof(ScalarShapes))]
    public void ShapeStaysScalar(string shape)
    {
        var body = Shape(shape);

        Assert.Same(body, Vectorizer().Rewrite(body));
    }

    // A walk folds the elements it reads through p, as the type p walks, into a
    // local of their kind with operations vectors have; one that also reads p's
    // address as a number, reads its element as another type, divides, folds a
    // value of another kind or folds into a native int stays a walk.
    [Theory]
    [InlineData("its elements", true)]
    [InlineData("its element plus its reference's complement", false)]
    [InlineData("its reference's complement plus its element", false)]
    [InlineData("its elements as another type", false)]
    [InlineData("its elements divided by three", false)]
    [InlineData("a long plus its elements", false)]
    [InlineData("its elements into a native int", false)]
    public void WalkFoldsOnlyElementsOfItsOwnType(string value, bool vectorized)
    {
        CilInstruction element = I(CilOperation.LoadObject, new CilOperand.TypeIdentity(Int32));
        var body = Walk(value switch
        {
            "its element plus its reference's complement" => [Walked(), element, Walked(), I(CilOperation.OnesComplement), I(CilOperation.Add)],
            "its reference's complement plus its element" => [Walked(), I(CilOperation.OnesComplement), Walked(), element, I(CilOperation.Add)],
            "its elements as another type" => [Walked(), I(CilOperation.LoadObject, new CilOperand.TypeIdentity(CliTypeIdentity.Primitive("u4", CliValueKind.I4)))],
            "its elements divided by three" => [Walked(), element, I(CilOperation.LoadInt32, 3), I(CilOperation.Divide)],
            "a long plus its elements" => [I(CilOperation.LoadInt64, new CilOperand.ConstantI8(5)), Walked(), element, I(CilOperation.Add)],
            _ => [Walked(), element],
        }, value == "its elements into a native int" ? CliValueKind.NativeInt : CliValueKind.I4);

        Assert.Equal(vectorized, !ReferenceEquals(body, Vectorizer().Rewrite(body)));
    }

    // sum = sum + (r[i + c] | 1) as a span's inlined indexer reads it: a check of
    // i + c, unsigned, against the length n, around a block that only throws,
    // then the element through Unsafe.Add of r at i + c. The vector loop takes it
    // under one entry check of the indexer's, or with no check at all; any other
    // check, index or element, and a store beside it, keep it scalar.
    [Theory]
    [InlineData("its elements", true)]
    [InlineData("its elements through Unsafe.Add of an int", true)]
    [InlineData("its elements at the counter plus one", true)]
    [InlineData("its elements past a throw of no null", true)]
    [InlineData("its elements past nops before the throw", true)]
    [InlineData("its elements unchecked", true)]
    [InlineData("its elements copied", true)]
    [InlineData("its elements copied through Unsafe.Add of an int", true)]
    [InlineData("its elements checked past the largest offset", false)]
    [InlineData("its elements read past the largest offset", false)]
    [InlineData("its elements as another type", false)]
    [InlineData("its elements as a struct", false)]
    [InlineData("a load of nothing", false)]
    [InlineData("its elements' addresses added", false)]
    [InlineData("its elements stored into an array", false)]
    [InlineData("a check whose block falls through", false)]
    [InlineData("a check whose block throws after two nulls", false)]
    [InlineData("a check whose block is only a nop", false)]
    [InlineData("a check whose block pushes before its call", false)]
    [InlineData("a check whose thrower takes an argument", false)]
    [InlineData("a check whose call does not resolve", false)]
    [InlineData("a check whose target is outside the body", false)]
    [InlineData("a check without a target", false)]
    [InlineData("a check of the counter doubled", false)]
    [InlineData("a check against a constant", false)]
    [InlineData("a check of nothing", false)]
    [InlineData("a native index of an argument", false)]
    [InlineData("a native index of nothing", false)]
    [InlineData("a native index into Unsafe.Add of an int", false)]
    [InlineData("an int index into Unsafe.Add of a native int", false)]
    [InlineData("Unsafe.Add of one value", false)]
    [InlineData("Unsafe.Add of a constant index", false)]
    [InlineData("Unsafe.Add of the counter doubled", false)]
    [InlineData("Unsafe.Add of a constant reference", false)]
    [InlineData("Unsafe.Add of a converted reference", false)]
    [InlineData("another type's Add", false)]
    [InlineData("Unsafe.Subtract", false)]
    [InlineData("a non-generic Unsafe.Add", false)]
    [InlineData("Unsafe.Add of a pointer", false)]
    public void SpanFoldRunsAsVectorsOnlyUnderItsIndexersCheck(string shape, bool vectorized)
    {
        var body = SpanShape(shape);

        Assert.Equal(vectorized, !ReferenceEquals(body, Vectorizer().Rewrite(body)));
    }

    // The entry check of r[i + 1]'s: i + 1 is not negative and n - 1 + 1, the
    // last index, lies below the length, unsigned.
    [Fact]
    public void SpanIndexersCheckBecomesTheVectorLoopsEntryCheck()
    {
        var rewritten = Vectorizer().Rewrite(SpanShape("its elements at the counter plus one"));
        var code = rewritten.Instructions.Select(static instruction => instruction.Operand switch
        {
            CilOperand.Index { Value: var index } => $"{instruction.Operation} {index}",
            CilOperand.ConstantI4 { Value: var value } => $"{instruction.Operation} {value}",
            _ => instruction.Operation.ToString(),
        }).ToArray();
        var text = string.Join(", ", code);

        Assert.Contains("LoadLocal 0, LoadInt32 1, Add, LoadInt32 0, BranchIfLessThanSigned", text, StringComparison.Ordinal);
        Assert.Contains("LoadLocal 3, LoadInt32 0, Add, LoadLocal 3, BranchIfGreaterThanOrEqualUnsigned", text, StringComparison.Ordinal);
    }

    // Each vector load extends its index to a native int as the scalar code did:
    // the indexer converts it unsigned, and Unsafe.Add of an int sign-extends.
    [Theory]
    [InlineData("its elements", CilOperation.ConvertNativeUInt)]
    [InlineData("its elements through Unsafe.Add of an int", CilOperation.ConvertNativeInt)]
    public void SpanElementLoadExtendsItsIndexAsTheScalarCodeDid(string shape, CilOperation extension)
    {
        var code = Vectorizer().Rewrite(SpanShape(shape)).Instructions;

        var loads = Enumerable.Range(1, code.Length - 1).Where(index => code[index].Operand is CilOperand.MethodInstance
        {
            Value.Definition.Name: "LoadUnsafe",
        }).ToArray();
        Assert.NotEmpty(loads);
        Assert.All(loads, index => Assert.Equal(extension, code[index - 1].Operation));
    }

    private static CilMethodBody SpanShape(string shape)
    {
        CilInstruction[] counter = [Counter()];
        CilInstruction[] next = [Counter(), I(CilOperation.LoadInt32, 1), I(CilOperation.Add)];
        CilInstruction[] far = [Counter(), I(CilOperation.LoadInt32, 1 << 16), I(CilOperation.Add)];
        CilInstruction[] native = [Counter(), I(CilOperation.ConvertNativeUInt)];
        CilInstruction[] thrower = [Call(Throw), I(CilOperation.LoadNull), I(CilOperation.Throw)];
        return shape switch
        {
            "its elements" => SpanLoop(Checked(counter, thrower, SpanRead(native, UnsafeAddNative))),
            "its elements through Unsafe.Add of an int" => SpanLoop(Checked(counter, thrower, SpanRead(counter, UnsafeAdd))),
            "its elements at the counter plus one" => SpanLoop(Checked(next, thrower,
                SpanRead([.. next, I(CilOperation.ConvertNativeUInt)], UnsafeAddNative))),
            "its elements past a throw of no null" => SpanLoop(Checked(counter, [Call(Throw), I(CilOperation.Throw)],
                SpanRead(native, UnsafeAddNative))),
            "its elements past nops before the throw" => SpanLoop(Checked(counter, [I(CilOperation.Nop), I(CilOperation.Nop), .. thrower],
                SpanRead(native, UnsafeAddNative))),
            "its elements unchecked" => SpanLoop(SpanRead(native, UnsafeAddNative)),
            "its elements copied" => SpanLoop(Checked(counter, thrower, SpanCopy(native, UnsafeAddNative))),
            "its elements copied through Unsafe.Add of an int" => SpanLoop(Checked(counter, thrower, SpanCopy(counter, UnsafeAdd))),
            "its elements checked past the largest offset" => SpanLoop(Checked(far, thrower, SpanRead(native, UnsafeAddNative))),
            "its elements read past the largest offset" => SpanLoop(SpanRead([.. far, I(CilOperation.ConvertNativeUInt)], UnsafeAddNative)),
            "its elements as another type" => SpanLoop(SpanRead(native, UnsafeAddNative,
                I(CilOperation.LoadObject, new CilOperand.TypeIdentity(CliTypeIdentity.Primitive("u4", CliValueKind.I4))))),
            "its elements' addresses added" => SpanLoop(SpanRead(native, UnsafeAddNative, I(CilOperation.Add))),
            // a[i] = r[i], into int array argument 0.
            "its elements stored into an array" => SpanLoop([Arg(0), Counter(), I(CilOperation.LoadLocal, new CilOperand.Index(2)), .. native,
                Call(UnsafeAddNative), I(CilOperation.LoadObject, new CilOperand.TypeIdentity(Int32)),
                I(CilOperation.StoreArrayElement, new CilOperand.TypeIdentity(Int32))]),
            "a check whose block falls through" => SpanLoop(Checked(counter, [Call(Throw)], SpanRead(native, UnsafeAddNative))),
            "a check whose block throws after two nulls" => SpanLoop(Checked(counter,
                [Call(Throw), I(CilOperation.LoadNull), I(CilOperation.LoadNull), I(CilOperation.Throw)], SpanRead(native, UnsafeAddNative))),
            "a check whose block is only a nop" => SpanLoop(Checked(counter, [I(CilOperation.Nop)], SpanRead(native, UnsafeAddNative))),
            "a check whose block pushes before its call" => SpanLoop(Checked(counter,
                [I(CilOperation.LoadInt32, 1), Call(ThrowWithArgument), I(CilOperation.Throw)], SpanRead(native, UnsafeAddNative))),
            "a check whose thrower takes an argument" => SpanLoop(Checked(counter, [Call(ThrowWithArgument), I(CilOperation.Throw)],
                SpanRead(native, UnsafeAddNative))),
            "a check whose call does not resolve" => SpanLoop(Checked(counter, [I(CilOperation.Call), I(CilOperation.Throw)],
                SpanRead(native, UnsafeAddNative))),
            "a check whose target is outside the body" => SpanLoop(Checked(counter, thrower, SpanRead(native, UnsafeAddNative), target: 1000)),
            "a check without a target" => SpanLoop([Counter(), Length(), I(CilOperation.BranchIfLessThanUnsigned), .. SpanRead(native, UnsafeAddNative)]),
            "a check of the counter doubled" => SpanLoop(Checked([Counter(), I(CilOperation.LoadInt32, 2), I(CilOperation.Multiply)], thrower,
                SpanRead(native, UnsafeAddNative))),
            "a check against a constant" => SpanLoop(Checked(counter, thrower, SpanRead(native, UnsafeAddNative),
                length: I(CilOperation.LoadInt32, 10))),
            "a check of nothing" => SpanLoop(Checked([], thrower, SpanRead(native, UnsafeAddNative), length: I(CilOperation.Nop))),
            "a native index of an argument" => SpanLoop(SpanRead([Arg(3), I(CilOperation.ConvertNativeUInt)], UnsafeAddNative)),
            "a native index of nothing" => SpanLoop([I(CilOperation.ConvertNativeInt), .. SpanRead(native, UnsafeAddNative)]),
            "a native index into Unsafe.Add of an int" => SpanLoop(SpanRead(native, UnsafeAdd)),
            "an int index into Unsafe.Add of a native int" => SpanLoop(SpanRead(counter, UnsafeAddNative)),
            "Unsafe.Add of one value" => SpanLoop([.. native, Call(UnsafeAddNative), I(CilOperation.LoadObject, new CilOperand.TypeIdentity(Int32)),
                I(CilOperation.StoreLocal, new CilOperand.Index(4))]),
            "a load of nothing" => SpanLoop([I(CilOperation.LoadObject, new CilOperand.TypeIdentity(Int32)), I(CilOperation.StoreLocal, new CilOperand.Index(4))]),
            "its elements as a struct" => SpanLoop(SpanRead(native, UnsafeAddNative,
                I(CilOperation.LoadObject, new CilOperand.TypeIdentity(CliTypeIdentity.Named(Assembly, "Fixture", "Pair", isValueType: true))))),
            "Unsafe.Add of a constant index" => SpanLoop(SpanRead([I(CilOperation.LoadInt32, 5)], UnsafeAdd)),
            "Unsafe.Add of the counter doubled" => SpanLoop(SpanRead([Counter(), I(CilOperation.LoadInt32, 2), I(CilOperation.Multiply)], UnsafeAdd)),
            "Unsafe.Add of a constant reference" => SpanLoop(SpanRead(native, UnsafeAddNative, reference: [I(CilOperation.LoadInt32, 0)])),
            "Unsafe.Add of a converted reference" => SpanLoop(SpanRead(native, UnsafeAddNative,
                reference: [Arg(3), I(CilOperation.ConvertInt64)])),
            "another type's Add" => SpanLoop(SpanRead(counter, AddOf("Fixture", "Owner", "Add", [Int32], Int32))),
            "Unsafe.Subtract" => SpanLoop(SpanRead(counter, AddOf("System.Runtime.CompilerServices", "Unsafe", "Subtract", [Int32], Int32))),
            "a non-generic Unsafe.Add" => SpanLoop(SpanRead(counter, AddOf("System.Runtime.CompilerServices", "Unsafe", "Add", [], Int32))),
            _ => SpanLoop(SpanRead(counter, AddOf("System.Runtime.CompilerServices", "Unsafe", "Add", [Int32], Int32, pointer: true))),
        };
    }

    // i = 0; goto test; start: body; i = i + 1; test: if (i < n) goto start;
    // return, with i in local 0, sum in 1, r in 2, n in 3 and a copy in 4.
    private static CilMethodBody SpanLoop(CilInstruction[] body) =>
        Loop(body, [Length()], locals: [(CliValueKind.ManagedAddress, CliTypeIdentity.ManagedByReference(Int32)),
            (CliValueKind.I4, Int32), (CliValueKind.I4, Int32)]);

    // index; n; blt.un read; thrower; read: the body starts at offset 3.
    private static CilInstruction[] Checked(CilInstruction[] index, CilInstruction[] thrower, CilInstruction[] read,
        int? target = null, CilInstruction? length = null) =>
    [
        .. index, length ?? Length(),
        I(CilOperation.BranchIfLessThanUnsigned, new CilOperand.BranchTarget(target ?? 3 + index.Length + 2 + thrower.Length)),
        .. thrower, .. read,
    ];

    // sum = sum + (*add(reference, index) | 1).
    private static CilInstruction[] SpanRead(CilInstruction[] index, MethodInstanceModel add, CilInstruction? load = null,
        CilInstruction[]? reference = null) =>
    [
        I(CilOperation.LoadLocal, new CilOperand.Index(1)), .. reference ?? [I(CilOperation.LoadLocal, new CilOperand.Index(2))], .. index,
        Call(add), load ?? I(CilOperation.LoadObject, new CilOperand.TypeIdentity(Int32)),
        I(CilOperation.LoadInt32, 1), I(CilOperation.BitwiseOr), I(CilOperation.Add), I(CilOperation.StoreLocal, new CilOperand.Index(1)),
    ];

    // s = *add(r, index); sum = sum + (s | 1), with s in local 4.
    private static CilInstruction[] SpanCopy(CilInstruction[] index, MethodInstanceModel add) =>
    [
        I(CilOperation.LoadLocal, new CilOperand.Index(2)), .. index, Call(add), I(CilOperation.LoadObject, new CilOperand.TypeIdentity(Int32)),
        I(CilOperation.StoreLocal, new CilOperand.Index(4)), I(CilOperation.LoadLocal, new CilOperand.Index(1)),
        I(CilOperation.LoadLocal, new CilOperand.Index(4)), I(CilOperation.LoadInt32, 1), I(CilOperation.BitwiseOr), I(CilOperation.Add),
        I(CilOperation.StoreLocal, new CilOperand.Index(1)),
    ];

    private static CilInstruction Length() => I(CilOperation.LoadLocal, new CilOperand.Index(3));

    private static readonly MethodInstanceModel Throw = Thrower([]);
    private static readonly MethodInstanceModel ThrowWithArgument = Thrower([Int32]);

    private static MethodInstanceModel Thrower(CliTypeIdentity[] parameters)
    {
        var signature = MethodSignatureModel.Create(CliTypeIdentity.FromStackKind(CliValueKind.Void), parameters);
        var definition = new MethodDefinitionModel(new(Assembly, 0x06000005), new(Assembly, 0x02000005), "ThrowIndexOutOfRangeException",
            true, signature, 1);
        return new(definition, CliTypeIdentity.Named(Assembly, "System", "ThrowHelper", isValueType: false), [], signature);
    }

    // Unsafe.Add<int>(ref int, nint).
    private static readonly MethodInstanceModel UnsafeAddNative =
        AddOf("System.Runtime.CompilerServices", "Unsafe", "Add", [Int32], CliTypeIdentity.Primitive("i", CliValueKind.NativeInt));

    private static MethodInstanceModel AddOf(string ns, string type, string name, CliTypeIdentity[] arguments, CliTypeIdentity index,
        bool pointer = false)
    {
        var reference = CliTypeIdentity.ManagedByReference(Int32);
        var source = pointer ? CliTypeIdentity.Primitive("i", CliValueKind.NativeInt) : reference;
        var signature = MethodSignatureModel.Create(reference, source, index);
        var definition = new MethodDefinitionModel(new(Assembly, 0x06000006), new(Assembly, 0x02000006), name, true, signature, 1);
        return new(definition, CliTypeIdentity.Named(Assembly, ns, type, isValueType: false), [.. arguments], signature);
    }

    private static ElementwiseLoopVectorizer Vectorizer()
    {
        var methods = new FakeVectorMethods();
        var calledMethods = new OperandCalledMethodResolver();
        var parser = new ElementwiseBodyParser(calledMethods);
        return new(
            new ElementwiseLoopMatcher(parser, new ElementwiseLaneSelector()),
            new ElementwiseWalkMatcher(parser),
            new ElementwiseLoopPrefixEmitter(methods),
            new ElementwiseWalkPrefixEmitter(methods),
            calledMethods,
            true);
    }

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
        // s = s + b[i] * 0.5 over doubles, whose sum depends on its order.
        "double fold" => Loop(
            [I(CilOperation.LoadLocal, new CilOperand.Index(1)), Arg(1), Counter(),
                I(CilOperation.LoadArrayElement, new CilOperand.TypeIdentity(Float64)),
                I(CilOperation.LoadFloat64, new CilOperand.ConstantF8(0.5)), I(CilOperation.Multiply), I(CilOperation.Add),
                I(CilOperation.StoreLocal, new CilOperand.Index(1))], Bound(), sum: CliValueKind.F8),
        // s = s + value over int elements, into a native int, or of values another
        // kind leads or division computes.
        "int fold into a native int" => IntFold([Arg(1), Counter(), I(CilOperation.LoadArrayElement, new CilOperand.TypeIdentity(Int32))],
            CliValueKind.NativeInt),
        "long plus int elements" => IntFold([I(CilOperation.LoadInt64, new CilOperand.ConstantI8(5)), Arg(1), Counter(),
            I(CilOperation.LoadArrayElement, new CilOperand.TypeIdentity(Int32)), I(CilOperation.Add)], CliValueKind.I4),
        "int fold of elements divided by three" => IntFold([Arg(1), Counter(), I(CilOperation.LoadArrayElement, new CilOperand.TypeIdentity(Int32)),
            I(CilOperation.LoadInt32, 3), I(CilOperation.Divide)], CliValueKind.I4),
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

    // s = s + value, with s in local 1.
    private static CilMethodBody IntFold(CilInstruction[] value, CliValueKind sum) => Loop(
        [I(CilOperation.LoadLocal, new CilOperand.Index(1)), .. value, I(CilOperation.Add), I(CilOperation.StoreLocal, new CilOperand.Index(1))],
        Bound(), sum: sum);

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
        CilOperation branch = CilOperation.BranchIfLessThanSigned,
        CliValueKind sum = CliValueKind.I4,
        (CliValueKind Kind, CliTypeIdentity Type)[]? locals = null)
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
        locals ??= [];
        return new(method, 4, [CliValueKind.I4, sum, .. locals.Select(static local => local.Kind)], instructions)
        {
            LocalSignatureTypes = [Int32, CliTypeIdentity.FromStackKind(sum), .. locals.Select(static local => local.Type)],
        };
    }

    // sum = sum + value, walking p over n ints: p in local 0, sum in local 1 and
    // n in local 2, from arguments 0 and 1.
    private static CilMethodBody Walk(CilInstruction[] value, CliValueKind sum)
    {
        CilInstruction[] code =
        [
            I(CilOperation.LoadInt32, 0), I(CilOperation.StoreLocal, new CilOperand.Index(1)),
            Arg(0), I(CilOperation.StoreLocal, new CilOperand.Index(0)),
            Arg(1), I(CilOperation.StoreLocal, new CilOperand.Index(2)),
            I(CilOperation.LoadLocal, new CilOperand.Index(1)), .. value, I(CilOperation.Add), I(CilOperation.StoreLocal, new CilOperand.Index(1)),
            Walked(), I(CilOperation.LoadInt32, 1), Call(UnsafeAdd), I(CilOperation.StoreLocal, new CilOperand.Index(0)),
            I(CilOperation.LoadLocal, new CilOperand.Index(2)), I(CilOperation.LoadInt32, 1), I(CilOperation.Subtract),
            I(CilOperation.StoreLocal, new CilOperand.Index(2)),
            I(CilOperation.LoadLocal, new CilOperand.Index(2)), new(0, 0, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(6)),
            I(CilOperation.Return),
        ];
        var reference = CliTypeIdentity.ManagedByReference(Int32);
        var method = new MethodDefinitionModel(new(Assembly, 0x06000001), new(Assembly, 0x02000001), "Walk", true,
            MethodSignatureModel.Create(CliTypeIdentity.FromStackKind(CliValueKind.Void), reference, Int32), 1);
        return new(method, 4, [CliValueKind.ManagedAddress, sum, CliValueKind.I4],
            [.. code.Select(static (instruction, index) => instruction with { Offset = index, NextOffset = index + 1 })])
        {
            LocalSignatureTypes = [reference, CliTypeIdentity.FromStackKind(sum), Int32],
        };
    }

    private static CilInstruction Walked() => I(CilOperation.LoadLocal, new CilOperand.Index(0));

    // Unsafe.Add<int>(ref int, int).
    private static readonly MethodInstanceModel UnsafeAdd = UnsafeAddOfInt();

    private static MethodInstanceModel UnsafeAddOfInt()
    {
        var reference = CliTypeIdentity.ManagedByReference(Int32);
        var signature = MethodSignatureModel.Create(reference, reference, Int32);
        var definition = new MethodDefinitionModel(new(Assembly, 0x06000004), new(Assembly, 0x02000004), "Add", true, signature, 1);
        return new(definition, CliTypeIdentity.Named(Assembly, "System.Runtime.CompilerServices", "Unsafe", isValueType: false), [Int32], signature);
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
