using System.Collections.Immutable;
using NetWasm.Compiler.Analysis.Constants;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Analysis.Constants;

public sealed class KnownSizeRewriterTests
{
    private static readonly AssemblyIdentity CoreLib = new("NetWasm.CoreLib");

    [Theory]
    [InlineData("primitive:u1", 1)]
    [InlineData("primitive:char", 2)]
    [InlineData("primitive:f4", 4)]
    [InlineData("primitive:i8", 8)]
    public void PrimitiveSizesAreTheirOwn(string name, int size) =>
        Assert.Equal(size, KnownSizeRewriter.Size(Primitive(name)));

    [Theory]
    [InlineData("System.Runtime.Intrinsics", "Vector64`1", 8)]
    [InlineData("System.Runtime.Intrinsics", "Vector128`1", 16)]
    [InlineData("System.Numerics", "Vector`1", 16)]
    [InlineData("System.Runtime.Intrinsics", "Vector256`1", 32)]
    [InlineData("System.Runtime.Intrinsics", "Vector512`1", 64)]
    public void VectorSizesAreFixed(string @namespace, string name, int size) =>
        Assert.Equal(size, KnownSizeRewriter.Size(CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.Named(CoreLib, @namespace, name, isValueType: true), [Primitive("primitive:i4")])));

    // Pointer-sized and laid-out types keep their sizeof.
    [Fact]
    public void OtherSizesStayUnknown()
    {
        Assert.Null(KnownSizeRewriter.Size(Primitive("primitive:nativeint")));
        Assert.Null(KnownSizeRewriter.Size(CliTypeIdentity.Named(CoreLib, "Fixture", "Pair", isValueType: true)));
    }

    // Native integers and pointers are the compiled target's native integer size.
    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    public void NativeSizedTypesAndIntPtrSizeAreTheTargetsNativeIntegerSize(int size)
    {
        var nativeUInt = CliTypeIdentity.Primitive("nativeuint", CliValueKind.NativeInt);
        var functionPointer = CliTypeIdentity.FunctionPointer(
            new CliFunctionPointerSignature(0, 0, 0, MethodSignatureModel.Create(CliValueKind.Void)));
        foreach (var type in new[] { Primitive("primitive:nativeint"), nativeUInt,
                     CliTypeIdentity.UnmanagedPointer(Primitive("primitive:i4")), functionPointer })
        {
            Assert.Equal(size, Folded(new KnownSizeRewriter(new FixedMethodResolver(null), size), SizeOf(type)));
        }
        foreach (var declaring in new[] { Primitive("primitive:nativeint"), nativeUInt })
        {
            var rewriter = new KnownSizeRewriter(new FixedMethodResolver(Method(declaring, "get_Size")), size);
            Assert.Equal(size, Folded(rewriter, Call()));
        }
        var sizeOf = new KnownSizeRewriter(new FixedMethodResolver(Method(Unsafe, "SizeOf", [nativeUInt])), size);
        Assert.Equal(size, Folded(sizeOf, Call()));
        var shared = new KnownSizeRewriter(new FixedMethodResolver(Method(Unsafe, "SizeOf", [Primitive("primitive:i8")])), size);
        Assert.Equal(8, Folded(shared, Call()));
    }

    // A laid-out type's sizeof, another call, an unresolved one, an instance Size and another
    // type's Size keep their instruction.
    [Fact]
    public void OtherInstructionsAreUnchanged()
    {
        var pair = CliTypeIdentity.Named(CoreLib, "Fixture", "Pair", isValueType: true);
        foreach (var (resolved, body) in new (MethodInstanceModel?, CilMethodBody)[]
                 {
                     (null, SizeOf(pair)),
                     (null, Call()),
                     (Method(Unsafe, "SizeOf", [pair]), Call()),
                     (Method(Primitive("primitive:nativeint"), "get_Size", isStatic: false), Call()),
                     (Method(Primitive("primitive:i4"), "get_Size"), Call()),
                     (Method(Primitive("primitive:nativeint"), "get_MaxValue"), Call()),
                 })
        {
            Assert.Same(body, new KnownSizeRewriter(new FixedMethodResolver(resolved), 8).Rewrite(body));
        }
    }

    [Fact]
    public void RejectsMissingInputs()
    {
        Assert.Throws<ArgumentNullException>(() => new KnownSizeRewriter(null!, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => new KnownSizeRewriter(new FixedMethodResolver(null), 2));
        Assert.Throws<ArgumentNullException>(() => new KnownSizeRewriter(new FixedMethodResolver(null), 4).Rewrite(null!));
    }

    private static readonly CliTypeIdentity Unsafe =
        CliTypeIdentity.Named(CoreLib, "System.Runtime.CompilerServices", "Unsafe", isValueType: false);

    private static int Folded(KnownSizeRewriter rewriter, CilMethodBody body)
    {
        var instruction = rewriter.Rewrite(body).Instructions[0];
        Assert.Equal(CilOperation.LoadInt32, instruction.Operation);
        return Assert.IsType<CilOperand.ConstantI4>(instruction.Operand).Value;
    }

    private static CilMethodBody SizeOf(CliTypeIdentity type) => Body(new(0, 1, CilOperation.SizeOf, new CilOperand.TypeIdentity(type)));

    private static CilMethodBody Call() => Body(new(0, 1, CilOperation.Call, new CilOperand.None()));

    private static CilMethodBody Body(CilInstruction first) => new(
        new MethodDefinitionModel(new EntityKey(CoreLib, 0x06000001), new EntityKey(CoreLib, 0x02000001), "Caller",
            IsStatic: true, MethodSignatureModel.Create(CliValueKind.I4), 0),
        1,
        [],
        [first, new(1, 2, CilOperation.Return, new CilOperand.None())]);

    private static MethodInstanceModel Method(
        CliTypeIdentity declaring, string name, ImmutableArray<CliTypeIdentity> arguments = default, bool isStatic = true)
    {
        var signature = MethodSignatureModel.Create(CliValueKind.I4);
        return new(
            new MethodDefinitionModel(new EntityKey(CoreLib, 0x06000002), new EntityKey(CoreLib, 0x02000002), name,
                IsStatic: isStatic, signature, 0),
            declaring,
            arguments.IsDefault ? [] : arguments,
            signature);
    }

    private sealed class FixedMethodResolver(MethodInstanceModel? method) : NetWasm.Compiler.Analysis.ICalledMethodResolver
    {
        public MethodInstanceModel? Resolve(CilInstruction instruction) => method;
    }

    private static CliTypeIdentity Primitive(string canonicalName) => canonicalName switch
    {
        "primitive:u1" => CliTypeIdentity.Primitive("u1", CliValueKind.I4),
        "primitive:char" => CliTypeIdentity.Primitive("char", CliValueKind.I4),
        "primitive:f4" => CliTypeIdentity.Primitive("f4", CliValueKind.F4),
        "primitive:i8" => CliTypeIdentity.Primitive("i8", CliValueKind.I8),
        "primitive:i4" => CliTypeIdentity.Primitive("i4", CliValueKind.I4),
        _ => CliTypeIdentity.Primitive("nativeint", CliValueKind.NativeInt),
    };
}
