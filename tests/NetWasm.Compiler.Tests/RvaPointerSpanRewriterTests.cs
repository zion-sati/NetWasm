using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests;

// Roslyn creates ReadOnlySpan<byte> over constant data with the span's pointer
// constructor; over a whole read-only RVA field, the rewrite makes it the same
// RVA view that RuntimeHelpers.CreateSpan gives wider elements.
public sealed class RvaPointerSpanRewriterTests
{
    [Theory]
    [InlineData("u1", 1, 4)]
    [InlineData("i1", 1, 4)]
    [InlineData("bool", 1, 4)]
    [InlineData("u4", 4, 4)]
    public void PointerSpanOverAWholeReadOnlyFieldBecomesItsRvaView(string element, int size, int length)
    {
        var fixture = new Fixture(element, size);

        var rewritten = fixture.Rewrite(fixture.Body(length));

        Assert.Equal(
            [CilOperation.Nop, CilOperation.Nop, CilOperation.CreateReadOnlySpanFromRva, CilOperation.Return],
            rewritten.Instructions.Select(static instruction => instruction.Operation));
        var view = Assert.IsType<CilOperand.ReadOnlySpanData>(rewritten.Instructions[2].Operand);
        Assert.Equal(fixture.Field.Definition, view.Field.Definition);
        Assert.Equal(fixture.Owner, view.Field.DeclaringType);
        Assert.Equal(fixture.SpanType, view.SpanType);
        Assert.Equal(fixture.ElementType, view.ElementType);
        Assert.Equal(length, view.ElementCount);
    }

    [Fact]
    public void AFieldInstanceOperandIsTheFieldItNames()
    {
        var fixture = new Fixture();
        var body = fixture.Body(4);
        body = body with
        {
            Instructions = body.Instructions.SetItem(0, body.Instructions[0] with
            {
                Operand = new CilOperand.FieldInstance(fixture.Field),
            }),
        };

        var rewritten = fixture.Rewrite(body);

        Assert.Equal(CilOperation.CreateReadOnlySpanFromRva, rewritten.Instructions[2].Operation);
    }

    public enum Fault
    {
        PartOfTheData,
        MoreThanTheData,
        EmptySpan,
        WritableField,
        InstanceField,
        NoData,
        StaticConstructor,
        StaticFieldValue,
        OtherConstructor,
        OtherType,
        ReferenceElement,
        BranchIntoLength,
        BranchIntoConstruction,
        LengthNotConstant,
        NotConstructed,
        OtherFieldOperand,
        UnresolvedConstructor,
        StaticMethod,
        OtherMethod,
        OpenElement,
        PointerSecond,
        WideLength,
    }

    [Theory]
    [InlineData(Fault.PartOfTheData)]
    [InlineData(Fault.MoreThanTheData)]
    [InlineData(Fault.EmptySpan)]
    [InlineData(Fault.WritableField)]
    [InlineData(Fault.InstanceField)]
    [InlineData(Fault.NoData)]
    [InlineData(Fault.StaticConstructor)]
    [InlineData(Fault.StaticFieldValue)]
    [InlineData(Fault.OtherConstructor)]
    [InlineData(Fault.OtherType)]
    [InlineData(Fault.ReferenceElement)]
    [InlineData(Fault.BranchIntoLength)]
    [InlineData(Fault.BranchIntoConstruction)]
    [InlineData(Fault.LengthNotConstant)]
    [InlineData(Fault.NotConstructed)]
    [InlineData(Fault.OtherFieldOperand)]
    [InlineData(Fault.UnresolvedConstructor)]
    [InlineData(Fault.StaticMethod)]
    [InlineData(Fault.OtherMethod)]
    [InlineData(Fault.OpenElement)]
    [InlineData(Fault.PointerSecond)]
    [InlineData(Fault.WideLength)]
    public void AnythingElsePreservesTheOriginalBody(Fault fault)
    {
        var fixture = new Fixture(fault: fault);
        var body = fixture.Body(fault switch
        {
            Fault.PartOfTheData => 3,
            Fault.MoreThanTheData => 5,
            Fault.EmptySpan => 0,
            _ => 4,
        }, fault);

        Assert.Same(body, fixture.Rewrite(body));
    }

    [Fact]
    public void NullBodyFailsBeforeRewriting() =>
        Assert.Throws<ArgumentNullException>(() => new Fixture().Rewrite(null!));

    private sealed class Fixture : IFieldRepository, ITypeIdentityResolver, ITypeDefinitionResolver, IMethodRepository
    {
        private static readonly AssemblyIdentity Core = new("NetWasm.CoreLib");
        private readonly MethodInstanceModel _constructor;
        private readonly TypeDefinitionModel _ownerDefinition;
        private readonly MethodDefinitionModel _typeInitializer;
        private readonly Fault? _fault;

        public Fixture(string element = "u1", int size = 1, Fault? fault = null)
        {
            _fault = fault;
            ElementType = fault switch
            {
                Fault.ReferenceElement => CliTypeIdentity.Named(Core, "System", "String", isValueType: false),
                Fault.OpenElement => CliTypeIdentity.GenericParameter(method: false, 0),
                _ => CliTypeIdentity.Primitive(element, CliValueKind.I4),
            };
            SpanType = CliTypeIdentity.GenericInstantiation(
                CliTypeIdentity.Named(Core, "System", fault == Fault.OtherType ? "Span`1" : "ReadOnlySpan`1", isValueType: true),
                [ElementType]);
            var pointer = CliTypeIdentity.UnmanagedPointer(CliTypeIdentity.FromStackKind(CliValueKind.Void));
            var int32 = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
            var constructor = new MethodDefinitionModel(
                new(Core, 1), new(Core, 2), fault == Fault.OtherMethod ? "Create" : ".ctor", fault == Fault.StaticMethod,
                fault switch
                {
                    Fault.OtherConstructor => MethodSignatureModel.Create(CliValueKind.Void, CliValueKind.I4),
                    Fault.PointerSecond => MethodSignatureModel.Create(CliTypeIdentity.FromStackKind(CliValueKind.Void), int32, pointer),
                    Fault.WideLength => MethodSignatureModel.Create(CliTypeIdentity.FromStackKind(CliValueKind.Void), pointer,
                        CliTypeIdentity.Primitive("i8", CliValueKind.I8)),
                    _ => MethodSignatureModel.Create(CliTypeIdentity.FromStackKind(CliValueKind.Void), pointer, int32),
                },
                1);
            _constructor = new(constructor, SpanType, [], constructor.Signature);
            Owner = CliTypeIdentity.Named(Core, "", "<PrivateImplementationDetails>", isValueType: false);
            _typeInitializer = new(new(Core, 5), new(Core, 4), ".cctor", true, MethodSignatureModel.Create(CliValueKind.Void), 1);
            _ownerDefinition = new(new(Core, 4), "", "<PrivateImplementationDetails>", false, [new(Core, 3)],
                fault == Fault.StaticConstructor ? [_typeInitializer.Key] : []);
            var data = new FieldDefinitionModel(new(Core, 3), _ownerDefinition.Key, "Data",
                CliTypeIdentity.Named(Core, "", "__StaticArrayInitTypeSize4", isValueType: true),
                fault != Fault.InstanceField)
            {
                InitialData = fault == Fault.NoData ? [] : [.. Enumerable.Range(1, 4 * size).Select(static value => (byte)value)],
                IsInitOnly = fault != Fault.WritableField,
            };
            Field = new(data, Owner, data.SignatureType);
        }

        public CliTypeIdentity ElementType { get; }
        public CliTypeIdentity SpanType { get; }
        public CliTypeIdentity Owner { get; }
        public FieldInstanceModel Field { get; }

        public CilMethodBody Body(int length, Fault? fault = null) => new(
            new MethodDefinitionModel(new(Core, 10), new(Core, 11), "get_Table", true, MethodSignatureModel.Create(SpanType), 1),
            2,
            [],
            [
                new(0, 1, fault == Fault.StaticFieldValue ? CilOperation.LoadStaticField : CilOperation.LoadStaticFieldAddress,
                    fault == Fault.OtherFieldOperand ? new CilOperand.None() : new CilOperand.Entity(Field.Definition.Key)),
                fault == Fault.LengthNotConstant
                    ? new(1, 2, CilOperation.LoadArgument, new CilOperand.Index(0))
                    : new(1, 2, CilOperation.LoadInt32, new CilOperand.ConstantI4(length)),
                new(2, 3, fault == Fault.NotConstructed ? CilOperation.Call : CilOperation.NewObject,
                    new CilOperand.MethodInstance(_constructor)),
                fault switch
                {
                    Fault.BranchIntoLength => new(3, 4, CilOperation.Branch, new CilOperand.BranchTarget(1)),
                    Fault.BranchIntoConstruction => new(3, 4, CilOperation.Branch, new CilOperand.BranchTarget(2)),
                    _ => new(3, 4, CilOperation.Return, new CilOperand.None()),
                },
            ]);

        public CilMethodBody Rewrite(CilMethodBody body) =>
            new RvaPointerSpanRewriter(
                new FixedCalledMethodResolver(_fault == Fault.UnresolvedConstructor ? null : _constructor),
                this, this, this, this)
                .Rewrite(body);

        public FieldDefinitionModel GetField(EntityKey key) =>
            key == Field.Definition.Key ? Field.Definition : throw new KeyNotFoundException();

        public CliTypeIdentity GetTypeIdentity(EntityKey type) =>
            type == _ownerDefinition.Key ? Owner : throw new KeyNotFoundException();

        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) =>
            identity.Equals(Owner) ? _ownerDefinition : throw new KeyNotFoundException();

        public MethodDefinitionModel GetMethod(EntityKey key) =>
            key == _typeInitializer.Key ? _typeInitializer : throw new KeyNotFoundException();
    }

    private sealed class FixedCalledMethodResolver(MethodInstanceModel? target) : ICalledMethodResolver
    {
        public MethodInstanceModel? Resolve(CilInstruction instruction) => target;
    }
}
