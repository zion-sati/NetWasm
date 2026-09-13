using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class ExceptionObjectStateReaderTests
{
    [Fact]
    public void RejectsMissingLayoutCollaborators()
    {
        var layouts = new RecordingLayoutProvider();
        var program = new FakeProgram();

        Assert.Throws<ArgumentNullException>(() => new ExceptionObjectStateReader(
            null!, layouts, new ExceptionFieldLayoutResolver(layouts, program, program, layouts)));
        Assert.Throws<ArgumentNullException>(() => new ExceptionObjectStateReader(
            layouts, null!, new ExceptionFieldLayoutResolver(layouts, program, program, layouts)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExceptionBaseReadsTypeMessageAndLengthAtTargetWidth(bool memory64)
    {
        var target = memory64 ? WasmTargetLayout.Wasm64 : WasmTargetLayout.Wasm32;
        var layouts = new RecordingLayoutProvider(target);
        var fieldLayouts = new FixedFieldLayoutProvider(28);
        var code = new RecordingInstructionWriter();
        IExceptionObjectStateReader reader = new[]
        {
            new ExceptionObjectStateReader(
                layouts,
                layouts, new ExceptionFieldLayoutResolver(
                new FixedDescriptorSource(),
                new ExceptionTypeRepository(),
                new MessageFieldRepository(),
                fieldLayouts)),
        }.Cast<IExceptionObjectStateReader>().Single();

        reader.Emit(code, 0, 1, 2, 3);

        Assert.Equal(MessageFieldRepository.MessageKey, fieldLayouts.Request);
        Assert.Contains(
            memory64 ? WasmOpcodes.I64EqualZero : WasmOpcodes.I32EqualZero,
            code.ToArray());
        Assert.Contains(WasmOpcodes.I32Load, code.ToArray());
        Assert.Contains(WasmOpcodes.Else, code.ToArray());
    }

    [Fact]
    public void MissingExceptionBaseUsesTargetWidthForNullMessage()
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.Wasm64);
        var program = new FakeProgram();
        var code = new RecordingInstructionWriter();
        IExceptionObjectStateReader reader = new[]
        {
            new ExceptionObjectStateReader(
                layouts,
                layouts, new ExceptionFieldLayoutResolver(
                layouts,
                program,
                program,
                layouts)),
        }.Cast<IExceptionObjectStateReader>().Single();

        reader.Emit(code, 0, 1, 2, 3);

        Assert.Contains(WasmOpcodes.I64Constant, code.ToArray());
    }

    [Fact]
    public void MissingExceptionBaseProducesNullRawStateWithoutCalls()
    {
        var layouts = new RecordingLayoutProvider();
        var program = new FakeProgram();
        var outputBuffer = new WasmBinaryBuffer();
        var output = new WasmBinaryWriter(outputBuffer);
        var code = new WasmInstructionWriter(output);
        Action<IExceptionObjectStateReader> contract = reader => reader.Emit(
            code,
            exceptionLocal: 0,
            typeIdLocal: 1,
            messageLocal: 2,
            messageLengthLocal: 3);

        contract(new ExceptionObjectStateReader(
            layouts,
            layouts, new ExceptionFieldLayoutResolver(
            layouts,
            program,
            program,
            layouts)));

        var body = new WasmBinarySnapshotReader(outputBuffer).Read();
        Assert.Equal(-1, Array.IndexOf(body, WasmOpcodes.Call));
        Assert.Equal(3, body.Count(value => value == WasmOpcodes.LocalSet));
    }

    [Fact]
    public void ExceptionFieldLayoutResolverResolvesReachableFieldOffset()
    {
        var resolver = CreateFieldResolver(
            new FixedDescriptorSource(),
            new ExceptionTypeRepository(),
            new MessageFieldRepository(),
            new FixedFieldLayoutProvider(27));

        Assert.Equal(27, resolver.Resolve("_message"));
        Assert.Throws<ArgumentException>(() => resolver.Resolve(""));
    }

    [Fact]
    public void ExceptionFieldLayoutResolverReturnsNullWithoutExceptionDescriptor()
    {
        var resolver = CreateFieldResolver(
            new EmptyDescriptorSource(),
            new ExceptionTypeRepository(),
            new MessageFieldRepository(),
            new FixedFieldLayoutProvider(27));

        Assert.Null(resolver.Resolve("_message"));
    }

    private sealed class EmptyDescriptorSource : ITypeDescriptorSource
    {
        public ImmutableArray<TypeDescriptorLayout> TypeDescriptors => [];
        public ImmutableArray<ConstructedTypeDescriptorLayout>
            ConstructedTypeDescriptors => [];
        public ImmutableArray<ValueTypeDescriptorLayout> ValueTypeDescriptors => [];
    }

    private static readonly Func<
        ITypeDescriptorSource,
        ITypeRepository,
        IFieldRepository,
        IInstanceFieldLayoutProvider,
        IExceptionFieldLayoutResolver> CreateFieldResolver =
        static (descriptors, types, fields, layouts) =>
            new ExceptionFieldLayoutResolver(descriptors, types, fields, layouts);

    private sealed class FixedDescriptorSource : ITypeDescriptorSource
    {
        public ImmutableArray<TypeDescriptorLayout> TypeDescriptors =>
            [new(EmitterTestSupport.TypeKey, 1, 0, 16, 0, 0, null)];
        public ImmutableArray<ConstructedTypeDescriptorLayout>
            ConstructedTypeDescriptors => [];
        public ImmutableArray<ValueTypeDescriptorLayout> ValueTypeDescriptors => [];
    }

    private sealed class ExceptionTypeRepository : ITypeRepository
    {
        public TypeDefinitionModel GetTypeDefinition(EntityKey key) => new(
            key,
            "System",
            "Exception",
            false,
            [MessageFieldRepository.MessageKey],
            []);
    }

    private sealed class MessageFieldRepository : IFieldRepository
    {
        public static EntityKey MessageKey { get; } = new(EmitterTestSupport.Assembly, 99);

        public FieldDefinitionModel GetField(EntityKey key) => new(
            key,
            EmitterTestSupport.TypeKey,
            "_message",
            CliValueKind.ManagedReference,
            false);
    }

    private sealed class FixedFieldLayoutProvider(int offset) :
        IInstanceFieldLayoutProvider
    {
        public EntityKey? Request { get; private set; }

        public FieldLayout GetFieldLayout(FieldInstanceModel field) =>
            new(offset);

        public FieldLayout GetFieldLayout(EntityKey field)
        {
            Request = field;
            return new(offset);
        }
    }
}
