using System.Text;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.ModuleEncoding;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class WasmBinaryTests
{
    public static TheoryData<uint, byte[]> UnsignedLebCases => new()
    {
        { 0u, [0x00] },
        { 127u, [0x7f] },
        { 128u, [0x80, 0x01] },
        { 624485u, [0xe5, 0x8e, 0x26] },
    };

    public static TheoryData<int, byte[]> SignedLebCases => new()
    {
        { 0, [0x00] },
        { -1, [0x7f] },
        { 63, [0x3f] },
        { 64, [0xc0, 0x00] },
        { -64, [0x40] },
        { -65, [0xbf, 0x7f] },
        { -624485, [0x9b, 0xf1, 0x59] },
    };

    [Theory]
    [MemberData(nameof(UnsignedLebCases))]
    public void WritesUnsignedLeb128(uint value, byte[] expected)
    {
        var buffer = new WasmBinaryBuffer();
        var writer = new WasmBinaryWriter(buffer);
        new UnsignedLeb128Encoder().Encode(writer, value);
        Assert.Equal(expected, new WasmBinarySnapshotReader(buffer).Read());
    }

    [Theory]
    [MemberData(nameof(SignedLebCases))]
    public void WritesSignedLeb128(int value, byte[] expected)
    {
        var buffer = new WasmBinaryBuffer();
        var writer = new WasmBinaryWriter(buffer);
        new SignedLeb128Encoder().Encode(writer, value);
        Assert.Equal(expected, new WasmBinarySnapshotReader(buffer).Read());
    }

    [Fact]
    public void WritesUnsignedAndSigned64BitLeb128()
    {
        var buffer = new WasmBinaryBuffer();
        var writer = new WasmBinaryWriter(buffer);

        new UnsignedLeb12864Encoder().Encode(writer, ulong.MaxValue);
        new SignedLeb12864Encoder().Encode(writer, -1);

        Assert.Equal(
            [0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0x01, 0x7f],
            new WasmBinarySnapshotReader(buffer).Read());
    }

    [Fact]
    public void WritesUtf8StringsAndVectors()
    {
        var buffer = new WasmBinaryBuffer();
        var writer = new WasmBinaryWriter(buffer);
        var reader = new WasmBinarySnapshotReader(buffer);
        var unsigned = new UnsignedLeb128Encoder();
        var utf8 = new Utf8StringEncoder(unsigned);
        utf8.Encode(writer, "AΩ");
        unsigned.Encode(writer, 2);
        writer.Write([1, 2]);

        byte[] expected = [3, 0x41, 0xce, 0xa9, 2, 1, 2];
        Assert.Equal(expected, reader.Read());
        Assert.Throws<ArgumentNullException>(() => utf8.Encode(writer, null!));
    }

    [Fact]
    public void WritesEverySupportedScalarInstructionEncoding()
    {
        var buffer = new WasmBinaryBuffer();
        var writer = new WasmBinaryWriter(buffer);
        writer.Write([0xff, 0xaa]);
        WasmInstruction[] instructions =
        [
            WasmInstruction.WithOperand(WasmOpcodes.Block, WasmInstructionOperand.BlockType(0x40)),
            WasmInstruction.WithOperand(WasmOpcodes.Block, WasmInstructionOperand.BlockType(0x7f)),
            WasmInstruction.WithOperand(WasmOpcodes.Loop, WasmInstructionOperand.BlockType(0x40)),
            WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(0x40)),
            WasmInstruction.NoOperand(WasmOpcodes.Else),
            WasmInstruction.NoOperand(WasmOpcodes.End),
            WasmInstruction.NoOperand(WasmOpcodes.Unreachable),
            WasmInstruction.NoOperand(WasmOpcodes.Return),
            WasmInstruction.NoOperand(WasmOpcodes.Drop),
            WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero),
            WasmInstruction.NoOperand(WasmOpcodes.I64EqualZero),
            WasmInstruction.NoOperand(WasmOpcodes.I32Equal),
            WasmInstruction.NoOperand(WasmOpcodes.I32LessThanSigned),
            WasmInstruction.NoOperand(WasmOpcodes.I32GreaterThanSigned),
            WasmInstruction.NoOperand(WasmOpcodes.I32GreaterThanOrEqualUnsigned),
            WasmInstruction.NoOperand(WasmOpcodes.I32Add),
            WasmInstruction.NoOperand(WasmOpcodes.I32Subtract),
            WasmInstruction.NoOperand(WasmOpcodes.I32Multiply),
            WasmInstruction.NoOperand(WasmOpcodes.I32DivideSigned),
            WasmInstruction.NoOperand(WasmOpcodes.I32And),
            WasmInstruction.NoOperand(WasmOpcodes.I32Xor),
            WasmInstruction.WithOperand(WasmOpcodes.Branch, WasmInstructionOperand.Unsigned(1)),
            WasmInstruction.WithOperand(WasmOpcodes.BranchIf, WasmInstructionOperand.Unsigned(2)),
            WasmInstruction.WithOperand(
                WasmOpcodes.TryTable,
                WasmInstructionOperand.TryTableCatch(0x40, 3, 4)),
            WasmInstruction.WithOperand(WasmOpcodes.Throw, WasmInstructionOperand.Unsigned(5)),
            WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned(3)),
            WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned(4)),
            WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned(5)),
            WasmInstruction.WithOperand(WasmOpcodes.LocalTee, WasmInstructionOperand.Unsigned(6)),
            WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(-1)),
            WasmInstruction.WithOperand(WasmOpcodes.I32Load, WasmInstructionOperand.Memory(2, 12)),
            WasmInstruction.WithOperand(
                WasmOpcodes.I32Load16Unsigned,
                WasmInstructionOperand.Memory(1, 14)),
            WasmInstruction.WithOperand(WasmOpcodes.I32Store, WasmInstructionOperand.Memory(2, 16)),
        ];
        var instructionWriter = new WasmInstructionWriter(writer);
        foreach (var instruction in instructions)
        {
            instructionWriter.Write(instruction);
        }

        byte[] expected =
        [
                0xff, 0xaa, 0x02, 0x40, 0x02, 0x7f, 0x03, 0x40, 0x04, 0x40, 0x05, 0x0b,
                0x00, 0x0f, 0x1a, 0x45, 0x50, 0x46, 0x48, 0x4a, 0x4f, 0x6a, 0x6b,
                0x6c, 0x6d, 0x71, 0x73, 0x0c, 1, 0x0d, 2,
                0x1f, 0x40, 1, 0, 3, 4, 0x08, 5, 0x10, 3, 0x20, 4, 0x21, 5,
                0x22, 6, 0x41, 0x7f,
                0x28, 2, 12, 0x2f, 1, 14, 0x36, 2, 16,
        ];
        Assert.Equal(expected, new WasmBinarySnapshotReader(buffer).Read());
        Assert.Equal(33, instructions.Length);
    }

    [Fact]
    public void EncodesPrefixedInstructionsAlongsideRawBytes()
    {
        var buffer = new WasmBinaryBuffer();
        var writer = new WasmBinaryWriter(buffer);
        var unsigned = new UnsignedLeb128Encoder();
        var instructionWriter = new WasmInstructionWriter(writer);

        writer.Write([WasmOpcodes.I32Add]);
        unsigned.Encode(writer, WasmOpcodes.MemoryCopy);
        instructionWriter.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Prefixed,
            WasmInstructionOperand.Prefixed(WasmOpcodes.MemoryCopy)));
        writer.Write([0, 0]);
        instructionWriter.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Prefixed,
            WasmInstructionOperand.Prefixed(WasmOpcodes.I32TruncateSaturateF64Unsigned)));

        Assert.Equal(
            [
                WasmOpcodes.I32Add,
                WasmOpcodes.MemoryCopy,
                WasmOpcodes.Prefixed, WasmOpcodes.MemoryCopy, 0, 0,
                WasmOpcodes.Prefixed, WasmOpcodes.I32TruncateSaturateF64Unsigned,
            ],
            new WasmBinarySnapshotReader(buffer).Read());
    }

    [Fact]
    public void SectionWriterWritesThroughItsCapabilityContract()
    {
        var buffer = new WasmBinaryBuffer();
        IWasmBinaryWriter module = new WasmBinaryWriter(buffer);

        Action<IWasmSectionWriter, IWasmBinaryWriter> writeSection =
            static (writer, output) => writer.Write(output, 7, [1, 2]);
        writeSection(new WasmSectionWriter(new UnsignedLeb128Encoder()), module);

        Assert.Equal([7, 2, 1, 2], new WasmBinarySnapshotReader(buffer).Read());
    }

    [Fact]
    public void ModuleEncoderEncodesThroughItsCapabilityContract()
    {
        Func<IModuleEncoder, WasmModuleBuildRequest, byte[]> encodeModule =
            static (encoder, request) => encoder.Encode(request);
        var result = encodeModule(CreateModuleEncoder(), new WasmModuleBuildRequest(
            [],
            "runtime",
            "memory",
            [],
            [],
            [],
            false,
            WasmTarget.Wasm32));

        Assert.Equal([0, 0x61, 0x73, 0x6d, 1, 0, 0, 0], result[..8]);
    }

    [Fact]
    public void ModuleEncoderOmitsFunctionNamesWhenRequested()
    {
        const string identity = "Unique.Sidecar.Only.Method";
        var result = CreateModuleEncoder().Encode(new WasmModuleBuildRequest(
            [],
            "runtime",
            "memory",
            [new(identity, WasmFunctionType.Create(CliValueKind.Void), [0, 0x0b])],
            [],
            [],
            false,
            WasmTarget.Wasm32,
            IncludeNameSection: false));

        Assert.Equal(-1, result.AsSpan().IndexOf(
            System.Text.Encoding.UTF8.GetBytes(identity)));
    }

    [Fact]
    public void ModuleBuilderDelegatesThroughItsCapabilityContract()
    {
        var encoder = new RecordingModuleEncoder();

        Func<
            IWasmModuleBuilder,
            IReadOnlyList<WasmFunctionImport>,
            string,
            string,
            IReadOnlyList<WasmFunctionDefinition>,
            IReadOnlyList<WasmExport>,
            IReadOnlyList<DataSegment>,
            byte[]> buildModule = static (
                builder,
                functionImports,
                memoryImportModule,
                memoryImportName,
                functions,
                exports,
                dataSegments) => builder.Build(
                    functionImports,
                    memoryImportModule,
                    memoryImportName,
                    functions,
                    exports,
                    dataSegments);
        var result = buildModule(new WasmModuleBuilder(encoder), [], "runtime", "memory", [], [], []);

        Assert.Equal([0xca, 0xfe], result);
        Assert.NotNull(encoder.Request);
        Assert.Equal("runtime", encoder.Request.MemoryImportModule);
        Assert.Equal("memory", encoder.Request.MemoryImportName);
    }

    [Fact]
    public void ModuleBuilderInternsTypesAndWritesImportsExportsDataAndNames()
    {
        var unary = WasmFunctionType.Create(
            CliValueKind.I4,
            CliValueKind.I4);
        byte[] body = [0, 0x20, 0, 0x0f, 0x0b];
        var module = CreateModuleBuilder().Build(
            [
                new WasmFunctionImport("runtime", "identity", unary),
                new WasmFunctionImport(
                    "host",
                    "write",
                    WasmFunctionType.Create(CliValueKind.Void, CliValueKind.I4)),
            ],
            "runtime",
            "memory",
            [new WasmFunctionDefinition("entry", unary, body)],
            [new WasmExport("run", 2)],
            [new DataSegment(16, [1, 2, 3])]);

        byte[] header = [0, 0x61, 0x73, 0x6d, 1, 0, 0, 0];
        Assert.Equal(header, module[..8]);
        var binaryText = System.Text.Encoding.UTF8.GetString(module);
        Assert.Contains("runtime", binaryText);
        Assert.Contains("identity", binaryText);
        Assert.Contains("memory", binaryText);
        Assert.Contains("host", binaryText);
        Assert.Contains("write", binaryText);
        Assert.Contains("run", binaryText);
        Assert.Contains("entry", binaryText);
        Assert.Contains("name", binaryText);
        byte[] data = [1, 2, 3];
        Assert.True(module.AsSpan().IndexOf(data) >= 0);
    }

    [Fact]
    public void ModuleBuilderSupportsNoDataAndRejectsInvalidArguments()
    {
        var module = CreateModuleBuilder().Build(
            [],
            "runtime",
            "memory",
            [],
            [],
            []);
        Assert.Equal(0, module[0]);

        var tagged = CreateModuleBuilder().Build(
            [],
            "runtime",
            "memory",
            [],
            [],
            [],
            includeManagedExceptionTag: true);
        byte[] tagSection = [0x0d, 0x03, 0x01, 0x00];
        Assert.True(tagged.AsSpan().IndexOf(tagSection) >= 0);

        Assert.Throws<ArgumentNullException>(() => CreateModuleBuilder().Build(
            null!, "runtime", "memory", [], [], []));
        Assert.Throws<ArgumentException>(() => CreateModuleBuilder().Build(
            [], "", "memory", [], [], []));
        Assert.Throws<ArgumentException>(() => CreateModuleBuilder().Build(
            [], "runtime", " ", [], [], []));
        Assert.Throws<ArgumentNullException>(() => CreateModuleBuilder().Build(
            [], "runtime", "memory", null!, [], []));
        Assert.Throws<ArgumentNullException>(() => CreateModuleBuilder().Build(
            [], "runtime", "memory", [], null!, []));
        Assert.Throws<ArgumentNullException>(() => CreateModuleBuilder().Build(
            [], "runtime", "memory", [], [], null!));
    }

    [Fact]
    public void ModuleEncoderRejectsVoidFunctionParametersThroughItsContract()
    {
        var invalidType = WasmFunctionType.Create(
            CliValueKind.I4,
            CliValueKind.Void);
        var encoder = new[]
        {
            CreateModuleEncoder(),
        }.Cast<IModuleEncoder>().Single();
        var request = new WasmModuleBuildRequest(
            [new WasmFunctionImport("runtime", "invalid", invalidType)],
            "runtime",
            "memory",
            [],
            [],
            [],
            false,
            WasmTarget.Wasm32);

        Assert.Throws<ArgumentOutOfRangeException>(() => encoder.Encode(request));
    }

    [Fact]
    public void ModuleBuilderWritesMemory64AddressesAndReferenceTypes()
    {
        var referenceIdentity = WasmFunctionType.Create(
            CliValueKind.ManagedReference,
            CliValueKind.ManagedReference);
        var module = CreateModuleBuilder().Build(
            [new WasmFunctionImport("runtime", "identity", referenceIdentity)],
            "runtime",
            "memory",
            [],
            [],
            [new DataSegment(16, [1])],
            target: WasmTarget.Wasm64);

        var referenceSignature = Bytes(0x60, 0x01, 0x7e, 0x01, 0x7e);
        var memoryImport = Bytes(
            0x07, 0x72, 0x75, 0x6e, 0x74, 0x69, 0x6d, 0x65,
            0x06, 0x6d, 0x65, 0x6d, 0x6f, 0x72, 0x79, 0x02, 0x04, 0x01);
        var dataOffset = Bytes(0x00, 0x42, 0x10, 0x0b, 0x01, 0x01);

        Assert.True(module.AsSpan().IndexOf(referenceSignature) >= 0);
        Assert.True(module.AsSpan().IndexOf(memoryImport) >= 0);
        Assert.True(module.AsSpan().IndexOf(dataOffset) >= 0);
    }

    [Fact]
    public void FunctionTypeFactoryPreservesParametersAndResult()
    {
        var type = WasmFunctionType.Create(
            CliValueKind.ManagedReference,
            CliValueKind.I4,
            CliValueKind.ManagedReference);
        Assert.Equal(CliValueKind.ManagedReference, type.Result);
        Assert.True(type.Parameters.SequenceEqual(
            [CliValueKind.I4, CliValueKind.ManagedReference]));
    }

    [Fact]
    public void RuntimeImportsSeparateAddressesFromSemanticIds()
    {
        var imports = WasmRuntimeImports.Create();
        var allocate = imports.Single(import => import.Name == RuntimeAbi.RuntimeAllocate);
        var registerType = imports.Single(import => import.Name == RuntimeAbi.RuntimeRegisterType);
        var rootEnter = imports.Single(import => import.Name == RuntimeAbi.RuntimeRootFrameEnter);

        Assert.Equal(CliValueKind.ManagedReference, allocate.Type.Result);
        Assert.True(allocate.Type.Parameters.SequenceEqual(
            [CliValueKind.ManagedAddress, CliValueKind.I4]));
        Assert.True(registerType.Type.Parameters.SequenceEqual(
            [
                CliValueKind.I4,
                CliValueKind.I4,
                CliValueKind.ManagedAddress,
                CliValueKind.ManagedAddress,
                CliValueKind.I4,
                CliValueKind.ManagedAddress,
                CliValueKind.I4,
                CliValueKind.I4,
                CliValueKind.I4,
            ]));
        Assert.Equal(CliValueKind.ManagedAddress, rootEnter.Type.Result);
        Assert.True(rootEnter.Type.Parameters.SequenceEqual([CliValueKind.I4]));
    }

    [Fact]
    public void PhysicalWasmTypesFollowTheSelectedAddressWidth()
    {
        Assert.Equal(
            WasmValueType.I32,
            WasmValueTypes.FromCli(CliValueKind.ManagedReference, WasmTargetLayout.Wasm32));
        Assert.Equal(
            WasmValueType.I64,
            WasmValueTypes.FromCli(CliValueKind.ManagedReference, WasmTargetLayout.Wasm64));
        Assert.Equal(
            WasmValueType.I64,
            WasmValueTypes.FromCli(CliValueKind.ManagedAddress, WasmTargetLayout.Wasm64));
        Assert.Equal(
            WasmValueType.I32,
            WasmValueTypes.FromCli(CliValueKind.I4, WasmTargetLayout.Wasm64));
        Assert.Equal(
            WasmValueType.I64,
            WasmValueTypes.FromCli(CliValueKind.I8, WasmTargetLayout.Wasm32));
        Assert.Equal(
            WasmValueType.F32,
            WasmValueTypes.FromCli(CliValueKind.F4, WasmTargetLayout.Wasm32));
        Assert.Equal(
            WasmValueType.F64,
            WasmValueTypes.FromCli(CliValueKind.F8, WasmTargetLayout.Wasm32));
        Assert.Equal(
            WasmValueType.I32,
            WasmValueTypes.FromCli(CliValueKind.NativeInt, WasmTargetLayout.Wasm32));
        Assert.Equal(
            WasmValueType.I64,
            WasmValueTypes.FromCli(CliValueKind.NativeInt, WasmTargetLayout.Wasm64));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WasmValueTypes.FromCli(CliValueKind.Void, WasmTargetLayout.Wasm32));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WasmValueTypes.FromCli(CliValueKind.Unknown, WasmTargetLayout.Wasm32));
    }

    private static byte[] Bytes(params byte[] values) => values;

    private static WasmModuleBuilder CreateModuleBuilder() =>
        WasmModuleBuilderFactory.Create();

    private static ModuleEncoder CreateModuleEncoder()
    {
        var unsigned = new UnsignedLeb128Encoder();
        var signed = new SignedLeb128Encoder();
        var signed64 = new SignedLeb12864Encoder();
        var instructionEncoders = new WasmInstructionEncoderRegistry(
        [
            new(
                WasmInstructionOperandShape.None,
                new NoOperandInstructionEncoder()),
            new(
                WasmInstructionOperandShape.SignedLeb128,
                new SignedLeb128InstructionEncoder(signed)),
            new(
                WasmInstructionOperandShape.SignedLeb12864,
                new SignedLeb12864InstructionEncoder(signed64)),
        ]);
        return new ModuleEncoder(
            unsigned,
            new UnsignedLeb12864Encoder(),
            signed,
            signed64,
            new Utf8StringEncoder(unsigned),
            new WasmSectionWriter(unsigned),
            static buffer => new WasmBinaryWriter(buffer),
            static buffer => new WasmBinarySnapshotReader(buffer),
            output => new WasmInstructionWriter(output, instructionEncoders));
    }

    private sealed class RecordingModuleEncoder : IModuleEncoder
    {
        public WasmModuleBuildRequest? Request { get; private set; }

        public byte[] Encode(WasmModuleBuildRequest request)
        {
            Request = request;
            return [0xca, 0xfe];
        }
    }

}
