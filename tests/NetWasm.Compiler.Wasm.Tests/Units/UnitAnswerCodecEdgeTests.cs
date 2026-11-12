using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Units;

namespace NetWasm.Compiler.Wasm.Tests.Units;

// A kept answer reads back as written whatever it found, and bytes that are not an answer
// of its kind are refused.
public sealed class UnitAnswerCodecEdgeTests
{
    private static readonly EntityKey Initializer = new(new AssemblyIdentity("Program"), 0x06000001);

    public static TheoryData<string> Answers => new()
    {
        "object layout absent", "guard absent", "guard direct without function", "guard constructed with function",
        "exception metadata absent", "stack trace locations absent",
    };

    private static (UnitQueryKind Kind, object Value) Answer(string answer) => answer switch
    {
        "object layout absent" => (UnitQueryKind.ObjectLayoutLookup, new UnitLookup<ObjectLayout>(false, default)),
        "guard absent" => (UnitQueryKind.StaticInitializerGuard, new UnitLookup<StaticInitializerGuard>(false, null)),
        "guard direct without function" => (UnitQueryKind.StaticInitializerGuard,
            new UnitLookup<StaticInitializerGuard>(true, new(64, Initializer, null) { FunctionIndex = OptionalFunctionIndex.Missing })),
        "guard constructed with function" => (UnitQueryKind.StaticInitializerGuard,
            new UnitLookup<StaticInitializerGuard>(true, new(64, null, "Box`1<i4>") { FunctionIndex = OptionalFunctionIndex.At(7) })),
        "exception metadata absent" => (UnitQueryKind.ExceptionMetadata, new UnitLookup<ExceptionGroupMetadata>(false, default)),
        _ => (UnitQueryKind.StackTraceLocationSymbols,
            new UnitLookup<ImmutableArray<StackTraceLocationSymbol>>(false, default)),
    };

    [Theory]
    [MemberData(nameof(Answers))]
    public void AnAnswerReadsBackAsItWasWrittenWhateverItFound(string answer)
    {
        var (kind, value) = Answer(answer);

        var bytes = UnitAnswerCodec.Encode(kind, "Program::Run@00000004", value)!;

        Assert.Equal(bytes, UnitAnswerCodec.Encode(kind, "Program::Run@00000004",
            UnitAnswerCodec.Decode(kind, "Program::Run@00000004", bytes, new NoMethods(), new NoFields())));
    }

    [Fact]
    public void BytesThatAreNotAnAnswerOfTheirKindAreRefused()
    {
        var bytes = UnitAnswerCodec.Encode(UnitQueryKind.StringTypeId, "", 7)!;

        Assert.Throws<InvalidDataException>(() =>
            UnitAnswerCodec.Decode(UnitQueryKind.StringTypeId, "", [(byte)(bytes[0] + 1), .. bytes[1..]], new NoMethods(), new NoFields()));
        Assert.Throws<InvalidDataException>(() =>
            UnitAnswerCodec.Decode(UnitQueryKind.StringTypeId, "", [.. bytes, 0], new NoMethods(), new NoFields()));
    }

    // The whole-program plans are kept beside the module, never as an answer.
    [Fact]
    public void AKindThatKeepsNoAnswerIsNotWrittenOrRead()
    {
        Assert.Null(UnitAnswerCodec.Encode(UnitQueryKind.TargetPlans, "", new object()));
        Assert.Null(UnitAnswerCodec.Encode(UnitQueryKind.StringTypeId, "", null));
        Assert.Throws<NotSupportedException>(() =>
            UnitAnswerCodec.Decode(UnitQueryKind.TargetPlans, "", [UnitAnswerCodec.Encode(UnitQueryKind.StringTypeId, "", 7)![0]],
                new NoMethods(), new NoFields()));
    }

    private sealed class NoMethods : IMethodRepository
    {
        public MethodDefinitionModel GetMethod(EntityKey key) => throw new NotSupportedException();
    }

    private sealed class NoFields : IFieldRepository
    {
        public FieldDefinitionModel GetField(EntityKey key) => throw new NotSupportedException();
    }
}
