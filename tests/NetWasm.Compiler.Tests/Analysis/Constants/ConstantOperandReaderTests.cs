using NetWasm.Compiler.Analysis.Constants;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Analysis.Constants;

using static ConstantRewriteTestSupport;

public sealed class ConstantOperandReaderTests
{
    [Fact]
    public void RejectsMissingDependenciesAndProducers()
    {
        var facts = new MethodFacts();
        Assert.Throws<ArgumentNullException>(() => new ConstantOperandReader(null!, facts, facts, Constructors(facts)));
        Assert.Throws<ArgumentNullException>(() => new ConstantOperandReader(facts, null!, facts, Constructors(facts)));
        Assert.Throws<ArgumentNullException>(() => new ConstantOperandReader(facts, facts, null!, Constructors(facts)));
        Assert.Throws<ArgumentNullException>(() => new ConstantOperandReader(facts, facts, facts, null!));
        Assert.Throws<ArgumentNullException>(() => AsReader(Operands(facts)).Read(null!));
    }

    [Fact]
    public void ReadsIntegerNullAndStringLiterals()
    {
        var reader = AsReader(Operands(new MethodFacts()));

        Assert.Equal(3, reader.MaximumLength);
        Assert.Equal(new ScalarConstant.Int32(-7), reader.Read([Instruction(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(-7))]));
        Assert.Equal(new ScalarConstant.Null(), reader.Read([Instruction(0, CilOperation.LoadNull)]));
        Assert.Equal(new ScalarConstant.String("Admin"), reader.Read([Instruction(0, CilOperation.LoadString, new CilOperand.UserString("Admin"))]));
        Assert.Null(reader.Read([Instruction(0, CilOperation.LoadArgument, new CilOperand.Index(0))]));
        Assert.Null(reader.Read([]));
    }

    [Fact]
    public void ReadsByteSpansOverReadOnlyStaticData()
    {
        var facts = Facts();
        var reader = AsReader(Operands(facts));
        var field = facts.Fields[FieldKey];

        Assert.Equal(new ScalarConstant.Bytes([80, 78, 71]), reader.Read(Utf8Literal(3)));
        Assert.Equal(new ScalarConstant.Bytes([]), reader.Read(Utf8Literal(0)));
        Assert.Equal(new ScalarConstant.Bytes([80, 78, 71, 0]), reader.Read(
            [Instruction(0, CilOperation.CreateReadOnlySpanFromRva, new CilOperand.ReadOnlySpanData(new(field, Owner, Byte), Span(Byte), Byte, 4))]));
        Assert.Null(reader.Read(
            [Instruction(0, CilOperation.CreateReadOnlySpanFromRva, new CilOperand.ReadOnlySpanData(new(field, Owner, Integer), Span(Integer), Integer, 1))]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void ReadsOnlyTheBytePointerSpanConstructor(int scenario)
    {
        var constructor = scenario switch
        {
            0 => Constructor(Span(Byte)) with { Definition = Constructor(Span(Byte)).Definition with { Name = "Create" } },
            1 => Constructor(Span(Byte)) with { Definition = Constructor(Span(Byte)).Definition with { IsStatic = true } },
            2 => Constructor(Owner),
            3 => Constructor(Span(Integer)),
            4 => Constructor(Span(Byte), Integer, Integer),
            _ => null,
        };
        var producers = Utf8Literal(3);
        producers[2] = producers[2] with { Operand = constructor is null ? new CilOperand.None() : new CilOperand.MethodInstance(constructor) };

        Assert.Null(AsReader(Operands(Facts())).Read(producers));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void ReadsOnlyReadOnlyDataWithoutAPreciseInitializer(int scenario)
    {
        var facts = Facts();
        var field = facts.Fields[FieldKey];
        facts.Fields[FieldKey] = scenario switch
        {
            0 => field with { IsStatic = false },
            1 => field with { IsInitOnly = false },
            2 => field with { InitialData = [] },
            _ => field,
        };
        facts.HasInitializer = scenario == 5;
        var length = scenario switch { 3 => 5, 4 => -1, _ => 3 };

        Assert.Null(AsReader(Operands(facts)).Read(Utf8Literal(length)));
    }

    [Fact]
    public void ReadsDataWhoseInitializerMayRunLater()
    {
        var facts = Facts();
        facts.HasInitializer = true;
        facts.Definition = facts.Definition with { IsBeforeFieldInit = true };

        Assert.Equal(new ScalarConstant.Bytes([80]), AsReader(Operands(facts)).Read(Utf8Literal(1)));
    }

    [Fact]
    public void ComparesByteConstantsByContent()
    {
        var bytes = new ScalarConstant.Bytes([1, 2]);
        Assert.Equal(new ScalarConstant.Bytes([1, 2]).GetHashCode(), bytes.GetHashCode());
        Assert.NotEqual(new ScalarConstant.Bytes([2, 1]), bytes);
        Assert.False(bytes.Equals(null));
    }

    internal static readonly EntityKey FieldKey = new(Assembly, 0x04000001);
    internal static readonly CliTypeIdentity Byte = CliTypeIdentity.Primitive("u1", CliValueKind.I4);
    private static readonly CliTypeIdentity Integer = CliTypeIdentity.Primitive("i4", CliValueKind.I4);

    internal static CliTypeIdentity Span(CliTypeIdentity element) => CliTypeIdentity.GenericInstantiation(
        CliTypeIdentity.Named(Assembly, "System", "ReadOnlySpan`1", true), [element]);

    internal static MethodFacts Facts()
    {
        var facts = new MethodFacts();
        facts.Fields[FieldKey] = new FieldDefinitionModel(FieldKey, TypeKey, "PNG", CliValueKind.ValueType, true)
        {
            InitialData = [80, 78, 71, 0],
            IsInitOnly = true,
        };
        return facts;
    }

    internal static MethodInstanceModel Constructor(CliTypeIdentity owner, params CliTypeIdentity[] parameters)
    {
        var signature = new MethodSignatureModel(CliTypeIdentity.FromStackKind(CliValueKind.Void),
            parameters.Length == 0
                ? [CliTypeIdentity.UnmanagedPointer(CliTypeIdentity.FromStackKind(CliValueKind.Void)), Integer]
                : [.. parameters]);
        return new(new(new(Assembly, 0x06000100), TypeKey, ".ctor", false, signature, 1), owner, [], signature);
    }

    internal static CilInstruction[] Utf8Literal(int length, int offset = 0) =>
    [
        Instruction(offset, CilOperation.LoadStaticFieldAddress, new CilOperand.Entity(FieldKey)),
        Instruction(offset + 1, CilOperation.LoadInt32, new CilOperand.ConstantI4(length)),
        Instruction(offset + 2, CilOperation.NewObject, new CilOperand.MethodInstance(Constructor(Span(Byte)))),
    ];

    private static IConstantOperandReader AsReader(IConstantOperandReader reader) => reader;
}
