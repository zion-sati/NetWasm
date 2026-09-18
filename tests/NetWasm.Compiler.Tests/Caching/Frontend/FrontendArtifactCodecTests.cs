using System.Collections.Immutable;
using System.IO;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Caching.Frontend;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Calls;
using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;

namespace NetWasm.Compiler.Tests.Caching.Frontend;

public sealed class FrontendArtifactCodecTests
{
    private static readonly AssemblyIdentity Assembly = new("Dependency");

    [Fact]
    public void RoundTripPreservesEveryTaggedVariantDeterministically()
    {
        var snapshot = CreateStressSnapshot();
        snapshot = snapshot with
        {
            Analysis = snapshot.Analysis with
            {
                Instructions = snapshot.Analysis.Instructions with
                {
                    Strings = snapshot.Analysis.Instructions.Strings.Add(
                        "\ud800x\udfff"),
                },
            },
        };
        var encoder = new FrontendArtifactEncoder();
        var decoder = new FrontendArtifactDecoder();

        var payload = encoder.Encode(snapshot);
        var decoded = decoder.Decode(payload);

        Assert.Equal(payload.ToArray(), encoder.Encode(decoded).ToArray());
        Assert.Equal(16, decoded.Analysis.Body.Instructions.Length);
        Assert.Equal(9, decoded.StructuredMethod.Body.Regions.Length);
        Assert.Equal(5, decoded.StructuredMethod.Blocks.Count);
        Assert.Equal(2, decoded.StructuredMethod.ExceptionGroups[Group(1)].ProtectedParts.Length);
        Assert.True(decoded.Analysis.Method.Definition.JSImport!.IsPromise);
        Assert.Equal("module", decoded.Analysis.Method.Definition.JSImport.ModuleName);
        Assert.Equal("export", decoded.Analysis.Method.Definition.JSExport!.ExportName);
        Assert.Equal("interface", decoded.Analysis.Method.Definition.WitImport!.InterfaceName);
        Assert.Equal("wit-export", decoded.Analysis.Method.Definition.WitExport!.FunctionName);
        Assert.Equal(
            "post-return",
            decoded.Analysis.Method.Definition.WitPostReturn!.FunctionName);
        Assert.Contains("\ud800x\udfff", decoded.Analysis.Instructions.Strings);
        Assert.Same(
            decoded.Analysis.Body.Instructions[0],
            decoded.StructuredMethod.Header.Instructions[0]);
    }

    [Fact]
    public void IndependentEquivalentGraphsEncodeIdentically()
    {
        var encoder = new FrontendArtifactEncoder();

        var first = encoder.Encode(CreateStressSnapshot());
        var second = encoder.Encode(CreateStressSnapshot());

        Assert.Equal(first.ToArray(), second.ToArray());
    }

    [Fact]
    public void RoundTripPreservesDefaultCollectionsAndNullOptions()
    {
        var snapshot = CreateStressSnapshot();
        var analysis = snapshot.Analysis;
        var body = analysis.Body with
        {
            MethodInstance = null,
            LocalSignatureTypes = default,
            ExceptionRegions = default,
        };
        var defaultBytes = analysis.Body.Instructions.SetItem(
            analysis.Body.Instructions.Length - 1,
            analysis.Body.Instructions[^1] with { Operand = new CilOperand.ByteData(default) });
        var defaultTableArrays = defaultBytes
            .Select(instruction => instruction.Operand is CilOperand.SwitchTargets
                ? instruction with { Operand = new CilOperand.SwitchTargets(default) }
                : instruction)
            .ToImmutableArray();
        var sparse = snapshot with
        {
            Analysis = analysis with
            {
                Method = analysis.Method with { MethodArguments = default },
                Body = body with { Instructions = defaultTableArrays },
                CatchTypes = default,
                Exceptions = default,
                Instructions = new(
                    default,
                    default,
                    default,
                    default,
                    default,
                    default,
                    default,
                    default,
                    default,
                    default),
            },
        };
        var encoder = new FrontendArtifactEncoder();

        var decoded = new FrontendArtifactDecoder().Decode(encoder.Encode(sparse));

        Assert.True(decoded.Analysis.Body.LocalSignatureTypes.IsDefault);
        Assert.True(decoded.Analysis.Body.ExceptionRegions.IsDefault);
        Assert.Null(decoded.Analysis.Body.MethodInstance);
        Assert.True(decoded.Analysis.CatchTypes.IsDefault);
        Assert.True(decoded.Analysis.Exceptions.IsDefault);
        Assert.True(decoded.Analysis.Instructions.RuntimeTypes.IsDefault);
        Assert.True(Assert.IsType<CilOperand.ByteData>(
            decoded.Analysis.Body.Instructions[^1].Operand).Value.IsDefault);
        Assert.Equal(encoder.Encode(sparse).ToArray(), encoder.Encode(decoded).ToArray());
    }

    [Fact]
    public void EncoderRejectsUnknownOpenStructuredVariants()
    {
        var snapshot = CreateStressSnapshot();
        var encoder = new FrontendArtifactEncoder();
        var firstBlock = snapshot.StructuredMethod.Blocks.First();
        var unknownExit = snapshot with
        {
            StructuredMethod = snapshot.StructuredMethod with
            {
                Blocks = snapshot.StructuredMethod.Blocks.SetItem(
                    firstBlock.Key,
                    firstBlock.Value with { Exit = new UnsupportedExit() }),
            },
        };
        var unknownRegion = snapshot with
        {
            StructuredMethod = snapshot.StructuredMethod with
            {
                Body = new([new UnsupportedRegion()]),
            },
        };
        var group = snapshot.StructuredMethod.ExceptionGroups[Group(1)];
        var unknownPart = snapshot with
        {
            StructuredMethod = snapshot.StructuredMethod with
            {
                ExceptionGroups = snapshot.StructuredMethod.ExceptionGroups.SetItem(
                    Group(1),
                    group with { ProtectedParts = [new UnsupportedPart()] }),
            },
        };
        var nullOperand = snapshot with
        {
            Analysis = snapshot.Analysis with
            {
                Body = snapshot.Analysis.Body with
                {
                    Instructions = snapshot.Analysis.Body.Instructions.SetItem(
                        0,
                        snapshot.Analysis.Body.Instructions[0] with { Operand = null! }),
                },
            },
        };

        Assert.Throws<InvalidDataException>(() => encoder.Encode(unknownExit));
        Assert.Throws<InvalidDataException>(() => encoder.Encode(unknownRegion));
        Assert.Throws<InvalidDataException>(() => encoder.Encode(unknownPart));
        Assert.Throws<InvalidDataException>(() => encoder.Encode(nullOperand));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(18)]
    [InlineData(19)]
    [InlineData(20)]
    public void DecoderRejectsCorruptBoundedValues(int corruption)
    {
        var encoder = new FrontendArtifactEncoder();
        var payload = encoder.Encode(CreateStressSnapshot()).ToArray();
        var malformed = corruption switch
        {
            0 => CorruptFirstBoolean(payload),
            1 => CorruptFirstEnum(payload),
            2 => CorruptFirstCollectionLength(payload),
            3 => CorruptFirstStringLength(payload),
            4 => CorruptFirstOperandTag(payload),
            5 => CorruptTypeConsistency(payload),
            6 => CorruptFirstBlockExitTag(payload),
            7 => CorruptFirstRegionTag(payload),
            8 => CorruptFirstExceptionPartTag(payload),
            9 => RemoveFirstNamedAssembly(payload),
            10 => MakePrimitiveRequireMissingElement(payload),
            11 => CorruptFirstStackKind(payload),
            12 => MakeTypeReferenceItself(payload),
            13 => CorruptFirstRootMethodReference(payload),
            14 => CorruptPrimitiveName(payload),
            15 => AddTrailingTypeEntryData(payload),
            16 => CorruptRootCollectionLength(payload, int.MinValue),
            17 => CorruptRootCollectionLength(payload, int.MaxValue),
            18 => CorruptRootBoolean(payload),
            19 => CorruptRootEnum(payload),
            20 => CorruptRawStringLength(payload),
            _ => throw new InvalidOperationException(),
        };

        Assert.Throws<InvalidDataException>(() =>
            new FrontendArtifactDecoder().Decode([.. malformed]));
    }

    private static FrontendArtifactSnapshot CreateStressSnapshot()
    {
        var primitive = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        var owner = CliTypeIdentity.Named(Assembly, "Fixture", "Owner", false);
        var value = CliTypeIdentity.Named(Assembly, "Fixture", "Value", true)
            .WithStackStorageType(primitive);
        var array = CliTypeIdentity.Array(value, 2);
        var types = ImmutableArray.Create(
            primitive,
            owner,
            CliTypeIdentity.SzArray(value),
            array,
            CliTypeIdentity.GenericInstantiation(owner, [value]),
            CliTypeIdentity.GenericParameter(method: false, 0),
            CliTypeIdentity.GenericParameter(method: true, 0),
            CliTypeIdentity.ManagedByReference(value),
            CliTypeIdentity.UnmanagedPointer(value),
            CliTypeIdentity.Named(Assembly, "", "NoNamespace", false),
            CliTypeIdentity.Named(
                Assembly,
                "Fixture",
                "CompactValue",
                true,
                CliValueKind.I8),
            CliTypeIdentity.SzArray(value).WithStackKind(CliValueKind.I4));
        var definition = MethodDefinition(value);
        var method = new MethodInstanceModel(definition, owner, [value], definition.Signature);
        var field = Field(owner, value);
        var instructions = Operands(method, field, value);
        var body = new CilMethodBody(
            definition,
            4,
            [CliValueKind.I4, CliValueKind.ManagedReference],
            instructions)
        {
            MethodInstance = method,
            LocalSignatureTypes = [value, array],
            ExceptionRegions =
            [
                new(
                    CilExceptionRegionKind.Filter,
                    0,
                    1,
                    1,
                    1,
                    Key(0x02000002),
                    2),
            ],
        };
        var stackMap = ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty
            .Add(7, [CliValueKind.ManagedReference])
            .Add(0, [CliValueKind.I4]);
        var facts = new ReachabilityInstructionAnalysis(
            types,
            [value],
            [array],
            [Key(0x02000002)],
            ["text"],
            [new ReachabilityMethodReference(CilOperation.Call, method)],
            [new ReachabilityEntityReference(CilOperation.LoadTypeToken, Key(0x02000002))],
            [field],
            [new ReachabilityDispatch(
                "dispatch",
                new DispatchDeclaration("caller", 7, method, CilOperation.CallVirtual))],
            [method],
            [new ManagedCallSite(
                new ManagedCallSiteKey(new ManagedMethodIdentity("caller"), 7),
                ManagedCallOperation.Virtual,
                new ManagedMethodIdentity(method.CanonicalName),
                method,
                value)]);
        var analysis = new ReachableMethodAnalysisSnapshot(
            method,
            body,
            stackMap,
            stackMap,
            [Key(0x02000002)],
            [new ReachabilityExceptionRequirement(
                ManagedExceptionKind.NullReference,
                "System.NullReferenceException")],
            facts);
        var structured = Structured(definition, method, value, instructions, stackMap);
        return new(analysis, new(
            structured.Header,
            structured.EntryBlock,
            structured.Blocks,
            structured.Body,
            structured.TopLevelExceptionGroups,
            structured.ExceptionGroups,
            structured.InstructionEntryStacks));
    }

    private static MethodDefinitionModel MethodDefinition(CliTypeIdentity value) => new(
        Key(0x06000001),
        Key(0x02000001),
        "Run",
        false,
        new MethodSignatureModel(value, [value]),
        123)
    {
        GenericArity = 1,
        IsVirtual = true,
        IsNewSlot = true,
        IsFinal = true,
        IsAbstract = true,
        JSImport = new("function", "module") { IsPromise = true },
        JSExport = new("export"),
        WitImport = new("interface", "wit-import"),
        WitExport = new("interface", "wit-export"),
        WitPostReturn = new("interface", "post-return"),
    };

    private static FieldInstanceModel Field(
        CliTypeIdentity owner,
        CliTypeIdentity value) => new(
        new FieldDefinitionModel(
            Key(0x04000001),
            Key(0x02000001),
            "Field",
            value,
            true)
        {
            InitialData = [1, 2, 3],
            LiteralValue = 42,
        },
        owner,
        value);

    private static ImmutableArray<CilInstruction> Operands(
        MethodInstanceModel method,
        FieldInstanceModel field,
        CliTypeIdentity type)
    {
        CilOperand[] operands =
        [
            new CilOperand.None(),
            new CilOperand.ConstantI4(1),
            new CilOperand.ConstantI8(2),
            new CilOperand.ConstantF4(3),
            new CilOperand.ConstantF8(4),
            new CilOperand.Index(5),
            new CilOperand.BranchTarget(6),
            new CilOperand.SwitchTargets([7, 8]),
            new CilOperand.Entity(Key(0x02000002)),
            new CilOperand.MethodInstance(method),
            new CilOperand.FieldInstance(field),
            new CilOperand.TypeIdentity(type),
            new CilOperand.CallSite(method.Signature),
            new CilOperand.NumericConversion(32, true, true, true, true),
            new CilOperand.UserString("operand"),
            new CilOperand.ByteData([9, 10]),
        ];
        return
        [
            .. operands.Select((operand, index) => new CilInstruction(
                index,
                index + 1,
                CilOperation.Nop,
                operand)),
        ];
    }

    private static StructuredMethod Structured(
        MethodDefinitionModel definition,
        MethodInstanceModel method,
        CliTypeIdentity value,
        ImmutableArray<CilInstruction> instructions,
        ImmutableDictionary<int, ImmutableArray<CliValueKind>> stackMap)
    {
        var ids = Enumerable.Range(0, 5).Select(Block).ToArray();
        var occurrence = new StructuredBlockOccurrence(
            ids[0],
            StructuredBlockRole.ExecutingReplica,
            Continuation(1));
        var condition = new StructuredCondition(
            0,
            CilOperation.BranchIfTrue,
            1,
            CliValueKind.I4,
            CliValueKind.I4);
        var blocks = ImmutableDictionary<StructuredBlockId, StructuredBlockDefinition>.Empty
            .Add(ids[0], BlockDefinition(ids[0], new StructuredFallthroughExit(ids[1])))
            .Add(ids[1], BlockDefinition(ids[1], new StructuredBranchExit(1, ids[2])))
            .Add(ids[2], BlockDefinition(
                ids[2],
                new StructuredConditionalExit(condition, ids[3], ids[4])))
            .Add(ids[3], BlockDefinition(ids[3], new StructuredLeaveExit(3, ids[4])))
            .Add(ids[4], BlockDefinition(
                ids[4],
                new StructuredTerminalExit(instructions[0])));
        var dispatcher = new StructuredDispatcher(
            ids[0],
            [new(occurrence, ids[1], null)],
            [new(ids[4], StructuredSequence.Empty)]);
        var exceptionRegion = new StructuredExceptionRegion(Group(1), Continuation(2))
        {
            DispatcherContinuations = [Continuation(1), Continuation(2)],
        };
        var sequence = new StructuredSequence(
        [
            new StructuredCode(occurrence),
            new StructuredLoopBreak(),
            new StructuredLoopContinue(),
            new StructuredDispatcherContinue(ids[1]),
            exceptionRegion,
            dispatcher,
            new StructuredIf(occurrence, StructuredSequence.Empty, StructuredSequence.Empty),
            new StructuredLoop(
                occurrence,
                true,
                StructuredSequence.Empty,
                StructuredSequence.Empty,
                StructuredSequence.Empty),
            new StructuredPostTestLoop(
                StructuredSequence.Empty,
                occurrence,
                false,
                StructuredSequence.Empty,
                StructuredSequence.Empty),
        ]);
        var clause = new StructuredExceptionClause(
            CilExceptionRegionKind.Filter,
            1,
            2,
            Key(0x02000002),
            3,
            StructuredSequence.Empty,
            StructuredSequence.Empty)
        {
            HandlerBlock = ids[1],
            FilterBlock = ids[2],
        };
        var group = new StructuredExceptionGroup(
            Group(1),
            Group(0),
            9001,
            1,
            [
                new StructuredExceptionCode(StructuredSequence.Empty),
                new StructuredNestedExceptionGroup(Group(2)),
            ],
            [clause],
            [new(Continuation(1), 4, ids[4], StructuredSequence.Empty)],
            dispatcher,
            ids[4])
        {
            ProtectedBlocks = [ids[0], ids[1]],
        };
        return new(
            new(definition, method, 4, [CliValueKind.I4], [value], instructions),
            ids[0],
            blocks,
            sequence,
            [Group(1)],
            ImmutableDictionary<StructuredExceptionGroupId, StructuredExceptionGroup>.Empty
                .Add(Group(2), group with { Id = Group(2), Parent = null })
                .Add(Group(1), group),
            stackMap);
    }

    private static StructuredBlockDefinition BlockDefinition(
        StructuredBlockId id,
        StructuredBlockExit exit) => new(
        id,
        1000 + id.Value,
        [new(3000 + id.Value, 3001 + id.Value, CilOperation.Nop, new CilOperand.None())],
        [CliValueKind.I4],
        exit)
        {
            EndOffset = 2000 + id.Value,
        };

    private static byte[] CorruptFirstBoolean(byte[] payload)
    {
        var copy = payload.ToArray();
        var entry = TableEntry(copy, 0, 0);
        copy[entry.Offset + 12] = 2;
        return copy;
    }

    private static byte[] CorruptFirstEnum(byte[] payload)
    {
        var copy = payload.ToArray();
        var entry = TableEntry(copy, 0, 0);
        Array.Fill(copy, (byte)0xff, entry.Offset, sizeof(int));
        return copy;
    }

    private static byte[] CorruptFirstCollectionLength(byte[] payload)
    {
        var copy = payload.ToArray();
        var entry = TableEntry(copy, 0, 0);
        Array.Fill(copy, (byte)0xfe, entry.Offset + 14, sizeof(int));
        return copy;
    }

    private static byte[] CorruptFirstStringLength(byte[] payload)
    {
        var copy = payload.ToArray();
        Array.Fill(copy, (byte)0xff, 6, 4);
        return copy;
    }

    private static byte[] CorruptRawStringLength(byte[] payload)
    {
        var copy = payload.ToArray();
        BitConverter.GetBytes((copy.Length - 14) * 3 / 4).CopyTo(copy, 10);
        return copy;
    }

    private static byte[] CorruptFirstOperandTag(byte[] payload)
    {
        var copy = payload.ToArray();
        var entry = TableEntry(copy, 5, 0);
        copy[entry.Offset + 12] = 0xff;
        return copy;
    }

    private static byte[] CorruptTypeConsistency(byte[] payload)
    {
        var copy = payload.ToArray();
        var entry = FindTypeEntry(copy, CliTypeShape.SzArray);
        BitConverter.GetBytes(StringId(copy, "[Dependency]Fixture.Owner"))
            .CopyTo(copy, entry.Offset + sizeof(int));
        return copy;
    }

    private static byte[] CorruptFirstBlockExitTag(byte[] payload)
    {
        var copy = payload.ToArray();
        var marker = Find(copy, BitConverter.GetBytes(1000));
        copy[marker + 24] = 0xff;
        return copy;
    }

    private static byte[] CorruptFirstRegionTag(byte[] payload)
    {
        var copy = payload.ToArray();
        var marker = Find(copy,
        [
            0,
            .. BitConverter.GetBytes(0),
            .. BitConverter.GetBytes((int)StructuredBlockRole.ExecutingReplica),
            1,
            .. BitConverter.GetBytes(1),
        ]);
        copy[marker] = 0xff;
        return copy;
    }

    private static byte[] CorruptFirstExceptionPartTag(byte[] payload)
    {
        var copy = payload.ToArray();
        var marker = Find(copy, BitConverter.GetBytes(9001));
        copy[marker + 12] = 0xff;
        return copy;
    }

    private static byte[] RemoveFirstNamedAssembly(byte[] payload)
    {
        var copy = payload.ToArray();
        var entry = FindTypeEntry(copy, CliTypeShape.Named);
        var assemblyFlag = TypeAssemblyFlag(copy, entry);
        Assert.Equal(1, copy[assemblyFlag]);
        copy[assemblyFlag] = 0;
        return copy;
    }

    private static byte[] MakePrimitiveRequireMissingElement(byte[] payload)
    {
        var copy = payload.ToArray();
        var entry = FindTypeEntry(copy, CliTypeShape.Primitive);
        BitConverter.GetBytes((int)CliTypeShape.SzArray).CopyTo(copy, entry.Offset);
        return copy;
    }

    private static byte[] CorruptFirstStackKind(byte[] payload)
    {
        var copy = payload.ToArray();
        var entry = FindTypeEntry(copy, CliTypeShape.Primitive);
        Array.Fill(copy, (byte)0xff, entry.Offset + 2 * sizeof(int), sizeof(int));
        return copy;
    }

    private static byte[] MakeTypeReferenceItself(byte[] payload)
    {
        var copy = payload.ToArray();
        var entry = FindTypeEntry(copy, CliTypeShape.SzArray);
        var elementFlag = entry.Offset + 3 * sizeof(int) + sizeof(byte);
        Assert.Equal(1, copy[elementFlag]);
        BitConverter.GetBytes(entry.Index).CopyTo(copy, elementFlag + sizeof(byte));
        return copy;
    }

    private static byte[] CorruptFirstRootMethodReference(byte[] payload)
    {
        var copy = payload.ToArray();
        var root = RootOffset(copy);
        BitConverter.GetBytes(int.MaxValue).CopyTo(copy, root);
        return copy;
    }

    private static byte[] CorruptPrimitiveName(byte[] payload)
    {
        var copy = payload.ToArray();
        var entry = FindTypeEntry(copy, CliTypeShape.Primitive);
        BitConverter.GetBytes(StringId(copy, "[Dependency]Fixture.Owner"))
            .CopyTo(copy, entry.Offset + sizeof(int));
        return copy;
    }

    private static byte[] AddTrailingTypeEntryData(byte[] payload)
    {
        var entry = TableEntry(payload, 0, 0);
        var bytes = payload.ToList();
        bytes.Insert(entry.Offset + entry.Length, 0);
        var encodedLength = BitConverter.GetBytes(entry.Length + 1);
        for (var index = 0; index < encodedLength.Length; index++)
        {
            bytes[entry.Offset - sizeof(int) + index] = encodedLength[index];
        }
        return [.. bytes];
    }

    private static byte[] CorruptRootCollectionLength(byte[] payload, int value)
    {
        var copy = payload.ToArray();
        BitConverter.GetBytes(value).CopyTo(copy, RootOffset(copy) + 12);
        return copy;
    }

    private static byte[] CorruptRootBoolean(byte[] payload)
    {
        var copy = payload.ToArray();
        copy[RootOffset(copy) + 92] = 2;
        return copy;
    }

    private static byte[] CorruptRootEnum(byte[] payload)
    {
        var copy = payload.ToArray();
        Array.Fill(copy, (byte)0xff, RootOffset(copy) + 16, sizeof(int));
        return copy;
    }

    private static (int Offset, int Length, int Index) FindTypeEntry(
        byte[] payload,
        CliTypeShape shape)
    {
        var count = TableCount(payload, 0);
        for (var index = 0; index < count; index++)
        {
            var entry = TableEntry(payload, 0, index);
            if (BitConverter.ToInt32(payload, entry.Offset) == (int)shape)
            {
                return (entry.Offset, entry.Length, index);
            }
        }
        throw new InvalidOperationException("The codec fixture type was not found.");
    }

    private static int TypeAssemblyFlag(
        byte[] payload,
        (int Offset, int Length, int Index) entry)
    {
        var offset = entry.Offset + 3 * sizeof(int) + sizeof(byte);
        if (payload[offset++] != 0)
        {
            offset += sizeof(int);
        }
        var argumentCount = BitConverter.ToInt32(payload, offset);
        offset += sizeof(int) + argumentCount * sizeof(int);
        offset += 2 * sizeof(int);
        return offset;
    }

    private static int StringId(byte[] payload, string expected)
    {
        var offset = 6;
        var count = BitConverter.ToInt32(payload, offset);
        offset += sizeof(int);
        for (var index = 0; index < count; index++)
        {
            var length = BitConverter.ToInt32(payload, offset);
            offset += sizeof(int);
            var value = new string(Enumerable.Range(0, length)
                .Select(index => (char)BitConverter.ToUInt16(
                    payload,
                    offset + index * sizeof(ushort)))
                .ToArray());
            if (value == expected)
            {
                return index;
            }
            offset += length * sizeof(ushort);
        }
        throw new InvalidOperationException("The codec fixture string was not found.");
    }

    private static int TableCount(byte[] payload, int table)
    {
        var offset = FirstTableOffset(payload);
        for (var current = 0; current < table; current++)
        {
            var count = BitConverter.ToInt32(payload, offset);
            offset += sizeof(int);
            for (var index = 0; index < count; index++)
            {
                var length = BitConverter.ToInt32(payload, offset);
                offset += sizeof(int) + length;
            }
        }
        return BitConverter.ToInt32(payload, offset);
    }

    private static (int Offset, int Length) TableEntry(
        byte[] payload,
        int table,
        int requestedIndex)
    {
        var offset = FirstTableOffset(payload);
        for (var current = 0; current <= table; current++)
        {
            var count = BitConverter.ToInt32(payload, offset);
            offset += sizeof(int);
            for (var index = 0; index < count; index++)
            {
                var length = BitConverter.ToInt32(payload, offset);
                offset += sizeof(int);
                if (current == table && index == requestedIndex)
                {
                    return (offset, length);
                }
                offset += length;
            }
        }
        throw new InvalidOperationException("The codec fixture table entry was not found.");
    }

    private static int RootOffset(byte[] payload)
    {
        var offset = FirstTableOffset(payload);
        for (var table = 0; table < 6; table++)
        {
            var count = BitConverter.ToInt32(payload, offset);
            offset += sizeof(int);
            for (var index = 0; index < count; index++)
            {
                var length = BitConverter.ToInt32(payload, offset);
                offset += sizeof(int) + length;
            }
        }
        return offset;
    }

    private static int FirstTableOffset(byte[] payload)
    {
        var offset = 6;
        var count = BitConverter.ToInt32(payload, offset);
        offset += sizeof(int);
        for (var index = 0; index < count; index++)
        {
            var length = BitConverter.ToInt32(payload, offset);
            offset += sizeof(int) + length * sizeof(ushort);
        }
        return offset;
    }

    private static int Find(byte[] payload, byte[] marker)
    {
        for (var index = 0; index <= payload.Length - marker.Length; index++)
        {
            if (payload.AsSpan(index, marker.Length).SequenceEqual(marker))
            {
                return index;
            }
        }

        throw new InvalidOperationException("The codec fixture marker was not found.");
    }

    private static EntityKey Key(int token) => new(Assembly, token);
    private static StructuredBlockId Block(int value) => new(value);
    private static StructuredExceptionGroupId Group(int value) => new(value);
    private static StructuredContinuationId Continuation(int value) => new(value);

    private sealed record UnsupportedExit : StructuredBlockExit;
    private sealed record UnsupportedRegion : StructuredRegion;
    private sealed record UnsupportedPart : StructuredExceptionPart;
}
