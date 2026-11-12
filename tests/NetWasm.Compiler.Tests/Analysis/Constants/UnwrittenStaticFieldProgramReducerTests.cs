using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Analysis.Constants;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests.Analysis.Constants;

using static NetWasm.Compiler.Tests.Analysis.StaticArrays.StaticArrayTestSupport;

// A static field no reachable code writes reads as its default, and its owner's static constructor
// still runs where it has one; any write, address, root or reflection descriptor keeps the load.
public sealed class UnwrittenStaticFieldProgramReducerTests
{
    private static readonly AssemblyIdentity Fixture = new("UnwrittenStatics");
    private static readonly EntityKey PlainKey = new(Fixture, 0x02000001);
    private static readonly EntityKey InitializedKey = new(Fixture, 0x02000002);
    private static readonly EntityKey ModeKey = new(Fixture, 0x02000003);
    private static readonly EntityKey ShapeKey = new(Fixture, 0x02000004);
    private static readonly CliTypeIdentity Plain = CliTypeIdentity.Named(Fixture, "Fixture", "Plain", false);
    private static readonly CliTypeIdentity Mode = CliTypeIdentity.Named(Fixture, "Fixture", "Mode", true);
    private static readonly CliTypeIdentity Shape = CliTypeIdentity.Named(Fixture, "Fixture", "Shape", true);
    private static readonly MethodDefinitionModel TypeInitializer =
        new(new(Fixture, 0x06000002), InitializedKey, ".cctor", true, MethodSignatureModel.Create(CliValueKind.Void), 1);

    private static int s_nextField = 1;

    [Fact]
    public void AStaticFieldNothingWritesReadsAsItsDefault()
    {
        var count = Field(PlainKey, CliTypeIdentity.Primitive("i4", CliValueKind.I4));
        var big = Field(PlainKey, CliTypeIdentity.Primitive("i8", CliValueKind.I8));
        var ratio = Field(PlainKey, CliTypeIdentity.Primitive("f8", CliValueKind.F8));
        var single = Field(PlainKey, CliTypeIdentity.Primitive("f4", CliValueKind.F4));
        var flag = Field(PlainKey, CliTypeIdentity.Primitive("bool", CliValueKind.I4));
        var name = Field(PlainKey, CliTypeIdentity.Primitive("string", CliValueKind.ManagedReference, isValueType: false));
        var mode = Field(PlainKey, Mode);
        var facts = new Facts(count, big, ratio, single, flag, name, mode);
        var run = Body(0x06000010, [.. new[] { count, big, ratio, single, flag, name, mode }.SelectMany(field =>
            new[] { Load(field), I(0, CilOperation.Pop) }), I(0, CilOperation.Return)]);
        var specializer = new RecordingSpecializer();

        var changes = Reducer(facts, specializer).Reduce(Program(run), ReachabilityRoots.Empty);

        var folded = Assert.Single(changes).Value;
        Assert.Equal(
            [CilOperation.LoadInt32, CilOperation.Pop, CilOperation.LoadInt64, CilOperation.Pop,
             CilOperation.LoadFloat64, CilOperation.Pop, CilOperation.LoadFloat32, CilOperation.Pop,
             CilOperation.LoadInt32, CilOperation.Pop, CilOperation.LoadNull, CilOperation.Pop,
             CilOperation.LoadInt32, CilOperation.Pop, CilOperation.Return],
            folded.Instructions.Select(instruction => instruction.Operation));
        Assert.All(folded.Instructions.Where(instruction => instruction.Operand is CilOperand.ConstantI4),
            instruction => Assert.Equal(new CilOperand.ConstantI4(0), instruction.Operand));
        Assert.Equal(new CilOperand.ConstantI8(0), folded.Instructions[2].Operand);
        Assert.Equal(new CilOperand.ConstantF8(0), folded.Instructions[4].Operand);
        Assert.Equal(new CilOperand.ConstantF4(0), folded.Instructions[6].Operand);
        Assert.Equal(1, specializer.Calls);
    }

    // The first access to a field of a type with a static constructor runs it, so the fold keeps
    // that access through the field's address; the dropped address writes nothing.
    [Fact]
    public void AFieldWhoseOwnerHasAStaticConstructorStillRunsIt()
    {
        var value = Field(InitializedKey, CliTypeIdentity.Primitive("i4", CliValueKind.I4));
        var facts = new Facts(value);
        var run = Body(0x06000011, [Load(value), I(0, CilOperation.Return)]);
        var reducer = Reducer(facts, new RecordingSpecializer());

        var folded = Assert.Single(reducer.Reduce(Program(run), ReachabilityRoots.Empty)).Value;

        Assert.Equal([CilOperation.LoadStaticFieldAddress, CilOperation.Pop, CilOperation.LoadInt32, CilOperation.Return],
            folded.Instructions.Select(instruction => instruction.Operation));
        var rewritten = Body(0x06000011, [.. folded.Instructions]);
        Assert.Empty(reducer.Reduce(Program(rewritten), ReachabilityRoots.Empty));
        Assert.DoesNotContain(value.Key, new StaticFieldWriteCollector(facts).Collect(Program(rewritten), ReachabilityRoots.Empty));
    }

    [Fact]
    public void AnyWriteAddressRootOrDescriptorKeepsTheLoad()
    {
        var stored = Field(PlainKey, CliTypeIdentity.Primitive("i4", CliValueKind.I4));
        var addressed = Field(PlainKey, CliTypeIdentity.Primitive("i4", CliValueKind.I4));
        var rooted = Field(PlainKey, CliTypeIdentity.Primitive("i4", CliValueKind.I4));
        var described = Field(PlainKey, CliTypeIdentity.Primitive("i4", CliValueKind.I4));
        var facts = new Facts(stored, addressed, rooted, described);
        var writer = Body(0x06000012,
        [
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(7)),
            I(1, CilOperation.StoreStaticField, new CilOperand.Entity(stored.Key)),
            I(2, CilOperation.LoadStaticFieldAddress, new CilOperand.Entity(addressed.Key)),
            I(3, CilOperation.LoadInt32, new CilOperand.ConstantI4(3)),
            I(4, CilOperation.StoreObject, new CilOperand.TypeIdentity(CliTypeIdentity.Primitive("i4", CliValueKind.I4))),
            I(5, CilOperation.Return),
        ]);
        var reader = Body(0x06000013, [Load(stored), Load(addressed), Load(rooted), Load(described), I(0, CilOperation.Return)]);
        var program = Program(writer, reader) with
        {
            FieldDescriptors = ImmutableDictionary<string, FieldInstanceModel>.Empty.Add("described",
                new FieldInstanceModel(described, Plain, described.SignatureType)),
        };

        var collector = new StaticFieldWriteCollector(facts);
        var written = collector.Collect(program, new([], [rooted.Key], []));
        Assert.Equal([stored.Key, addressed.Key, rooted.Key, described.Key], written.OrderBy(key => key.MetadataToken));
        Assert.Empty(Reducer(facts, new RecordingSpecializer()).Reduce(program, new([], [rooted.Key], [])));
        Assert.Throws<ArgumentNullException>(() => new StaticFieldWriteCollector(null!));
        Assert.Throws<ArgumentNullException>(() => collector.Collect(null!, ReachabilityRoots.Empty));
        Assert.Throws<ArgumentNullException>(() => collector.Collect(program, null!));
    }

    // A volatile load keeps its memory semantics; a literal or a field with initial data holds other
    // data than zero, and a struct has no constant load.
    [Fact]
    public void VolatileLoadsLiteralsInitialDataAndStructsKeepTheirLoads()
    {
        var plain = Field(PlainKey, CliTypeIdentity.Primitive("i4", CliValueKind.I4));
        var literal = Field(PlainKey, CliTypeIdentity.Primitive("i4", CliValueKind.I4)) with { IsLiteral = true };
        var data = Field(PlainKey, CliTypeIdentity.Primitive("i4", CliValueKind.I4)) with { InitialData = [1, 0, 0, 0] };
        var shape = Field(PlainKey, Shape);
        var instance = new FieldDefinitionModel(new(Fixture, 0x04000099), PlainKey, "instance", CliValueKind.I4, false);
        var facts = new Facts(plain, literal, data, shape, instance);
        var run = Body(0x06000014,
        [
            I(0, CilOperation.Volatile),
            Load(plain),
            Load(literal),
            Load(data),
            Load(shape),
            Load(instance),
            I(0, CilOperation.Return),
        ]);

        Assert.Empty(Reducer(facts, new RecordingSpecializer()).Reduce(Program(run), ReachabilityRoots.Empty));
    }

    [Fact]
    public void ReducerRejectsMissingCollaborators()
    {
        var facts = new Facts();
        var collector = new StaticFieldWriteCollector(facts);
        var specializer = new RecordingSpecializer();
        Assert.Throws<ArgumentNullException>(() => new UnwrittenStaticFieldProgramReducer(null!, facts, facts, facts, facts, specializer));
        Assert.Throws<ArgumentNullException>(() => new UnwrittenStaticFieldProgramReducer(collector, null!, facts, facts, facts, specializer));
        Assert.Throws<ArgumentNullException>(() => new UnwrittenStaticFieldProgramReducer(collector, facts, null!, facts, facts, specializer));
        Assert.Throws<ArgumentNullException>(() => new UnwrittenStaticFieldProgramReducer(collector, facts, facts, null!, facts, specializer));
        Assert.Throws<ArgumentNullException>(() => new UnwrittenStaticFieldProgramReducer(collector, facts, facts, facts, null!, specializer));
        Assert.Throws<ArgumentNullException>(() => new UnwrittenStaticFieldProgramReducer(collector, facts, facts, facts, facts, null!));
        var reducer = Reducer(facts, specializer);
        Assert.Throws<ArgumentNullException>(() => reducer.Reduce(null!, ReachabilityRoots.Empty));
        Assert.Throws<ArgumentNullException>(() => reducer.Reduce(Program(Body(0x06000015, [I(0, CilOperation.Return)])), null!));
    }

    private static UnwrittenStaticFieldProgramReducer Reducer(Facts facts, IMethodSpecializer specializer) =>
        new UnwrittenStaticFieldProgramReducer(new StaticFieldWriteCollector(facts), facts, facts, facts, facts, specializer);

    private static FieldDefinitionModel Field(EntityKey owner, CliTypeIdentity type) =>
        new(new(Fixture, 0x04000000 + s_nextField++), owner, "field" + s_nextField, type.StackKind, true) { SignatureType = type };

    private static CilInstruction Load(FieldDefinitionModel field) =>
        I(0, field.IsStatic ? CilOperation.LoadStaticField : CilOperation.LoadField, new CilOperand.Entity(field.Key));

    private static ManagedMethodBody Body(int token, CilInstruction[] instructions)
    {
        var signature = MethodSignatureModel.Create(CliValueKind.Void);
        var method = new MethodInstanceModel(new(new(Fixture, token), PlainKey, "Run" + token, true, signature, 1), Plain, [], signature);
        var numbered = instructions.Select((instruction, index) => instruction with { Offset = index, NextOffset = index + 1 });
        return Managed(new CilMethodBody(method.Definition, 8, [], [.. numbered]) { MethodInstance = method });
    }

    private static ReachableProgram Program(params ManagedMethodBody[] bodies) => new(null,
        bodies.ToImmutableDictionary(body => body.Method.Definition.Key), [], [], [], [], [], [],
        ImmutableDictionary<EntityKey, MethodRootMap>.Empty, ImmutableDictionary<CliTypeIdentity, MethodInstanceModel>.Empty, []);

    private sealed class RecordingSpecializer : IMethodSpecializer
    {
        public int Calls { get; private set; }

        public CilMethodBody Rewrite(CilMethodBody body)
        {
            Calls++;
            return body;
        }
    }

    private sealed class Facts(params FieldDefinitionModel[] fields) :
        IFieldRepository, ITypeIdentityResolver, ITypeDefinitionResolver, IMethodRepository
    {
        private readonly Dictionary<EntityKey, FieldDefinitionModel> _fields = fields.ToDictionary(field => field.Key);

        public FieldDefinitionModel GetField(EntityKey key) => _fields[key];

        public CliTypeIdentity GetTypeIdentity(EntityKey type) =>
            type == InitializedKey ? CliTypeIdentity.Named(Fixture, "Fixture", "Initialized", false) : Plain;

        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) => identity.FullName switch
        {
            "Fixture.Initialized" => new(InitializedKey, "Fixture", "Initialized", false, [], [TypeInitializer.Key]),
            "Fixture.Mode" => new(ModeKey, "Fixture", "Mode", true, [], [])
            {
                IsEnum = true,
                EnumUnderlyingType = CliTypeIdentity.Primitive("i4", CliValueKind.I4),
            },
            "Fixture.Shape" => new(ShapeKey, "Fixture", "Shape", true, [], []),
            _ => new(PlainKey, "Fixture", "Plain", false, [], []),
        };

        public MethodDefinitionModel GetMethod(EntityKey key) => key == TypeInitializer.Key
            ? TypeInitializer
            : throw new KeyNotFoundException(key.ToString());
    }
}
