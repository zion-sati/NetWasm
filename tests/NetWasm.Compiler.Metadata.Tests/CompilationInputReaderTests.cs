using System.Collections.Frozen;
using System.Collections.Immutable;
using System.IO.Hashing;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.NativeInterop;
using NetWasm.Compiler.Metadata.Inputs;

namespace NetWasm.Compiler.Metadata.Tests;

// An assembly's shape is everything but its method bodies: an edit that changes only
// bodies keeps it, and any other change, in any metadata table or its resources, does not.
public sealed class CompilationInputReaderTests
{
    // The IL scan steps over each instruction by its operand size: the specification's for
    // every opcode ECMA-335 defines, and none (the scan stops) for any other byte.
    [Fact]
    public void EveryOpcodesOperandSizeIsTheSpecifications()
    {
        var defined = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => (OpCode)field.GetValue(null)!)
            .Where(opcode => opcode.OpCodeType != OpCodeType.Nternal)
            .ToArray();
        for (var value = 0; value <= 0xFF; value++)
        {
            // switch and the two-byte prefix are read before the table.
            if (value is 0x45 or 0xFE) continue;
            var opcode = defined.SingleOrDefault(candidate => candidate.Size == 1 && (byte)candidate.Value == value);
            Assert.True(Size(opcode) == CompilationInputReader.BodyHash.OneByteOperandSize((byte)value), $"0x{value:x2}");
        }
        for (var value = 0; value <= 0xFF; value++)
        {
            var opcode = defined.SingleOrDefault(candidate => candidate.Size == 2 && (byte)candidate.Value == value);
            // no. (0xFE 0x19) is ECMA-335's, though Reflection.Emit has no field for it.
            var expected = value == 0x19 ? 1 : Size(opcode);
            Assert.True(expected == CompilationInputReader.BodyHash.TwoByteOperandSize((byte)value), $"0xfe 0x{value:x2}");
        }
    }

    private static int Size(OpCode opcode) => opcode.Size == 0 ? -1 : opcode.OperandType switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        _ => 4,
    };

    // A string literal is hashed by its text, so a literal another body's edit moves in the
    // heap is unchanged; after an opcode the scan does not know, bytes are taken as they are.
    [Theory]
    [InlineData(new byte[] { 0x24, 0x01 }, new byte[] { 0x24, 0x02 })]
    [InlineData(new byte[] { 0xFE, 0x08, 0x01 }, new byte[] { 0xFE, 0x08, 0x02 })]
    [InlineData(new byte[] { 0x00, 0xFE }, new byte[] { 0x00, 0xFF })]
    [InlineData(new byte[] { 0x45, 0x01, 0x00 }, new byte[] { 0x45, 0x01, 0x01 })]
    [InlineData(new byte[] { 0x45, 0xFF, 0xFF, 0xFF, 0xFF, 0x2A }, new byte[] { 0x45, 0xFF, 0xFF, 0xFF, 0xFF, 0x00 })]
    [InlineData(new byte[] { 0x45, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x2A },
        new byte[] { 0x45, 0x01, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0x2A })]
    public void IlAfterAnOpcodeTheScanDoesNotKnowIsHashedAsItIs(byte[] left, byte[] right)
    {
        var reader = Reader(Image());

        Assert.NotEqual(Hash(reader, left), Hash(reader, right));
    }

    [Fact]
    public void AMovedStringLiteralHashesAsItsText()
    {
        var before = Reader(Image(literalsBefore: []));
        var after = Reader(Image(literalsBefore: ["padding"]));
        var (literal, other) = (Literal(before, "literal"), Literal(before, "other"));

        Assert.NotEqual(literal, Literal(after, "literal"));
        Assert.Equal(Hash(before, Ldstr(literal)), Hash(after, Ldstr(Literal(after, "literal"))));
        Assert.NotEqual(Hash(before, Ldstr(literal)), Hash(before, Ldstr(other)));
    }

    // The token of the image's user string of the given text.
    private static int Literal(MetadataReader reader, string text)
    {
        var handle = MetadataTokens.UserStringHandle(1);
        while (reader.GetUserString(handle) != text) handle = reader.GetNextHandle(handle);
        return MetadataTokens.GetToken(handle);
    }

    [Fact]
    public void ABuildOfTheSameAssemblyHasItsShape() =>
        Assert.Equal(Shape(Image()), Shape(Image()));

    [Theory]
    [InlineData("module reference")]
    [InlineData("resource")]
    [InlineData("resource data")]
    [InlineData("exported type")]
    [InlineData("file")]
    [InlineData("security")]
    [InlineData("property accessor")]
    [InlineData("event accessor")]
    public void EveryTableAndTheResourcesShapeTheAssembly(string change) =>
        Assert.NotEqual(Shape(Image()), Shape(Image(change)));

    private static InputFingerprint Shape(byte[] image)
    {
        var assembly = ManagedAssembly.Parse("memory/Shapes.dll", image, AssemblyIdentityAliases.Empty,
            new StackKinds(), new NoNativeImports());
        var snapshot = new MetadataCompilationSnapshot([assembly], assembly.Identity, [], [], []);
        return new CompilationInputReader().Read(snapshot, CompilationInputs.Empty).Assemblies.Single().Detail!.Shape;
    }

    private static MetadataReader Reader(byte[] image) => new PEReader(ImmutableArray.Create(image)).GetMetadataReader();

    private static UInt128 Hash(MetadataReader reader, byte[] il)
    {
        var hash = new XxHash128();
        CompilationInputReader.BodyHash.AppendIl(hash, reader, il);
        return hash.GetCurrentHashAsUInt128();
    }

    private static byte[] Ldstr(int token) =>
        [0x72, (byte)token, (byte)(token >> 8), (byte)(token >> 16), (byte)(token >> 24), 0x2A];

    private static byte[] Image(string change = "", string[]? literalsBefore = null)
    {
        var metadata = new MetadataBuilder();
        foreach (var literal in literalsBefore ?? []) metadata.GetOrAddUserString(literal);
        metadata.GetOrAddUserString("literal");
        metadata.GetOrAddUserString("other");
        metadata.AddModule(0, metadata.GetOrAddString("Shapes.dll"), metadata.GetOrAddGuid(Guid.NewGuid()), default, default);
        metadata.AddAssembly(metadata.GetOrAddString("Shapes"), new(1, 0), default, default, 0, 0);
        var runtime = metadata.AddAssemblyReference(metadata.GetOrAddString("System.Runtime"), new(10, 0),
            default, default, 0, default);
        var objectType = metadata.AddTypeReference(runtime, metadata.GetOrAddString("System"), metadata.GetOrAddString("Object"));
        metadata.AddTypeDefinition(TypeAttributes.NotPublic, default, metadata.GetOrAddString("<Module>"), default,
            MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
        var holder = metadata.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Shapes"), metadata.GetOrAddString("Holder"), objectType,
            MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
        var signature = new BlobBuilder();
        new BlobEncoder(signature).MethodSignature(isInstanceMethod: true).Parameters(0, returnType => returnType.Void(), _ => { });
        MethodDefinitionHandle Accessor(string name) => metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Abstract | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            MethodImplAttributes.IL, metadata.GetOrAddString(name), metadata.GetOrAddBlob(signature), -1, default);
        var first = Accessor("First");
        var second = Accessor("Second");
        var propertySignature = new BlobBuilder();
        new BlobEncoder(propertySignature).PropertySignature(isInstanceProperty: true)
            .Parameters(0, returnType => returnType.Type().Int32(), _ => { });
        var property = metadata.AddProperty(PropertyAttributes.None, metadata.GetOrAddString("Value"),
            metadata.GetOrAddBlob(propertySignature));
        metadata.AddPropertyMap(holder, property);
        metadata.AddMethodSemantics(property, MethodSemanticsAttributes.Other, change == "property accessor" ? second : first);
        var @event = metadata.AddEvent(EventAttributes.None, metadata.GetOrAddString("Changed"), objectType);
        metadata.AddEventMap(holder, @event);
        metadata.AddMethodSemantics(@event, MethodSemanticsAttributes.Other, change == "event accessor" ? first : second);
        metadata.AddModuleReference(metadata.GetOrAddString(change == "module reference" ? "native_b" : "native_a"));
        metadata.AddManifestResource(ManifestResourceAttributes.Public,
            metadata.GetOrAddString(change == "resource" ? "Shapes.other" : "Shapes.data"), default, 0);
        metadata.AddExportedType(TypeAttributes.Public, metadata.GetOrAddString("System"),
            metadata.GetOrAddString(change == "exported type" ? "Other" : "Forwarded"), runtime, 0);
        metadata.AddAssemblyFile(metadata.GetOrAddString(change == "file" ? "other.netmodule" : "shapes.netmodule"),
            metadata.GetOrAddBlob(new byte[20]), containsMetadata: false);
        metadata.AddDeclarativeSecurityAttribute(EntityHandle.AssemblyDefinition,
            change == "security" ? DeclarativeSecurityAction.RequestOptional : DeclarativeSecurityAction.RequestMinimum,
            metadata.GetOrAddBlob(new byte[] { 0x2E, 0x00 }));
        var resources = new BlobBuilder();
        byte[] data = change == "resource data" ? [1, 2, 4] : [1, 2, 3];
        resources.WriteInt32(data.Length);
        resources.WriteBytes(data);
        var image = new BlobBuilder();
        new ManagedPEBuilder(new(imageCharacteristics: Characteristics.Dll), new(metadata), new BlobBuilder(),
            managedResources: resources).Serialize(image);
        return image.ToArray();
    }

    private sealed class StackKinds : IValueTypeDefinitionStackKindResolver
    {
        public CliValueKind Resolve(string canonicalName) => CliValueKind.ValueType;
    }

    private sealed class NoNativeImports : INativeImportDeclarationReader
    {
        public NativeImportDeclaration? Read(MetadataReader metadata, MethodDefinition method) => null;
    }
}

// How a compile's inputs differ from the last compile's, and the form they are kept in.
public sealed class CompilationInputsTests
{
    // An assembly read with detail is shaped by it; one read without, by all its content.
    [Fact]
    public void AnAssemblysShapeIsItsDetailsOrItsContent()
    {
        var detail = new AssemblyInputDetail(new(7, 7), System.Collections.Frozen.FrozenDictionary<int, InputFingerprint>.Empty);

        Assert.Equal(new InputFingerprint(7, 7), new AssemblyInputs("App", new(1, 1), detail).Shape);
        Assert.Equal(new InputFingerprint(1, 1), new AssemblyInputs("App", new(1, 1), null).Shape);
    }

    private static readonly InputFingerprint Shape = new(1, 2);

    private static AssemblyInputs Assembly(string name, ulong content, params (int Token, ulong Body)[] bodies) =>
        new(name, new(content, content), new(Shape,
            bodies.ToDictionary(body => body.Token, body => new InputFingerprint(body.Body, body.Body)).ToFrozenDictionary()));

    [Fact]
    public void ARemovedAssemblyIsMoreThanABodyEdit()
    {
        var changes = CompilationInputChanges.Between(new([Assembly("App", 1), Assembly("Lib", 2)]), new([Assembly("App", 1)]));

        Assert.Equal(1, changes.Removed);
        Assert.False(changes.IsBodiesOnly);
        Assert.Contains("1 removed; shape changed", changes.Describe(), StringComparison.Ordinal);
    }

    // As many bodies as before, but one the last compile did not have.
    [Fact]
    public void ABodyTheLastCompileDidNotHaveChangesTheShape()
    {
        var changes = CompilationInputChanges.Between(new([Assembly("App", 1, (0x06000001, 1))]),
            new([Assembly("App", 2, (0x06000002, 1))]));

        Assert.Equal(AssemblyInputChange.ShapeChanged, Assert.Single(changes.Assemblies).Change);
    }

    [Fact]
    public void EachEditedBodyIsNamedByItsToken()
    {
        var changes = CompilationInputChanges.Between(new([Assembly("App", 1, (0x06000001, 1), (0x06000002, 1))]),
            new([Assembly("App", 2, (0x06000001, 3), (0x06000002, 4))]));

        Assert.Equal("App: BodiesChanged 2 (0x06000001, 0x06000002)\n0 unchanged, 0 removed; bodies only\n", changes.Describe());
    }

    [Fact]
    public void KeptInputsReadBackAsTheyWereWritten()
    {
        var inputs = new CompilationInputs([Assembly("App", 1, (0x06000001, 5)), new("Lib", new(2, 2), null)])
        {
            Contributions = new Dictionary<string, MethodContributionRecord>
            {
                ["App::Run"] = new("App", 0x06000001, new(7, 8)),
            }.ToFrozenDictionary(StringComparer.Ordinal),
        };

        var read = CompilationInputsFormat.Read(CompilationInputsFormat.Write(inputs));

        Assert.Equal(inputs.Assemblies.Select(assembly => (assembly.Name, assembly.Content, assembly.Detail?.Shape)),
            read.Assemblies.Select(assembly => (assembly.Name, assembly.Content, assembly.Detail?.Shape)));
        Assert.Equal(inputs.Contributions["App::Run"], read.Contributions["App::Run"]);
    }

    // Inputs kept by another version, or damaged, read as none: the next compile reads all.
    [Theory]
    [InlineData("short")]
    [InlineData("magic")]
    [InlineData("version")]
    [InlineData("assemblies")]
    [InlineData("bodies")]
    [InlineData("contributions")]
    [InlineData("truncated")]
    public void InputsThatAreNotTheseReadAsNone(string damage)
    {
        var bytes = CompilationInputsFormat.Write(new([Assembly("App", 1, (0x06000001, 5))]));
        // Magic, version and count; the name "App" (length-prefixed), content (16), detail flag,
        // shape (16), then the body count.
        var bodies = 12 + 4 + 16 + 1 + 16;
        bytes = damage switch
        {
            "short" => bytes[..8],
            "magic" => Patched(bytes, 0, 0),
            "version" => Patched(bytes, 4, 99),
            "assemblies" => Patched(bytes, 8, -1),
            "bodies" => Patched(bytes, bodies, -1),
            "contributions" => Patched(bytes, bytes.Length - 4, -1),
            _ => bytes[..^5],
        };

        Assert.Empty(CompilationInputsFormat.Read(bytes).Assemblies);
    }

    private static byte[] Patched(byte[] bytes, int at, int value)
    {
        var copy = bytes.ToArray();
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(copy.AsSpan(at), value);
        return copy;
    }
}
