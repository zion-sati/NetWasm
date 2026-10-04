using System.Text;
using NetWasm.Runtime.Pack.Materialization;

namespace NetWasm.Runtime.Pack.Tests.Materialization;

public sealed class LinkedRuntimeModuleReaderTests
{
    [Theory]
    [InlineData(false, 65_552L, 4L, 32_768L)]
    [InlineData(true, 4_294_967_312L, 65_540L, 131_072L)]
    public void ReadsOwnedBoundsAndMemoryLimitsWithoutInstantiation(
        bool memory64, long globalBase, long initialPages, long maximumPages)
    {
        var globals = Bounds(memory64, globalBase);
        var module = Module(
            (0, [1, 2, 3]),
            Imports((0, "env", "callback"), (4, "env", "exception")),
            Memory(memory64, (ulong)initialPages, (ulong)maximumPages),
            Globals([new(0x7d, 0, 0x43, [0, 0, 0, 0]),
                new(0x7c, 1, 0x44, [0, 0, 0, 0, 0, 0, 0, 0]), .. globals]),
            Exports(2));
        var reader = Assert.IsAssignableFrom<ILinkedRuntimeModuleReader>(new LinkedRuntimeModuleReader());

        var result = reader.Read(module).MemoryLayout;

        Assert.Equal(new RuntimeLinkedMemoryLayout(memory64 ? "wasm64" : "wasm32",
            globalBase, globalBase + 1_024, globalBase + 1_024, globalBase + 66_560,
            globalBase + 66_576, initialPages * 65_536, maximumPages * 65_536), result);
    }

    [Fact]
    public void ReadsUnsignedWasm32AddressesAboveSignedIntegerRange()
    {
        var result = Read(Module(Memory(false, 65_536, 65_536),
            Globals(Bounds(false, 3_000_000_000)), Exports()));

        Assert.Equal(3_000_000_000, result.RuntimeGlobalBase);
        Assert.Equal(4_294_967_296, result.InitialMemorySizeBytes);
    }

    [Fact]
    public void AcceptsNonMinimalButValidLimitEncoding()
    {
        var result = Read(Module((5, [1, 0x81, 0, 0x84, 0, 0xa0, 0]),
            Globals(Bounds(false)), Exports()));

        Assert.Equal(262_144, result.InitialMemorySizeBytes);
        Assert.Equal(2_097_152, result.MaximumMemorySizeBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AcceptsLegalPaddedSignedConstants(bool memory64)
    {
        var globals = Bounds(memory64);
        var positive = Enumerable.Repeat((byte)0x80, memory64 ? 9 : 4).Append((byte)0).ToArray();
        globals[0] = globals[0] with { Value = positive };
        var result = Read(Module(Memory(memory64), Globals(globals), Exports()));

        Assert.Equal(0, result.RuntimeGlobalBase);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]
    public void RejectsTruncatedHeader(int length) =>
        Assert.Throws<InvalidOperationException>(() => Read(Module()[..length]));

    [Fact]
    public void RejectsInvalidCoreModuleHeader()
    {
        var module = Module();
        module[0] = 1;
        Assert.Throws<InvalidOperationException>(() => Read(module));
    }

    [Fact]
    public void RejectsMissingMemory() =>
        Assert.Throws<InvalidOperationException>(() => Read(Module(Globals(Bounds(false)), Exports())));

    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void RejectsDuplicateAuthoritySections(byte section) =>
        Assert.Throws<InvalidOperationException>(() => Read(Module((section, [0]), (section, [0]))));

    [Fact]
    public void RejectsMultipleDefinedMemories() =>
        Assert.Throws<InvalidOperationException>(() => Read(Module((5, [2, 1, 4, 32, 1, 4, 32]))));

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(7)]
    [InlineData(9)]
    public void RejectsUnsupportedMemoryLimits(byte flags) =>
        Assert.Throws<InvalidOperationException>(() => Read(Module((5, [1, flags]))));

    [Fact]
    public void RejectsInitialMemoryExceedingMaximum() =>
        Assert.Throws<InvalidOperationException>(() => Read(Module(Memory(false, 33, 32))));

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    public void RejectsNonFunctionOrTagImports(byte kind) =>
        Assert.Throws<InvalidOperationException>(() => Read(Module(Imports((kind, "env", "unsupported")))));

    [Fact]
    public void RejectsInvalidTagAttribute()
    {
        var section = Imports((4, "env", "exception"));
        section.Payload[^2] = 1;
        Assert.Throws<InvalidOperationException>(() => Read(Module(section)));
    }

    [Theory]
    [InlineData(0x7f, 0x42)]
    [InlineData(0x7e, 0x41)]
    [InlineData(0x7f, 0x43)]
    [InlineData(0x7f, 0x44)]
    [InlineData(0x7f, 0x23)]
    public void RejectsInconsistentOrNonConstantGlobals(byte type, byte opcode) =>
        Assert.Throws<InvalidOperationException>(() => Read(Module(
            Globals([new(type, 0, opcode, [0])]))));

    [Fact]
    public void RejectsInvalidGlobalMutability() =>
        Assert.Throws<InvalidOperationException>(() => Read(Module(
            Globals([new(0x7f, 2, 0x41, [0])]))));

    [Fact]
    public void RejectsIncompleteConstantExpression()
    {
        var section = Globals(Bounds(false));
        section.Payload[^1] = 0;
        Assert.Throws<InvalidOperationException>(() => Read(Module(section)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectsWrongWidthAndMutableAuthority(bool memory64)
    {
        Assert.Throws<InvalidOperationException>(() => Read(Module(Memory(memory64),
            Globals(Bounds(!memory64)), Exports())));
        var globals = Bounds(memory64);
        globals[0] = globals[0] with { Mutable = 1 };
        Assert.Throws<InvalidOperationException>(() => Read(Module(Memory(memory64), Globals(globals), Exports())));
    }

    [Fact]
    public void RejectsNegativeMemory64Bounds()
    {
        var globals = Bounds(true);
        globals[0] = globals[0] with { Value = Signed(-1) };
        Assert.Throws<InvalidOperationException>(() => Read(Module(Memory(true), Globals(globals), Exports())));
    }

    [Fact]
    public void RejectsFloatingPointBounds()
    {
        var globals = Bounds(false);
        globals[0] = new(0x7d, 0, 0x43, [0, 0, 0, 0]);
        Assert.Throws<InvalidOperationException>(() => Read(Module(Memory(false), Globals(globals), Exports())));
    }

    [Fact]
    public void RejectsMissingAndDuplicateExports()
    {
        Assert.Throws<InvalidOperationException>(() => Read(Module(Memory(false), Globals(Bounds(false)))));
        Assert.Throws<InvalidOperationException>(() => Read(Module(Memory(false), Globals(Bounds(false)),
            ExportEntries(("__global_base", 3, 0), ("__global_base", 3, 0)))));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(3, 5)]
    public void RejectsNonGlobalOrOutOfRangeExport(byte kind, uint index) =>
        Assert.Throws<InvalidOperationException>(() => Read(Module(Memory(false), Globals(Bounds(false)),
            ExportEntries(("__global_base", kind, index)))));

    [Fact]
    public void RejectsSectionTrailingBytes()
    {
        var section = Memory(false);
        Assert.Throws<InvalidOperationException>(() => Read(Module((section.Id, [.. section.Payload, 0]))));
    }

    [Fact]
    public void RejectsInvalidUtf8WithoutLeakingDecoderException()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Read(Module((7, [1, 1, 0xff, 3, 0]))));
        Assert.IsType<DecoderFallbackException>(exception.InnerException);
    }

    [Fact]
    public void RejectsMemorySizeOverflowWithoutLeakingArithmeticException()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Read(Module(
            Memory(true, 4, ulong.MaxValue), Globals(Bounds(true)), Exports())));
        Assert.IsType<OverflowException>(exception.InnerException);
    }

    [Fact]
    public void RejectsInvalidUnsignedAndSignedLebEncodings()
    {
        Assert.Throws<InvalidOperationException>(() => Read(Module((5, [1, 1, 0x80]))));
        Assert.Throws<InvalidOperationException>(() => Read(Module((5, [1, 1, 0x80, 0x80, 0x80, 0x80, 0x10]))));
        Assert.Throws<InvalidOperationException>(() => Read(Module((5, [1, 1, 0x80, 0x80, 0x80, 0x80, 0x80]))));
        Assert.Throws<InvalidOperationException>(() => Read(Module((5, [1, 5, .. Enumerable.Repeat((byte)0x80, 9), 2]))));
        foreach (var memory64 in new[] { false, true })
        {
            var globals = Bounds(memory64);
            globals[0] = globals[0] with { Value = Enumerable.Repeat((byte)0x80, memory64 ? 9 : 4)
                .Append((byte)0x10).ToArray() };
            Assert.Throws<InvalidOperationException>(() => Read(Module(Globals(globals))));
            globals[0] = globals[0] with { Value = Enumerable.Repeat((byte)0x80, memory64 ? 10 : 5).ToArray() };
            Assert.Throws<InvalidOperationException>(() => Read(Module(Globals(globals))));
        }
    }

    [Fact]
    public void RejectsOversizedAndTruncatedSections()
    {
        var oversized = Module();
        Assert.Throws<InvalidOperationException>(() => Read([.. oversized, 5, 0xff, 0xff, 0xff, 0xff, 7]));
        Assert.Throws<InvalidOperationException>(() => Read([.. oversized, 5, 2, 1]));
        Assert.Throws<InvalidOperationException>(() => Read(Module((7, [1, 2, 0]))));
    }

    [Fact]
    public void PreservesFunctionIndexSpacesAndUnrelatedNonScalarTypes()
    {
        var module = Module((1, [2, 0x60, 2, 0x7f, 0x7e, 1, 0x7d, 0x60, 1, 0x70, 0]),
            Imports((0, "env", "callback"), (4, "env", "exception")), (3, [2, 0, 1]),
            Memory(false), Globals(Bounds(false)), ExportsWithFunctions());
        var facts = new LinkedRuntimeModuleReader().Read(module);
        Assert.Equal(2, facts.FunctionTypes.Length);
        Assert.Equal([0x7f, 0x7e], facts.FunctionTypes[0].Parameters.Select(value => (int)value).ToArray());
        Assert.Equal([0x7d], facts.FunctionTypes[0].Results.Select(value => (int)value).ToArray());
        Assert.Equal([0x70], facts.FunctionTypes[1].Parameters.Select(value => (int)value).ToArray());
        Assert.Empty(facts.FunctionTypes[1].Results);
        Assert.Equal([0u], facts.ImportedFunctionTypeIndices.ToArray());
        Assert.Equal([0u, 1u], facts.DefinedFunctionTypeIndices.ToArray());
        Assert.Equal(new[] { new RuntimeLinkedImport("env", "callback", 0, 0),
            new RuntimeLinkedImport("env", "exception", 4, 0) }, facts.Imports);
        Assert.Equal(new RuntimeLinkedExport("native_function", 0, 1), facts.Exports.Single(export => export.Name == "native_function"));
    }

    [Fact]
    public void RejectsUnsupportedTypeFormAndTruncatedFunctionTypes()
    {
        Assert.Throws<InvalidOperationException>(() => Read(Module((1, [1, 0x5f]))));
        Assert.Throws<InvalidOperationException>(() => Read(Module((1, [1, 0x60, 2, 0x7f]))));
        Assert.Throws<InvalidOperationException>(() => Read(Module((3, [1]))));
    }

    private static (byte Id, byte[] Payload) ExportsWithFunctions() => ExportEntries(
        ("__global_base", 3, 0), ("__data_end", 3, 1), ("__stack_low", 3, 2),
        ("__stack_high", 3, 3), ("__heap_base", 3, 4), ("native_function", 0, 1));

    private static RuntimeLinkedMemoryLayout Read(byte[] module) => new LinkedRuntimeModuleReader().Read(module).MemoryLayout;

    private static Global[] Bounds(bool memory64, long start = 65_552) =>
        new long[] { start, start + 1_024, start + 1_024, start + 66_560, start + 66_576 }
            .Select(value => new Global(memory64 ? (byte)0x7e : (byte)0x7f, 0,
                memory64 ? (byte)0x42 : (byte)0x41, Signed(memory64 ? value : unchecked((int)value))))
            .ToArray();

    private static (byte Id, byte[] Payload) Memory(bool memory64, ulong minimum = 4, ulong maximum = 32) =>
        Section(5, writer =>
        {
            Unsigned(writer, 1);
            Unsigned(writer, memory64 ? 5u : 1u);
            Unsigned(writer, minimum);
            Unsigned(writer, maximum);
        });

    private static (byte Id, byte[] Payload) Globals(Global[] globals) => Section(6, writer =>
    {
        Unsigned(writer, (uint)globals.Length);
        foreach (var global in globals)
        {
            writer.Write(global.Type);
            writer.Write(global.Mutable);
            writer.Write(global.Opcode);
            writer.Write(global.Value);
            writer.Write((byte)0x0b);
        }
    });

    private static (byte Id, byte[] Payload) Exports(uint offset = 0) => ExportEntries(
        ("__global_base", 3, offset), ("__data_end", 3, offset + 1), ("__stack_low", 3, offset + 2),
        ("__stack_high", 3, offset + 3), ("__heap_base", 3, offset + 4));

    private static (byte Id, byte[] Payload) ExportEntries(params (string Name, byte Kind, uint Index)[] exports) =>
        Section(7, writer =>
        {
            Unsigned(writer, (uint)exports.Length);
            foreach (var export in exports)
            {
                Name(writer, export.Name);
                writer.Write(export.Kind);
                Unsigned(writer, export.Index);
            }
        });

    private static (byte Id, byte[] Payload) Imports(params (byte Kind, string Module, string Name)[] imports) =>
        Section(2, writer =>
        {
            Unsigned(writer, (uint)imports.Length);
            foreach (var import in imports)
            {
                Name(writer, import.Module);
                Name(writer, import.Name);
                writer.Write(import.Kind);
                if (import.Kind == 4)
                    writer.Write((byte)0);
                Unsigned(writer, 0);
            }
        });

    private static byte[] Module(params (byte Id, byte[] Payload)[] sections)
    {
        using var bytes = new MemoryStream();
        using var writer = new BinaryWriter(bytes);
        writer.Write("\0asm\x01\0\0\0"u8);
        foreach (var section in sections)
        {
            writer.Write(section.Id);
            Unsigned(writer, (uint)section.Payload.Length);
            writer.Write(section.Payload);
        }
        return bytes.ToArray();
    }

    private static (byte Id, byte[] Payload) Section(byte id, Action<BinaryWriter> write)
    {
        using var bytes = new MemoryStream();
        using var writer = new BinaryWriter(bytes);
        write(writer);
        return (id, bytes.ToArray());
    }

    private static void Name(BinaryWriter writer, string name)
    {
        var bytes = Encoding.UTF8.GetBytes(name);
        Unsigned(writer, (uint)bytes.Length);
        writer.Write(bytes);
    }

    private static void Unsigned(BinaryWriter writer, ulong value)
    {
        do
        {
            var item = (byte)(value & 0x7f);
            value >>= 7;
            writer.Write((byte)(item | (value == 0 ? 0 : 0x80)));
        } while (value != 0);
    }

    private static byte[] Signed(long value)
    {
        using var bytes = new MemoryStream();
        do
        {
            var item = (byte)(value & 0x7f);
            value >>= 7;
            var finished = (value == 0 && (item & 0x40) == 0) || (value == -1 && (item & 0x40) != 0);
            bytes.WriteByte((byte)(item | (finished ? 0 : 0x80)));
            if (finished)
                return bytes.ToArray();
        } while (true);
    }

    private sealed record Global(byte Type, byte Mutable, byte Opcode, byte[] Value);
}
