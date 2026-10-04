using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.NativeInterop;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.ModuleEncoding;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class NativeCallbackObjectWriterTests
{
    [Fact]
    public void EmptyPlanProducesNoSupportObject()
    {
        var writer = CreateWriter();

        Assert.Empty(writer.Write(NativeCallbackPlan.Empty, WasmTarget.Wasm32));
        Assert.Empty(writer.Write(NativeCallbackPlan.Empty, WasmTarget.Wasm64));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, 0x7f)]
    [InlineData(WasmTarget.Wasm64, 0x7e)]
    public void WritesVoidCallbackWithNoResultValue(
        WasmTarget target,
        byte addressType)
    {
        var callback = Callback(
            token: 1,
            name: "VoidCallback",
            MethodSignatureModel.Create(CliValueKind.Void));
        var callbacks = ImmutableDictionary<string, MethodInstanceModel>.Empty
            .Add(callback.CanonicalName, callback);
        var plan = NativeAbiTestSupport.CallbackPlanner().Build(
            callbacks,
            callbacks.Keys.ToHashSet(StringComparer.Ordinal),
            17);

        var types = new ObjectReader(
            ObjectFile.Parse(CreateWriter().Write(plan, target))[0].Payload);

        Assert.Equal(2u, types.ReadUnsigned());
        Assert.Equal((byte)0x60, types.ReadByte());
        Assert.Equal(0u, types.ReadUnsigned());
        Assert.Equal(0u, types.ReadUnsigned());
        Assert.Equal((byte)0x60, types.ReadByte());
        Assert.Equal(0u, types.ReadUnsigned());
        Assert.Equal(1u, types.ReadUnsigned());
        Assert.Equal(addressType, types.ReadByte());
        types.AssertEnd();
    }

    [Fact]
    public void RejectsInvalidInputs()
    {
        var writer = CreateWriter();

        Assert.Throws<ArgumentNullException>(() =>
            writer.Write(null!, WasmTarget.Wasm32));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            writer.Write(NativeCallbackPlan.Empty, (WasmTarget)42));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, 1, 5, false)]
    [InlineData(WasmTarget.Wasm64, 18, 10, true)]
    public void WritesDeterministicLinkerOwnedCallbackObject(
        WasmTarget target,
        byte relocationKind,
        int paddedAddressWidth,
        bool expectsMemory64Feature)
    {
        var callbacks = CreatePlan();
        var writer = CreateWriter();

        var first = writer.Write(callbacks, target);
        var second = writer.Write(callbacks, target);

        Assert.Equal(first, second);
        var sections = ObjectFile.Parse(first);
        Assert.Equal([1, 2, 3, 9, 10, 0, 0],
            sections.Take(7).Select(section => section.Id));
        Assert.Equal(expectsMemory64Feature ? 8 : 7, sections.Count);
        Assert.Equal("linking", sections[5].Name);
        Assert.Equal("reloc.CODE", sections[6].Name);
        Assert.Equal(
            expectsMemory64Feature ? "target_features" : null,
            sections.ElementAtOrDefault(7)?.Name);

        AssertTypes(sections[0].Payload, target);
        AssertImports(sections[1].Payload, callbacks, target);
        AssertFunctionTypes(sections[2].Payload);
        AssertElements(sections[3].Payload, target);
        var relocationOffsets = AssertCode(
            sections[4].Payload,
            target,
            paddedAddressWidth);
        AssertSymbols(sections[5].Payload, callbacks);
        AssertRelocations(
            sections[6].Payload,
            relocationKind,
            relocationOffsets);
        if (expectsMemory64Feature)
        {
            AssertTargetFeatures(sections[7].Payload);
        }
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, false)]
    [InlineData(WasmTarget.Wasm64, true)]
    public void WritesNamedOnlyObjectWithoutGetterTableOrElement(
        WasmTarget target,
        bool expectsMemory64Feature)
    {
        var callback = Callback(
            token: 1,
            name: "NamedCallback",
            MethodSignatureModel.Create(CliValueKind.I4, CliValueKind.I4),
            entryPoint: "named_entry");
        var callbacks = ImmutableDictionary<string, MethodInstanceModel>.Empty
            .Add(callback.CanonicalName, callback);
        var plan = NativeAbiTestSupport.CallbackPlanner().Build(
            callbacks,
            ImmutableHashSet<string>.Empty,
            17);

        var sections = ObjectFile.Parse(CreateWriter().Write(plan, target));

        Assert.Equal(expectsMemory64Feature
                ? [1, 2, 3, 10, 0, 0, 0]
                : [1, 2, 3, 10, 0, 0],
            sections.Select(section => (int)section.Id).ToArray());
        Assert.Equal("linking", sections[4].Name);
        Assert.Equal("reloc.CODE", sections[5].Name);
        Assert.Equal(
            expectsMemory64Feature ? "target_features" : null,
            sections.ElementAtOrDefault(6)?.Name);

        var types = new ObjectReader(sections[0].Payload);
        Assert.Equal(1u, types.ReadUnsigned());
        Assert.Equal((byte)0x60, types.ReadByte());
        Assert.Equal(1u, types.ReadUnsigned());
        Assert.Equal((byte)0x7f, types.ReadByte());
        Assert.Equal(1u, types.ReadUnsigned());
        Assert.Equal((byte)0x7f, types.ReadByte());
        types.AssertEnd();

        var imports = new ObjectReader(sections[1].Payload);
        Assert.Equal(2u, imports.ReadUnsigned());
        Assert.Equal(RuntimeAbi.ApplicationModule, imports.ReadString());
        Assert.Equal("named_entry", imports.ReadString());
        Assert.Equal((byte)0, imports.ReadByte());
        Assert.Equal(0u, imports.ReadUnsigned());
        Assert.Equal("env", imports.ReadString());
        Assert.Equal("__linear_memory", imports.ReadString());
        Assert.Equal((byte)2, imports.ReadByte());
        Assert.Equal(target == WasmTarget.Wasm64 ? (byte)4 : (byte)0,
            imports.ReadByte());
        Assert.Equal(0u, imports.ReadUnsigned());
        imports.AssertEnd();

        var functions = new ObjectReader(sections[2].Payload);
        Assert.Equal(1u, functions.ReadUnsigned());
        Assert.Equal(0u, functions.ReadUnsigned());
        functions.AssertEnd();

        var code = new ObjectReader(sections[3].Payload);
        Assert.Equal(1u, code.ReadUnsigned());
        Assert.Equal(10u, code.ReadUnsigned());
        Assert.Equal((byte)0, code.ReadByte());
        Assert.Equal((byte)0x20, code.ReadByte());
        Assert.Equal((byte)0, code.ReadByte());
        Assert.Equal((byte)0x10, code.ReadByte());
        var relocationOffset = code.Position;
        Assert.Equal([0x80, 0x80, 0x80, 0x80, 0x00], code.ReadBytes(5));
        Assert.Equal((byte)0x0b, code.ReadByte());
        code.AssertEnd();

        var symbols = new ObjectReader(sections[4].Payload);
        Assert.Equal(2u, symbols.ReadUnsigned());
        Assert.Equal((byte)8, symbols.ReadByte());
        var symbolSubsectionLength = symbols.ReadUnsigned();
        var symbolSubsectionEnd = symbols.Position + checked((int)symbolSubsectionLength);
        Assert.Equal(2u, symbols.ReadUnsigned());
        Assert.Equal((byte)0, symbols.ReadByte());
        Assert.Equal((byte)0x50, symbols.ReadByte());
        Assert.Equal(0u, symbols.ReadUnsigned());
        Assert.Equal(plan.Methods[0].RuntimeImportSymbol, symbols.ReadString());
        Assert.Equal((byte)0, symbols.ReadByte());
        Assert.Equal((byte)0, symbols.ReadByte());
        Assert.Equal(1u, symbols.ReadUnsigned());
        Assert.Equal("named_entry", symbols.ReadString());
        Assert.Equal(symbolSubsectionEnd, symbols.Position);
        symbols.AssertEnd();

        var relocations = new ObjectReader(sections[5].Payload);
        Assert.Equal(3u, relocations.ReadUnsigned());
        Assert.Equal(1u, relocations.ReadUnsigned());
        Assert.Equal((byte)0, relocations.ReadByte());
        Assert.Equal((uint)relocationOffset, relocations.ReadUnsigned());
        Assert.Equal(0u, relocations.ReadUnsigned());
        relocations.AssertEnd();
        if (expectsMemory64Feature)
        {
            AssertTargetFeatures(sections[6].Payload);
        }
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, 1, 5)]
    [InlineData(WasmTarget.Wasm64, 18, 10)]
    public void NamedAddressTargetsDefinedBridgeFromTableAndGetter(
        WasmTarget target,
        byte tableRelocationKind,
        int paddedAddressWidth)
    {
        var callback = Callback(
            token: 1,
            name: "NamedCallback",
            MethodSignatureModel.Create(CliValueKind.I4, CliValueKind.I4),
            entryPoint: "named_entry");
        var callbacks = ImmutableDictionary<string, MethodInstanceModel>.Empty
            .Add(callback.CanonicalName, callback);
        var plan = NativeAbiTestSupport.CallbackPlanner().Build(
            callbacks,
            ImmutableHashSet.Create(StringComparer.Ordinal, callback.CanonicalName),
            17);

        var sections = ObjectFile.Parse(CreateWriter().Write(plan, target));

        Assert.Equal([1, 2, 3, 9, 10, 0, 0],
            sections.Take(7).Select(section => (int)section.Id));
        var functions = new ObjectReader(sections[2].Payload);
        Assert.Equal(2u, functions.ReadUnsigned());
        Assert.Equal(0u, functions.ReadUnsigned());
        Assert.Equal(1u, functions.ReadUnsigned());
        functions.AssertEnd();

        var elements = new ObjectReader(sections[3].Payload);
        Assert.Equal(1u, elements.ReadUnsigned());
        Assert.Equal((byte)0, elements.ReadByte());
        Assert.Equal(target == WasmTarget.Wasm64 ? (byte)0x42 : (byte)0x41,
            elements.ReadByte());
        Assert.Equal((byte)0, elements.ReadByte());
        Assert.Equal((byte)0x0b, elements.ReadByte());
        Assert.Equal(1u, elements.ReadUnsigned());
        Assert.Equal(1u, elements.ReadUnsigned());
        elements.AssertEnd();

        var code = new ObjectReader(sections[4].Payload);
        Assert.Equal(2u, code.ReadUnsigned());
        Assert.Equal(10u, code.ReadUnsigned());
        Assert.Equal((byte)0, code.ReadByte());
        Assert.Equal((byte)0x20, code.ReadByte());
        Assert.Equal((byte)0, code.ReadByte());
        Assert.Equal((byte)0x10, code.ReadByte());
        var bridgeRelocationOffset = code.Position;
        AssertPaddedZero(code, 5);
        Assert.Equal((byte)0x0b, code.ReadByte());
        Assert.Equal((uint)(3 + paddedAddressWidth), code.ReadUnsigned());
        Assert.Equal((byte)0, code.ReadByte());
        Assert.Equal(target == WasmTarget.Wasm64 ? (byte)0x42 : (byte)0x41,
            code.ReadByte());
        var getterRelocationOffset = code.Position;
        AssertPaddedZero(code, paddedAddressWidth);
        Assert.Equal((byte)0x0b, code.ReadByte());
        code.AssertEnd();

        var symbols = new ObjectReader(sections[5].Payload);
        Assert.Equal(2u, symbols.ReadUnsigned());
        Assert.Equal((byte)8, symbols.ReadByte());
        var symbolSubsectionLength = symbols.ReadUnsigned();
        var symbolSubsectionEnd = symbols.Position + checked((int)symbolSubsectionLength);
        Assert.Equal(4u, symbols.ReadUnsigned());
        AssertFunctionSymbol(symbols, 0x50, 0, plan.Methods[0].RuntimeImportSymbol);
        AssertFunctionSymbol(symbols, 0x00, 1, "named_entry");
        AssertFunctionSymbol(symbols, 0x00, 2, "__netwasm_callback_address_0");
        Assert.Equal((byte)5, symbols.ReadByte());
        Assert.Equal((byte)0x10, symbols.ReadByte());
        Assert.Equal(0u, symbols.ReadUnsigned());
        Assert.Equal(symbolSubsectionEnd, symbols.Position);
        symbols.AssertEnd();

        var relocations = new ObjectReader(sections[6].Payload);
        Assert.Equal(4u, relocations.ReadUnsigned());
        Assert.Equal(2u, relocations.ReadUnsigned());
        Assert.Equal((byte)0, relocations.ReadByte());
        Assert.Equal((uint)bridgeRelocationOffset, relocations.ReadUnsigned());
        Assert.Equal(0u, relocations.ReadUnsigned());
        Assert.Equal(tableRelocationKind, relocations.ReadByte());
        Assert.Equal((uint)getterRelocationOffset, relocations.ReadUnsigned());
        Assert.Equal(1u, relocations.ReadUnsigned());
        relocations.AssertEnd();
    }

    private static NativeCallbackObjectWriter CreateWriter()
    {
        var unsigned = new UnsignedLeb128Encoder();
        return new(
            unsigned,
            new Utf8StringEncoder(unsigned),
            new WasmSectionWriter(unsigned),
            static buffer => new WasmBinaryWriter(buffer));
    }

    private static NativeCallbackPlan CreatePlan()
    {
        var integer = Callback(
            token: 1,
            name: "IntegerCallback",
            MethodSignatureModel.Create(
                CliValueKind.I4,
                CliValueKind.I4,
                CliValueKind.NativeInt));
        var floating = Callback(
            token: 2,
            name: "FloatingCallback",
            MethodSignatureModel.Create(CliValueKind.F8, CliValueKind.F8));
        var callbacks = ImmutableDictionary<string, MethodInstanceModel>.Empty
            .Add(floating.CanonicalName, floating)
            .Add(integer.CanonicalName, integer);
        return NativeAbiTestSupport.CallbackPlanner().Build(
            callbacks,
            callbacks.Keys.ToHashSet(StringComparer.Ordinal),
            17);
    }

    private static MethodInstanceModel Callback(
        int token,
        string name,
        MethodSignatureModel signature,
        string? entryPoint = null)
    {
        var assembly = new AssemblyIdentity("Callbacks");
        var definition = new MethodDefinitionModel(
            new(assembly, token),
            new(assembly, 100),
            name,
            true,
            signature,
            token)
        {
            NativeCallback = new([], entryPoint, false, false),
        };
        return new(
            definition,
            CliTypeIdentity.Named(assembly, "Tests", "Callbacks", false),
            [],
            signature);
    }

    private static void AssertTypes(byte[] payload, WasmTarget target)
    {
        var reader = new ObjectReader(payload);
        Assert.Equal(3u, reader.ReadUnsigned());
        Assert.Equal((byte)0x60, reader.ReadByte());
        Assert.Equal(2u, reader.ReadUnsigned());
        Assert.Equal((byte)0x7f, reader.ReadByte());
        Assert.Equal(target == WasmTarget.Wasm64 ? (byte)0x7e : (byte)0x7f,
            reader.ReadByte());
        Assert.Equal(1u, reader.ReadUnsigned());
        Assert.Equal((byte)0x7f, reader.ReadByte());

        Assert.Equal((byte)0x60, reader.ReadByte());
        Assert.Equal(1u, reader.ReadUnsigned());
        Assert.Equal((byte)0x7c, reader.ReadByte());
        Assert.Equal(1u, reader.ReadUnsigned());
        Assert.Equal((byte)0x7c, reader.ReadByte());

        Assert.Equal((byte)0x60, reader.ReadByte());
        Assert.Equal(0u, reader.ReadUnsigned());
        Assert.Equal(1u, reader.ReadUnsigned());
        Assert.Equal(target == WasmTarget.Wasm64 ? (byte)0x7e : (byte)0x7f,
            reader.ReadByte());
        reader.AssertEnd();
    }

    private static void AssertImports(
        byte[] payload,
        NativeCallbackPlan callbacks,
        WasmTarget target)
    {
        var reader = new ObjectReader(payload);
        Assert.Equal(4u, reader.ReadUnsigned());
        for (var index = 0; index < callbacks.Methods.Length; index++)
        {
            Assert.Equal(RuntimeAbi.ApplicationModule, reader.ReadString());
            Assert.Equal(callbacks.Methods[index].ThunkExportName, reader.ReadString());
            Assert.Equal((byte)0, reader.ReadByte());
            Assert.Equal((uint)index, reader.ReadUnsigned());
        }

        Assert.Equal("env", reader.ReadString());
        Assert.Equal("__linear_memory", reader.ReadString());
        Assert.Equal((byte)2, reader.ReadByte());
        Assert.Equal(target == WasmTarget.Wasm64 ? (byte)4 : (byte)0,
            reader.ReadByte());
        Assert.Equal(0u, reader.ReadUnsigned());

        Assert.Equal("env", reader.ReadString());
        Assert.Equal("__indirect_function_table", reader.ReadString());
        Assert.Equal((byte)1, reader.ReadByte());
        Assert.Equal((byte)0x70, reader.ReadByte());
        Assert.Equal(target == WasmTarget.Wasm64 ? (byte)4 : (byte)0,
            reader.ReadByte());
        Assert.Equal(1u, reader.ReadUnsigned());
        reader.AssertEnd();
    }

    private static void AssertFunctionTypes(byte[] payload)
    {
        var reader = new ObjectReader(payload);
        Assert.Equal(2u, reader.ReadUnsigned());
        Assert.Equal(2u, reader.ReadUnsigned());
        Assert.Equal(2u, reader.ReadUnsigned());
        reader.AssertEnd();
    }

    private static void AssertElements(byte[] payload, WasmTarget target)
    {
        var reader = new ObjectReader(payload);
        Assert.Equal(1u, reader.ReadUnsigned());
        Assert.Equal((byte)0, reader.ReadByte());
        Assert.Equal(target == WasmTarget.Wasm64 ? (byte)0x42 : (byte)0x41,
            reader.ReadByte());
        Assert.Equal((byte)0, reader.ReadByte());
        Assert.Equal((byte)0x0b, reader.ReadByte());
        Assert.Equal(2u, reader.ReadUnsigned());
        Assert.Equal(0u, reader.ReadUnsigned());
        Assert.Equal(1u, reader.ReadUnsigned());
        reader.AssertEnd();
    }

    private static int[] AssertCode(
        byte[] payload,
        WasmTarget target,
        int paddedAddressWidth)
    {
        var reader = new ObjectReader(payload);
        Assert.Equal(2u, reader.ReadUnsigned());
        var offsets = new int[2];
        for (var index = 0; index < offsets.Length; index++)
        {
            Assert.Equal((uint)(3 + paddedAddressWidth), reader.ReadUnsigned());
            Assert.Equal((byte)0, reader.ReadByte());
            Assert.Equal(target == WasmTarget.Wasm64 ? (byte)0x42 : (byte)0x41,
                reader.ReadByte());
            offsets[index] = reader.Position;
            for (var byteIndex = 1; byteIndex < paddedAddressWidth; byteIndex++)
            {
                Assert.Equal((byte)0x80, reader.ReadByte());
            }
            Assert.Equal((byte)0, reader.ReadByte());
            Assert.Equal((byte)0x0b, reader.ReadByte());
        }
        reader.AssertEnd();
        return offsets;
    }

    private static void AssertSymbols(byte[] payload, NativeCallbackPlan callbacks)
    {
        var reader = new ObjectReader(payload);
        Assert.Equal(2u, reader.ReadUnsigned());
        Assert.Equal((byte)8, reader.ReadByte());
        var subsectionLength = reader.ReadUnsigned();
        var subsectionEnd = reader.Position + checked((int)subsectionLength);
        Assert.Equal(5u, reader.ReadUnsigned());
        for (var index = 0; index < callbacks.Methods.Length; index++)
        {
            Assert.Equal((byte)0, reader.ReadByte());
            Assert.Equal((byte)0x50, reader.ReadByte());
            Assert.Equal((uint)index, reader.ReadUnsigned());
            Assert.Equal(callbacks.Methods[index].NativeSymbol, reader.ReadString());
        }
        for (var index = 0; index < callbacks.Methods.Length; index++)
        {
            Assert.Equal((byte)0, reader.ReadByte());
            Assert.Equal((byte)0, reader.ReadByte());
            Assert.Equal((uint)(callbacks.Methods.Length + index), reader.ReadUnsigned());
            Assert.Equal(callbacks.Methods[index].GetterName, reader.ReadString());
        }
        Assert.Equal((byte)5, reader.ReadByte());
        Assert.Equal((byte)0x10, reader.ReadByte());
        Assert.Equal(0u, reader.ReadUnsigned());
        Assert.Equal(subsectionEnd, reader.Position);
        reader.AssertEnd();
    }

    private static void AssertRelocations(
        byte[] payload,
        byte expectedKind,
        int[] expectedOffsets)
    {
        var reader = new ObjectReader(payload);
        Assert.Equal(4u, reader.ReadUnsigned());
        Assert.Equal(2u, reader.ReadUnsigned());
        for (var index = 0; index < expectedOffsets.Length; index++)
        {
            Assert.Equal(expectedKind, reader.ReadByte());
            Assert.Equal((uint)expectedOffsets[index], reader.ReadUnsigned());
            Assert.Equal((uint)index, reader.ReadUnsigned());
        }
        reader.AssertEnd();
    }

    private static void AssertTargetFeatures(byte[] payload)
    {
        var reader = new ObjectReader(payload);
        Assert.Equal(1u, reader.ReadUnsigned());
        Assert.Equal((byte)0x2b, reader.ReadByte());
        Assert.Equal("memory64", reader.ReadString());
        reader.AssertEnd();
    }

    private static void AssertFunctionSymbol(
        ObjectReader reader,
        byte flags,
        uint index,
        string name)
    {
        Assert.Equal((byte)0, reader.ReadByte());
        Assert.Equal(flags, reader.ReadByte());
        Assert.Equal(index, reader.ReadUnsigned());
        Assert.Equal(name, reader.ReadString());
    }

    private static void AssertPaddedZero(ObjectReader reader, int width)
    {
        for (var index = 1; index < width; index++)
        {
            Assert.Equal((byte)0x80, reader.ReadByte());
        }
        Assert.Equal((byte)0, reader.ReadByte());
    }

    private sealed record ObjectSection(byte Id, string? Name, byte[] Payload);

    private static class ObjectFile
    {
        public static List<ObjectSection> Parse(byte[] bytes)
        {
            var reader = new ObjectReader(bytes);
            Assert.Equal(
                new byte[] { 0, 97, 115, 109, 1, 0, 0, 0 },
                reader.ReadBytes(8));
            var sections = new List<ObjectSection>();
            while (!reader.End)
            {
                var id = reader.ReadByte();
                var size = reader.ReadUnsigned();
                var payload = reader.ReadBytes(checked((int)size));
                if (id != 0)
                {
                    sections.Add(new(id, null, payload));
                    continue;
                }
                var custom = new ObjectReader(payload);
                var name = custom.ReadString();
                sections.Add(new(id, name, custom.ReadRemaining()));
            }
            return sections;
        }
    }

    private sealed class ObjectReader(byte[] bytes)
    {
        public int Position { get; private set; }
        public bool End => Position == bytes.Length;

        public byte ReadByte()
        {
            Assert.True(Position < bytes.Length);
            return bytes[Position++];
        }

        public uint ReadUnsigned()
        {
            uint value = 0;
            var shift = 0;
            while (true)
            {
                var current = ReadByte();
                value |= (uint)(current & 0x7f) << shift;
                if ((current & 0x80) == 0)
                {
                    return value;
                }
                shift += 7;
                Assert.True(shift < 35);
            }
        }

        public string ReadString() =>
            System.Text.Encoding.UTF8.GetString(
                ReadBytes(checked((int)ReadUnsigned())));

        public byte[] ReadBytes(int length)
        {
            Assert.InRange(length, 0, bytes.Length - Position);
            var result = bytes.AsSpan(Position, length).ToArray();
            Position += length;
            return result;
        }

        public byte[] ReadRemaining() => ReadBytes(bytes.Length - Position);

        public void AssertEnd() => Assert.Equal(bytes.Length, Position);
    }
}
