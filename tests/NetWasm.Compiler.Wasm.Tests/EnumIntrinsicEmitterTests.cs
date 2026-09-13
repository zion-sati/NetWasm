using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class EnumIntrinsicEmitterTests
{
    private static readonly CliTypeIdentity StateType = CliTypeIdentity.Named(
        new AssemblyIdentity("EnumTests"),
        "Tests",
        "State",
        isValueType: true);

    [Theory]
    [InlineData(true, WasmOpcodes.I64LessThanSigned, WasmOpcodes.I64GreaterThanSigned)]
    [InlineData(false, WasmOpcodes.I64LessThanUnsigned, WasmOpcodes.I64GreaterThanUnsigned)]
    public void CompareToEmitsUnderlyingTypeAwareComparison(
        bool isSigned,
        byte expectedLessThan,
        byte expectedGreaterThan)
    {
        var program = new EnumProgram(isSigned);
        var layouts = new EnumLayouts(program.UnderlyingType);
        var code = new RecordingInstructionWriter();
        Action<IEnumCompareToEmitter> emit = emitter =>
            emitter.EmitCompareTo(
                code,
                CliValueKind.ManagedReference,
                null,
                0,
                1,
                2,
                4,
                3);

        emit(CreateCompareTo(program, layouts));

        var bytes = code.ToArray();
        Assert.Contains(expectedLessThan, bytes);
        Assert.Contains(expectedGreaterThan, bytes);
        Assert.Contains(WasmOpcodes.LocalSet, bytes);
    }

    [Theory]
    [InlineData(true, WasmOpcodes.I32LessThanSigned, WasmOpcodes.I32GreaterThanSigned)]
    [InlineData(false, WasmOpcodes.I32LessThanUnsigned, WasmOpcodes.I32GreaterThanUnsigned)]
    public void CompareToEmitsNarrowUnderlyingTypeAwareComparison(
        bool isSigned,
        byte expectedLessThan,
        byte expectedGreaterThan)
    {
        var program = new EnumProgram(isSigned, wide: false);
        var layouts = new EnumLayouts(program.UnderlyingType);
        var code = new RecordingInstructionWriter();

        ((IEnumCompareToEmitter)CreateCompareTo(program, layouts))
            .EmitCompareTo(
                code,
                CliValueKind.ManagedReference,
                null,
                0,
                1,
                2,
                4,
                3);

        Assert.Contains(expectedLessThan, code.ToArray());
        Assert.Contains(expectedGreaterThan, code.ToArray());
    }

    [Theory]
    [InlineData(true, true, WasmOpcodes.I64LessThanSigned, WasmOpcodes.I64GreaterThanSigned)]
    [InlineData(false, true, WasmOpcodes.I64LessThanUnsigned, WasmOpcodes.I64GreaterThanUnsigned)]
    [InlineData(true, false, WasmOpcodes.I32LessThanSigned, WasmOpcodes.I32GreaterThanSigned)]
    [InlineData(false, false, WasmOpcodes.I32LessThanUnsigned, WasmOpcodes.I32GreaterThanUnsigned)]
    public void ConstrainedCompareToUsesClosedReceiverStorage(
        bool isSigned,
        bool wide,
        byte expectedLessThan,
        byte expectedGreaterThan)
    {
        var program = new EnumProgram(isSigned, wide);
        var layouts = new EnumLayouts(program.UnderlyingType);
        var code = new RecordingInstructionWriter();
        Action<IEnumCompareToEmitter> emit = emitter => emitter.EmitCompareTo(
            code,
            CliValueKind.ManagedAddress,
            StateType,
            0,
            1,
            2,
            4,
            3);

        emit(CreateCompareTo(program, layouts));

        var bytes = code.ToArray();
        Assert.Contains(expectedLessThan, bytes);
        Assert.Contains(expectedGreaterThan, bytes);
    }

    [Fact]
    public void ConstrainedCompareToRequiresItsClosedReceiverType()
    {
        var program = new EnumProgram(isSigned: true);
        var layouts = new EnumLayouts(program.UnderlyingType);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ((IEnumCompareToEmitter)CreateCompareTo(program, layouts)).EmitCompareTo(
                new RecordingInstructionWriter(),
                CliValueKind.ManagedAddress,
                null,
                0,
                1,
                2,
                4,
                3));

        Assert.Contains("closed receiver type", exception.Message);
    }

    [Fact]
    public void ConstrainedCompareToRejectsAnUnreachableReceiverType()
    {
        var program = new EnumProgram(isSigned: true);
        var layouts = new EnumLayouts(program.UnderlyingType);
        var missing = CliTypeIdentity.Named(
            new AssemblyIdentity("Missing"),
            "Tests",
            "Missing",
            isValueType: true);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ((IEnumCompareToEmitter)CreateCompareTo(program, layouts)).EmitCompareTo(
                new RecordingInstructionWriter(),
                CliValueKind.ManagedAddress,
                missing,
                0,
                1,
                2,
                4,
                3));

        Assert.Contains("no reachable enum descriptor", exception.Message);
    }

    [Fact]
    public void EqualsEmitsWideUnderlyingEquality()
    {
        var program = new EnumProgram(isSigned: true);
        var layouts = new EnumLayouts(program.UnderlyingType);
        var code = new RecordingInstructionWriter();
        Action<IEnumEqualsEmitter> emit = emitter =>
            emitter.EmitEquals(
                code,
                CliValueKind.ManagedReference,
                null,
                0,
                1,
                2,
                4,
                3);

        emit(CreateEquals(program, layouts));

        Assert.Contains(WasmOpcodes.I64Equal, code.ToArray());
    }

    [Fact]
    public void EqualsEmitsNarrowUnderlyingEquality()
    {
        var program = new EnumProgram(isSigned: true, wide: false);
        var layouts = new EnumLayouts(program.UnderlyingType);
        var code = new RecordingInstructionWriter();
        Action<IEnumEqualsEmitter> emit = emitter =>
            emitter.EmitEquals(
                code,
                CliValueKind.ManagedReference,
                null,
                0,
                1,
                2,
                4,
                3);

        emit(CreateEquals(program, layouts));

        Assert.Contains(WasmOpcodes.I32Equal, code.ToArray());
    }

    [Theory]
    [InlineData(true, WasmOpcodes.I64Equal)]
    [InlineData(false, WasmOpcodes.I32Equal)]
    public void ConstrainedEqualsUsesClosedReceiverStorage(
        bool wide,
        byte expectedEquality)
    {
        var program = new EnumProgram(isSigned: true, wide);
        var layouts = new EnumLayouts(program.UnderlyingType);
        var code = new RecordingInstructionWriter();
        Action<IEnumEqualsEmitter> emit = emitter => emitter.EmitEquals(
            code,
            CliValueKind.ManagedAddress,
            StateType,
            0,
            1,
            2,
            4,
            3);

        emit(CreateEquals(program, layouts));

        Assert.Contains(expectedEquality, code.ToArray());
    }

    [Fact]
    public void ConstrainedEqualsRequiresItsClosedReceiverType()
    {
        var program = new EnumProgram(isSigned: true);
        var layouts = new EnumLayouts(program.UnderlyingType);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ((IEnumEqualsEmitter)CreateEquals(program, layouts)).EmitEquals(
                new RecordingInstructionWriter(),
                CliValueKind.ManagedAddress,
                null,
                0,
                1,
                2,
                4,
                3));

        Assert.Contains("closed receiver type", exception.Message);
    }

    [Fact]
    public void ConstrainedEqualsRejectsAnUnreachableReceiverType()
    {
        var program = new EnumProgram(isSigned: true);
        var layouts = new EnumLayouts(program.UnderlyingType);
        var missing = CliTypeIdentity.Named(
            new AssemblyIdentity("Missing"),
            "Tests",
            "Missing",
            isValueType: true);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ((IEnumEqualsEmitter)CreateEquals(program, layouts)).EmitEquals(
                new RecordingInstructionWriter(),
                CliValueKind.ManagedAddress,
                missing,
                0,
                1,
                2,
                4,
                3));

        Assert.Contains("no reachable enum descriptor", exception.Message);
    }

    [Fact]
    public void HashCodeEmitsWideUnderlyingHash()
    {
        var program = new EnumProgram(isSigned: true);
        var layouts = new EnumLayouts(program.UnderlyingType);
        var code = new RecordingInstructionWriter();
        Action<IEnumHashCodeEmitter> emit = emitter => emitter.EmitHashCode(
            code,
            CliValueKind.ManagedReference,
            null,
            0,
            1,
            2,
            3);

        emit(CreateHashCode(program, layouts));

        Assert.Contains(WasmOpcodes.I64ShiftRightUnsigned, code.ToArray());
        Assert.Contains(WasmOpcodes.I32Xor, code.ToArray());
    }

    [Fact]
    public void HashCodeLoadsNarrowSignedUnderlyingStorageWithSignExtension()
    {
        var program = new EnumProgram(isSigned: true, wide: false, narrowBytes: 1);
        var layouts = new EnumLayouts(program.UnderlyingType);
        var code = new RecordingInstructionWriter();

        ((IEnumHashCodeEmitter)CreateHashCode(program, layouts)).EmitHashCode(
            code,
            CliValueKind.ManagedReference,
            null,
            0,
            1,
            2,
            3);

        Assert.Contains(WasmOpcodes.I32Load8Signed, code.ToArray());
    }

    [Fact]
    public void ConstrainedHashCodeLoadsTheClosedReceiverStorage()
    {
        var program = new EnumProgram(isSigned: true, wide: false, narrowBytes: 1);
        var layouts = new EnumLayouts(program.UnderlyingType);
        var code = new RecordingInstructionWriter();
        var enumType = CliTypeIdentity.Named(
            new AssemblyIdentity("EnumTests"),
            "Tests",
            "State",
            isValueType: true);

        ((IEnumHashCodeEmitter)CreateHashCode(program, layouts)).EmitHashCode(
            code,
            CliValueKind.ManagedAddress,
            enumType,
            0,
            1,
            2,
            3);

        Assert.Contains(WasmOpcodes.I32Load8Signed, code.ToArray());
    }

    [Fact]
    public void ConstrainedHashCodeRequiresItsClosedReceiverType()
    {
        var program = new EnumProgram(isSigned: true);
        var layouts = new EnumLayouts(program.UnderlyingType);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ((IEnumHashCodeEmitter)CreateHashCode(program, layouts)).EmitHashCode(
                new RecordingInstructionWriter(),
                CliValueKind.ManagedAddress,
                null,
                0,
                1,
                2,
                3));

        Assert.Contains("closed receiver type", exception.Message);
    }

    [Fact]
    public void GetTypeCodeDispatchesThroughReachableEnumStorage()
    {
        var program = new EnumProgram(isSigned: true, wide: false);
        var layouts = new EnumLayouts(program.UnderlyingType);
        var references = new ReferenceComparisonEmitter(layouts);
        var emitter = new EnumTypeCodeEmitter(
            new EnumStorageResolver(program, program, layouts, layouts, layouts),
            new EnumNullCheckEmitter(
                references,
                new ImplicitExceptionEmitter(layouts, layouts, 0)));
        var code = new RecordingInstructionWriter();

        ((IEnumTypeCodeEmitter)emitter).EmitTypeCode(code, 0, 1, 2);

        var bytes = code.ToArray();
        Assert.Contains(WasmOpcodes.I32Load, bytes);
        Assert.Contains(WasmOpcodes.I32Equal, bytes);
        Assert.Contains(WasmOpcodes.LocalSet, bytes);
    }

    [Fact]
    public void HashCodeRejectsAnEmptyReachableEnumSet()
    {
        var program = new EnumProgram(isSigned: true);
        var layouts = new EnumLayouts(program.UnderlyingType);
        Func<IEnumHashCodeEmitter, InvalidOperationException> contract = emitter =>
            Assert.Throws<InvalidOperationException>(() => emitter.EmitHashCode(
                new RecordingInstructionWriter(),
                CliValueKind.ManagedReference,
                null,
                0,
                1,
                2,
                3));
        var exception = contract(new EnumHashCodeEmitter(
            new EmptyEnumStorageResolver(),
            new NoOpEnumNullCheckEmitter(),
            layouts,
            layouts));

        Assert.Contains("reachable enum descriptor", exception.Message);
    }

    private static EnumEqualsEmitter CreateEquals(
        EnumProgram program,
        EnumLayouts layouts)
    {
        var references = new ReferenceComparisonEmitter(layouts);
        return new EnumEqualsEmitter(
            new EnumStorageResolver(program, program, layouts, layouts, layouts),
            new EnumNullCheckEmitter(
                references,
                new ImplicitExceptionEmitter(layouts, layouts, 0)),
            new EnumValuePairEmitter(layouts),
            references,
            layouts);
    }

    private static EnumHashCodeEmitter CreateHashCode(
        EnumProgram program,
        EnumLayouts layouts)
    {
        var references = new ReferenceComparisonEmitter(layouts);
        return new EnumHashCodeEmitter(
            new EnumStorageResolver(program, program, layouts, layouts, layouts),
            new EnumNullCheckEmitter(
                references,
                new ImplicitExceptionEmitter(layouts, layouts, 0)),
            layouts,
            layouts);
    }

    private static EnumCompareToEmitter CreateCompareTo(
        EnumProgram program,
        EnumLayouts layouts)
    {
        var references = new ReferenceComparisonEmitter(layouts);
        return new EnumCompareToEmitter(
            new EnumStorageResolver(program, program, layouts, layouts, layouts),
            new EnumNullCheckEmitter(
                references,
                new ImplicitExceptionEmitter(layouts, layouts, 0)),
            new EnumValuePairEmitter(layouts),
            references,
            new ImplicitExceptionEmitter(layouts, layouts, 0),
            layouts);
    }

    private sealed class EmptyEnumStorageResolver : IEnumStorageResolver
    {
        public ImmutableArray<EnumStorage> Resolve() => [];
    }

    private sealed class NoOpEnumNullCheckEmitter : IEnumNullCheckEmitter
    {
        public void Emit(IWasmInstructionWriter code, int receiver)
        {
        }
    }

    private sealed class EnumProgram(bool isSigned, bool wide = true, int narrowBytes = 0) :
        ITypeRepository,
        IFieldRepository,
        IMethodRepository,
        ISymbolFormatter,
        ITypeClassifier
    {
        private static readonly AssemblyIdentity Assembly = new("EnumTests");
        internal static readonly EntityKey TypeKey = new(Assembly, 0x02000001);
        internal static readonly EntityKey FieldKey = new(Assembly, 0x04000001);

        internal CliTypeIdentity UnderlyingType { get; } =
            CreateUnderlyingType(isSigned, wide, narrowBytes);

        private static CliTypeIdentity CreateUnderlyingType(
            bool isSigned,
            bool wide,
            int narrowBytes)
        {
            var name = narrowBytes switch
            {
                1 => isSigned ? "i1" : "u1",
                2 => isSigned ? "i2" : "u2",
                _ => (isSigned, wide) switch
                {
                    (true, true) => "i8",
                    (false, true) => "u8",
                    (true, false) => "i4",
                    _ => "u4",
                },
            };
            var stackKind = name is "i8" or "u8" ? CliValueKind.I8 : CliValueKind.I4;
            return CliTypeIdentity.Primitive(name, stackKind);
        }

        public bool IsDelegateType(EntityKey type) => false;

        public TypeDefinitionModel GetTypeDefinition(EntityKey key) => new(
            TypeKey,
            "Tests",
            "State",
            true,
            [FieldKey],
            [])
        {
            IsEnum = true,
            EnumUnderlyingType = CliTypeIdentity.FromStackKind(CliValueKind.I8),
        };

        public FieldDefinitionModel GetField(EntityKey key) => new(
            FieldKey,
            TypeKey,
            "value__",
            UnderlyingType,
            false);

        public MethodDefinitionModel GetMethod(EntityKey key) =>
            throw new KeyNotFoundException();

        public string Format(EntityKey key) => "Tests.State";

        public string Format(MethodDefinitionModel method) => method.Name;
    }

    private sealed class EnumLayouts(CliTypeIdentity underlyingType) :
        ITargetLayout,
        IValueLayoutProvider,
        ITypeLayoutProvider,
        IInstanceFieldLayoutProvider,
        IStaticFieldLayoutProvider,
        IStaticDataLayout,
        IRuntimeObjectLayout,
        IManagedExceptionObjectProvider,
        ITypeDescriptorSource
    {
        public WasmTargetLayout Target => WasmTargetLayout.Wasm32;
        public int ReferenceArrayTypeId => 0;
        public int StringTypeId => 0;
        public int TypeTypeId => 0;
        public int StringLengthOffset => 0;
        public int StringDataOffset => 0;
        public int ArrayLengthOffset => 0;
        public int ArrayDataPointerOffset => 0;
        public int ArrayElementTypeIdOffset => 0;
        public int StaticDataEnd => 0;
        public int DelegateTargetOffset => 0;
        public int DelegateMethodIdOffset => 0;
        public int DelegateLeftOffset => 0;
        public int DelegateRightOffset => 0;
        public ImmutableArray<TypeDescriptorLayout> TypeDescriptors =>
            [new(EnumProgram.TypeKey, 7, 0, 16, 0, 0, null)];
        public ImmutableArray<ConstructedTypeDescriptorLayout> ConstructedTypeDescriptors => [];
        public ImmutableArray<ValueTypeDescriptorLayout> ValueTypeDescriptors => [];
        public ImmutableArray<int> StaticRootAddresses => [];
        public ImmutableArray<DataSegment> DataSegments => [];

        public ValueLayout GetValueLayout(CliTypeIdentity type)
        {
            var size = underlyingType.CanonicalName switch
            {
                "primitive:i1" or "primitive:u1" => 1,
                "primitive:i2" or "primitive:u2" => 2,
                "primitive:i8" or "primitive:u8" => sizeof(long),
                _ => sizeof(int),
            };
            return new(underlyingType, size, size, []);
        }

        public ObjectLayout GetObjectLayout(CliTypeIdentity type) => new(7, 16, []);

        public bool GetObjectLayout(CliTypeIdentity type, out ObjectLayout layout)
        {
            layout = GetObjectLayout(type);
            return true;
        }
        public ObjectLayout GetObjectLayout(EntityKey type) => new(7, 16, []);
        public FieldLayout GetFieldLayout(FieldInstanceModel field) => new(8);
        public FieldLayout GetFieldLayout(EntityKey field) => new(8);
        public StaticFieldLayout GetStaticFieldLayout(EntityKey field) => new(0);
        public StaticFieldLayout GetStaticFieldLayout(FieldInstanceModel field) => new(0);
        public StringLayout GetStringLayout(string value) => new(0, 0, 0);
        public int GetExceptionObject(ManagedExceptionKind kind) => 32 + (int)kind * 8;
    }
}
