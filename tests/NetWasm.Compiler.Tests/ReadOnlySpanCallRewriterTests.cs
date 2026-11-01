using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

public sealed class ReadOnlySpanCallRewriterTests
{
    [Fact]
    public void ExactRvaCreateSpanPairBecomesExplicitSpanCreation()
    {
        var fixture = new Fixture();

        var rewritten = fixture.Rewrite(fixture.Body(
            fixture.Token(0, 1),
            fixture.Call(1, 2)));

        Assert.Equal(CilOperation.Nop, rewritten.Instructions[0].Operation);
        Assert.Equal(CilOperation.CreateReadOnlySpanFromRva,
            rewritten.Instructions[1].Operation);
        var operand = Assert.IsType<CilOperand.ReadOnlySpanData>(
            rewritten.Instructions[1].Operand);
        Assert.Equal(fixture.Field, operand.Field);
        Assert.Equal(fixture.SpanType, operand.SpanType);
        Assert.Equal(fixture.ElementType, operand.ElementType);
        Assert.Equal(2, operand.ElementCount);
        Assert.Equal(1, rewritten.Instructions[1].Offset);
    }

    [Fact]
    public void BranchEntryIntoCallPreservesOriginalPair()
    {
        var fixture = new Fixture();
        var body = fixture.Body(
            new CilInstruction(0, 1, CilOperation.Branch,
                new CilOperand.BranchTarget(2)),
            fixture.Token(1, 2),
            fixture.Call(2, 3));

        var rewritten = fixture.Rewrite(body);

        Assert.Same(body, rewritten);
        Assert.Equal(CilOperation.LoadFieldToken, rewritten.Instructions[1].Operation);
        Assert.Equal(CilOperation.Call, rewritten.Instructions[2].Operation);
    }

    [Fact]
    public void ExceptionBoundaryBetweenTokenAndCallPreservesOriginalPair()
    {
        var fixture = new Fixture();
        var body = fixture.Body(
            fixture.Token(0, 1),
            fixture.Call(1, 2),
            new CilInstruction(2, 3, CilOperation.Return, new CilOperand.None())) with
        {
            ExceptionRegions =
            [
                new(
                    CilExceptionRegionKind.Finally,
                    TryOffset: 1,
                    TryLength: 1,
                    HandlerOffset: 2,
                    HandlerLength: 1,
                    CatchType: null,
                    FilterOffset: null),
            ],
        };

        var rewritten = fixture.Rewrite(body);

        Assert.Same(body, rewritten);
    }

    [Fact]
    public void ProtectedRegionEndBetweenTokenAndCallPreservesOriginalPair()
    {
        var fixture = new Fixture();
        var body = fixture.Body(
            fixture.Token(0, 1),
            fixture.Call(1, 2),
            new CilInstruction(2, 3, CilOperation.Return, new CilOperand.None())) with
        {
            ExceptionRegions =
            [
                new(
                    CilExceptionRegionKind.Finally,
                    TryOffset: 0,
                    TryLength: 1,
                    HandlerOffset: 2,
                    HandlerLength: 1,
                    CatchType: null,
                    FilterOffset: null),
            ],
        };

        var rewritten = fixture.Rewrite(body);

        Assert.Same(body, rewritten);
    }

    [Fact]
    public void HandlerEndBetweenTokenAndCallPreservesOriginalPair()
    {
        var fixture = new Fixture();
        var body = fixture.Body(
            new CilInstruction(0, 1, CilOperation.Nop, new CilOperand.None()),
            new CilInstruction(1, 2, CilOperation.Branch,
                new CilOperand.BranchTarget(4)),
            fixture.Token(2, 3),
            fixture.Call(3, 4),
            new CilInstruction(4, 5, CilOperation.Return, new CilOperand.None())) with
        {
            ExceptionRegions =
            [
                new(
                    CilExceptionRegionKind.Finally,
                    TryOffset: 0,
                    TryLength: 1,
                    HandlerOffset: 2,
                    HandlerLength: 1,
                    CatchType: null,
                    FilterOffset: null),
            ],
        };

        var rewritten = fixture.Rewrite(body);

        Assert.Same(body, rewritten);
    }

    [Fact]
    public void FilterEntryBetweenTokenAndCallPreservesOriginalPair()
    {
        var fixture = new Fixture();
        var body = fixture.Body(
            fixture.Token(0, 1),
            fixture.Call(1, 2),
            new CilInstruction(2, 3, CilOperation.Return, new CilOperand.None())) with
        {
            ExceptionRegions =
            [
                new(
                    CilExceptionRegionKind.Filter,
                    TryOffset: 2,
                    TryLength: 1,
                    HandlerOffset: 2,
                    HandlerLength: 1,
                    CatchType: null,
                    FilterOffset: 1),
            ],
        };

        var rewritten = fixture.Rewrite(body);

        Assert.Same(body, rewritten);
    }

    [Theory]
    [InlineData("primitive:nativeint", 8)]
    [InlineData("primitive:u4", 6)]
    [InlineData("primitive:u4", 0)]
    public void UnsupportedElementOrRvaShapePreservesOriginalPair(
        string elementCanonicalName,
        int dataLength)
    {
        var fixture = new Fixture(elementCanonicalName, dataLength);
        var body = fixture.Body(fixture.Token(0, 1), fixture.Call(1, 2));

        var rewritten = fixture.Rewrite(body);

        Assert.Same(body, rewritten);
    }

    [Fact]
    public void NullBodyFailsBeforeRewriting()
    {
        var fixture = new Fixture();

        Assert.Throws<ArgumentNullException>(() => fixture.Rewrite(null!));
    }

    private sealed class Fixture
    {
        private readonly MethodInstanceModel _target;

        public Fixture(
            string elementCanonicalName = "primitive:u4",
            int dataLength = 8)
        {
            var core = new AssemblyIdentity("NetWasm.CoreLib");
            ElementType = elementCanonicalName switch
            {
                "primitive:nativeint" =>
                    CliTypeIdentity.Primitive("nativeint", CliValueKind.NativeInt),
                _ => CliTypeIdentity.Primitive("u4", CliValueKind.I4),
            };
            var spanDefinition = CliTypeIdentity.Named(
                core, "System", "ReadOnlySpan`1", isValueType: true);
            SpanType = CliTypeIdentity.GenericInstantiation(
                spanDefinition,
                [ElementType]);
            var handle = CliTypeIdentity.Named(
                core, "System", "RuntimeFieldHandle", isValueType: true);
            var runtimeHelpers = CliTypeIdentity.Named(
                core,
                "System.Runtime.CompilerServices",
                "RuntimeHelpers",
                isValueType: false);
            var openElement = CliTypeIdentity.GenericParameter(method: true, 0);
            var definition = new MethodDefinitionModel(
                new EntityKey(core, 1),
                new EntityKey(core, 2),
                "CreateSpan",
                true,
                MethodSignatureModel.Create(
                    CliTypeIdentity.GenericInstantiation(spanDefinition, [openElement]),
                    handle),
                0)
            {
                GenericArity = 1,
                IsPublic = true,
            };
            _target = new(
                definition,
                runtimeHelpers,
                [ElementType],
                MethodSignatureModel.Create(SpanType, handle));

            var owner = CliTypeIdentity.Named(
                core, "", "<PrivateImplementationDetails>", isValueType: false);
            var dataType = CliTypeIdentity.Named(
                core, "", "__StaticArrayInitTypeSize8", isValueType: true);
            var fieldDefinition = new FieldDefinitionModel(
                new EntityKey(core, 3),
                new EntityKey(core, 4),
                "Data",
                dataType,
                true)
            {
                InitialData = dataLength == 0
                    ? []
                    : [.. Enumerable.Range(0, dataLength).Select(value => (byte)value)],
                IsInitOnly = true,
            };
            Field = new(fieldDefinition, owner, dataType);
        }

        public CliTypeIdentity ElementType { get; }
        public CliTypeIdentity SpanType { get; }
        public FieldInstanceModel Field { get; }

        public CilInstruction Token(int offset, int nextOffset) => new(
            offset,
            nextOffset,
            CilOperation.LoadFieldToken,
            new CilOperand.FieldInstance(Field));

        public CilInstruction Call(int offset, int nextOffset) => new(
            offset,
            nextOffset,
            CilOperation.Call,
            new CilOperand.MethodInstance(_target));

        public CilMethodBody Body(params CilInstruction[] instructions) => new(
            new MethodDefinitionModel(
                new EntityKey(_target.Definition.Key.Assembly, 10),
                new EntityKey(new AssemblyIdentity("fixture"), 11),
                "Run",
                true,
                MethodSignatureModel.Create(CliValueKind.Void),
                1),
            2,
            [],
            [.. instructions]);

        public CilMethodBody Rewrite(CilMethodBody body) =>
            new CreateSpanCallRewriter(
                new FixedCalledMethodResolver(_target),
                new ReadOnlySpanDataResolver())
                .Rewrite(body);
    }

    private sealed class FixedCalledMethodResolver(MethodInstanceModel target) :
        ICalledMethodResolver
    {
        public MethodInstanceModel? Resolve(CilInstruction instruction) => target;
    }
}
