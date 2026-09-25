using NetWasm.Compiler.Analysis;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Calls;
using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;

namespace NetWasm.Compiler.Caching.Frontend;

internal sealed class FrontendArtifactDecoder : IFrontendArtifactDecoder
{
    private const uint Magic = 0x3146434E;
    private const ushort SchemaVersion = 4;

    public FrontendArtifactSnapshot Decode(ImmutableArray<byte> payload)
    {
        if (payload.IsDefault)
        {
            throw new ArgumentException(
                "The frontend artifact payload must be initialized.",
                nameof(payload));
        }

        try
        {
            using var stream = new MemoryStream(payload.ToArray(), writable: false);
            using var reader = new BinaryReader(
                stream,
                new UTF8Encoding(false, true),
                leaveOpen: true);
            if (reader.ReadUInt32() != Magic)
            {
                throw Invalid("The frontend artifact magic is invalid.");
            }

            var version = reader.ReadUInt16();
            if (version != SchemaVersion)
            {
                throw Invalid($"Frontend artifact schema version {version} is unsupported.");
            }

            var tables = ReadTables.Read(reader, stream);
            var session = new ReadSession(reader, stream, tables);
            var result = session.ReadSnapshot();
            if (stream.Position != stream.Length)
            {
                throw Invalid("The frontend artifact contains trailing data.");
            }

            return result;
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is
            EndOfStreamException or
            DecoderFallbackException or
            ArgumentException or
            OverflowException)
        {
            throw Invalid("The frontend artifact payload is malformed.", exception);
        }
    }

    private static InvalidDataException Invalid(string message, Exception? inner = null) =>
        new(message, inner);

    private sealed class ReadTables
    {
        private readonly List<string> _strings = [];
        private readonly List<CliTypeIdentity> _types = [];
        private readonly List<MethodSignatureModel> _signatures = [];
        private readonly List<MethodDefinitionModel> _methodDefinitions = [];
        private readonly List<MethodInstanceModel> _methodInstances = [];
        private readonly List<FieldInstanceModel> _fieldInstances = [];
        private readonly List<CilInstruction> _instructions = [];

        internal static ReadTables Read(BinaryReader reader, Stream stream)
        {
            var tables = new ReadTables();
            var stringCount = ReadCount(reader, stream);
            for (var index = 0; index < stringCount; index++)
            {
                var length = ReadCount(reader, stream);
                if (length > (stream.Length - stream.Position) / sizeof(ushort))
                {
                    throw Invalid("The frontend artifact contains an invalid string length.");
                }
                var characters = new char[length];
                for (var characterIndex = 0;
                     characterIndex < characters.Length;
                     characterIndex++)
                {
                    characters[characterIndex] = (char)reader.ReadUInt16();
                }
                tables._strings.Add(new string(characters));
            }

            tables.ReadTable(reader, stream, tables._types, entry => entry.ReadType());
            tables.ReadTable(reader, stream, tables._signatures, entry => entry.ReadSignature());
            tables.ReadTable(
                reader,
                stream,
                tables._methodDefinitions,
                entry => entry.ReadMethodDefinition());
            tables.ReadTable(
                reader,
                stream,
                tables._methodInstances,
                entry => entry.ReadMethodInstance());
            tables.ReadTable(
                reader,
                stream,
                tables._fieldInstances,
                entry => entry.ReadFieldInstance());
            tables.ReadTable(
                reader,
                stream,
                tables._instructions,
                entry => entry.ReadInstruction());
            return tables;
        }

        internal string String(int id) => Resolve(_strings, id, "string");

        internal CliTypeIdentity Type(int id) => Resolve(_types, id, "type");

        internal MethodSignatureModel Signature(int id) =>
            Resolve(_signatures, id, "method-signature");

        internal MethodDefinitionModel MethodDefinition(int id) =>
            Resolve(_methodDefinitions, id, "method-definition");

        internal MethodInstanceModel MethodInstance(int id) =>
            Resolve(_methodInstances, id, "method-instance");

        internal FieldInstanceModel FieldInstance(int id) =>
            Resolve(_fieldInstances, id, "field-instance");

        internal CilInstruction Instruction(int id) =>
            Resolve(_instructions, id, "instruction");

        private void ReadTable<T>(
            BinaryReader reader,
            Stream stream,
            List<T> values,
            Func<EntryReader, T> read)
        {
            var count = ReadCount(reader, stream);
            for (var index = 0; index < count; index++)
            {
                var length = ReadCount(reader, stream);
                var bytes = ReadExactly(reader, length);
                using var entryStream = new MemoryStream(bytes, writable: false);
                using var entryReader = new BinaryReader(
                    entryStream,
                    new UTF8Encoding(false, true),
                    leaveOpen: true);
                var value = read(new EntryReader(entryReader, entryStream, this));
                if (entryStream.Position != entryStream.Length)
                {
                    throw Invalid("A frontend artifact table entry contains trailing data.");
                }
                values.Add(value);
            }
        }

        private static T Resolve<T>(IReadOnlyList<T> values, int id, string kind)
        {
            if ((uint)id >= (uint)values.Count)
            {
                throw Invalid($"The frontend artifact contains an invalid {kind} table reference.");
            }
            return values[id];
        }

        private static int ReadCount(BinaryReader reader, Stream stream)
        {
            var count = reader.ReadInt32();
            if (count < 0 || count > stream.Length - stream.Position)
            {
                throw Invalid("The frontend artifact contains an invalid collection length.");
            }
            return count;
        }

        private static byte[] ReadExactly(BinaryReader reader, int length) =>
            reader.ReadBytes(length);

        private sealed class EntryReader(
            BinaryReader reader,
            Stream stream,
            ReadTables tables)
        {
            internal CliTypeIdentity ReadType()
            {
                var shape = (CliTypeShape)reader.ReadInt32();
                var canonicalName = ReadString();
                var stackKind = ReadEnum<CliValueKind>();
                var isValueType = ReadBoolean();
                var elementType = ReadNullableClass(() => tables.Type(reader.ReadInt32()));
                var typeArguments = ReadArray(() => tables.Type(reader.ReadInt32()));
                var genericParameterIndex = reader.ReadInt32();
                var arrayRank = reader.ReadInt32();
                var assembly = ReadNullableStruct(() => new AssemblyIdentity(ReadString()));
                var fullName = ReadNullableClass(ReadString);
                var storageType = ReadNullableClass(() => tables.Type(reader.ReadInt32()));

                var result = shape switch
                {
                    CliTypeShape.Primitive => CliTypeIdentity.Primitive(
                        PrimitiveName(canonicalName), stackKind, isValueType),
                    CliTypeShape.Named => Named(assembly, fullName, isValueType, stackKind),
                    CliTypeShape.SzArray => CliTypeIdentity.SzArray(Required(elementType, shape)),
                    CliTypeShape.Array => CliTypeIdentity.Array(
                        Required(elementType, shape), arrayRank),
                    CliTypeShape.GenericInstantiation => CliTypeIdentity.GenericInstantiation(
                        Required(elementType, shape), typeArguments),
                    CliTypeShape.GenericTypeParameter => CliTypeIdentity.GenericParameter(
                        method: false, genericParameterIndex),
                    CliTypeShape.GenericMethodParameter => CliTypeIdentity.GenericParameter(
                        method: true, genericParameterIndex),
                    CliTypeShape.ManagedByReference => CliTypeIdentity.ManagedByReference(
                        Required(elementType, shape)),
                    CliTypeShape.UnmanagedPointer => CliTypeIdentity.UnmanagedPointer(
                        Required(elementType, shape)),
                    _ => throw Invalid($"CLI type shape {shape} is unsupported."),
                };
                if (storageType is not null)
                {
                    result = result.WithStackStorageType(storageType);
                }
                else if (result.StackKind != stackKind)
                {
                    result = result.WithStackKind(stackKind);
                }

                if (!StringComparer.Ordinal.Equals(result.CanonicalName, canonicalName))
                {
                    throw Invalid("The encoded CLI type identity is inconsistent.");
                }
                return result;
            }

            internal MethodSignatureModel ReadSignature() => new(
                tables.Type(reader.ReadInt32()),
                ReadArray(() => tables.Type(reader.ReadInt32())));

            internal MethodDefinitionModel ReadMethodDefinition()
            {
                var key = ReadEntityKey();
                var declaringType = ReadEntityKey();
                var name = ReadString();
                var isStatic = ReadBoolean();
                var signature = tables.Signature(reader.ReadInt32());
                var relativeVirtualAddress = reader.ReadInt32();
                var genericArity = reader.ReadInt32();
                var isVirtual = ReadBoolean();
                var isNewSlot = ReadBoolean();
                var isFinal = ReadBoolean();
                var isAbstract = ReadBoolean();
                var jsImport = ReadNullableClass(() =>
                    new InteropImportDeclaration(ReadString(), ReadNullableString())
                    {
                        IsPromise = ReadBoolean(),
                    });
                var jsExport = ReadNullableClass(
                    () => new InteropExportDeclaration(ReadNullableString()));
                var witImport = ReadNullableClass(
                    () => new WitImportDeclaration(ReadString(), ReadString()));
                var witExport = ReadNullableClass(
                    () => new WitExportDeclaration(ReadString(), ReadString()));
                var witPostReturn = ReadNullableClass(
                    () => new WitPostReturnDeclaration(ReadString(), ReadString()));
                return new(key, declaringType, name, isStatic, signature, relativeVirtualAddress)
                {
                    GenericArity = genericArity,
                    IsVirtual = isVirtual,
                    IsNewSlot = isNewSlot,
                    IsFinal = isFinal,
                    IsAbstract = isAbstract,
                    JSImport = jsImport,
                    JSExport = jsExport,
                    WitImport = witImport,
                    WitExport = witExport,
                    WitPostReturn = witPostReturn,
                };
            }

            internal MethodInstanceModel ReadMethodInstance() => new(
                tables.MethodDefinition(reader.ReadInt32()),
                tables.Type(reader.ReadInt32()),
                ReadArray(() => tables.Type(reader.ReadInt32())),
                tables.Signature(reader.ReadInt32()));

            internal FieldInstanceModel ReadFieldInstance()
            {
                var key = ReadEntityKey();
                var declaringTypeKey = ReadEntityKey();
                var name = ReadString();
                var isStatic = ReadBoolean();
                var initialData = ReadBytes();
                var literalValue = ReadNullableStruct(reader.ReadUInt64);
                var explicitOffset = ReadNullableStruct(reader.ReadInt32);
                var signatureType = tables.Type(reader.ReadInt32());
                var definition = new FieldDefinitionModel(
                    key, declaringTypeKey, name, signatureType, isStatic)
                {
                    InitialData = initialData,
                    LiteralValue = literalValue,
                    ExplicitOffset = explicitOffset,
                };
                return new(
                    definition,
                    tables.Type(reader.ReadInt32()),
                    tables.Type(reader.ReadInt32()));
            }

            internal CilInstruction ReadInstruction()
            {
                var offset = reader.ReadInt32();
                var nextOffset = reader.ReadInt32();
                var operation = ReadEnum<CilOperation>();
                CilOperand operand = reader.ReadByte() switch
                {
                    0 => new CilOperand.None(),
                    1 => new CilOperand.ConstantI4(reader.ReadInt32()),
                    2 => new CilOperand.ConstantI8(reader.ReadInt64()),
                    3 => new CilOperand.ConstantF4(reader.ReadSingle()),
                    4 => new CilOperand.ConstantF8(reader.ReadDouble()),
                    5 => new CilOperand.Index(reader.ReadInt32()),
                    6 => new CilOperand.BranchTarget(reader.ReadInt32()),
                    7 => new CilOperand.SwitchTargets(ReadArray(reader.ReadInt32)),
                    8 => new CilOperand.Entity(ReadEntityKey()),
                    9 => new CilOperand.MethodInstance(
                        tables.MethodInstance(reader.ReadInt32())),
                    10 => new CilOperand.FieldInstance(
                        tables.FieldInstance(reader.ReadInt32())),
                    11 => new CilOperand.TypeIdentity(tables.Type(reader.ReadInt32())),
                    12 => new CilOperand.CallSite(tables.Signature(reader.ReadInt32())),
                    13 => new CilOperand.NumericConversion(
                        reader.ReadInt32(), ReadBoolean(), ReadBoolean(),
                        ReadBoolean(), ReadBoolean()),
                    14 => new CilOperand.UserString(ReadString()),
                    15 => new CilOperand.ByteData(ReadBytes()),
                    var tag => throw Invalid($"CIL operand tag {tag} is unsupported."),
                };
                return new(offset, nextOffset, operation, operand);
            }

            private EntityKey ReadEntityKey() =>
                new(new AssemblyIdentity(ReadString()), reader.ReadInt32());

            private string ReadString() => tables.String(reader.ReadInt32());

            private string? ReadNullableString() => ReadNullableClass(ReadString);

            private ImmutableArray<byte> ReadBytes()
            {
                var count = ReadCount(allowDefault: true);
                return count == -1 ? default : [.. reader.ReadBytes(count)];
            }

            private ImmutableArray<T> ReadArray<T>(Func<T> read)
            {
                var count = ReadCount(allowDefault: true);
                if (count == -1)
                {
                    return default;
                }
                var values = ImmutableArray.CreateBuilder<T>(count);
                for (var index = 0; index < count; index++)
                {
                    values.Add(read());
                }
                return values.MoveToImmutable();
            }

            private int ReadCount(bool allowDefault = false)
            {
                var count = reader.ReadInt32();
                if ((allowDefault && count == -1) || count >= 0 && count <= Remaining)
                {
                    return count;
                }
                throw Invalid("The frontend artifact contains an invalid collection length.");
            }

            private bool ReadBoolean() => reader.ReadByte() switch
            {
                0 => false,
                1 => true,
                _ => throw Invalid("The frontend artifact contains an invalid Boolean value."),
            };

            private T? ReadNullableClass<T>(Func<T> read)
                where T : class => ReadBoolean() ? read() : null;

            private T? ReadNullableStruct<T>(Func<T> read)
                where T : struct => ReadBoolean() ? read() : null;

            private T ReadEnum<T>() where T : struct, Enum
            {
                var value = reader.ReadInt32();
                if (!Enum.IsDefined(typeof(T), value))
                {
                    throw Invalid(
                        $"The frontend artifact contains an invalid {typeof(T).Name} value.");
                }
                return (T)Enum.ToObject(typeof(T), value);
            }

            private static string PrimitiveName(string canonicalName)
            {
                const string prefix = "primitive:";
                if (!canonicalName.StartsWith(prefix, StringComparison.Ordinal))
                {
                    throw Invalid("The encoded primitive CLI type name is invalid.");
                }
                return canonicalName[prefix.Length..];
            }

            private static CliTypeIdentity Named(
                AssemblyIdentity? assembly,
                string? fullName,
                bool isValueType,
                CliValueKind stackKind)
            {
                if (assembly is not AssemblyIdentity presentAssembly || fullName is null)
                {
                    throw Invalid("The encoded named CLI type is incomplete.");
                }
                var separator = fullName.LastIndexOf('.');
                var @namespace = separator < 0 ? "" : fullName[..separator];
                var name = separator < 0 ? fullName : fullName[(separator + 1)..];
                return CliTypeIdentity.Named(
                    presentAssembly, @namespace, name, isValueType, stackKind);
            }

            private static CliTypeIdentity Required(
                CliTypeIdentity? elementType,
                CliTypeShape shape) =>
                elementType ?? throw Invalid(
                    $"CLI type shape {shape} requires an element type.");

            private long Remaining => stream.Length - stream.Position;
        }
    }

    private sealed class ReadSession(
        BinaryReader reader,
        Stream stream,
        ReadTables tables)
    {
        internal FrontendArtifactSnapshot ReadSnapshot() =>
            new(ReadAnalysis(), ReadStructuredMethod());

        private ReachableMethodAnalysisSnapshot ReadAnalysis() => new(
            ReadMethodInstance(),
            ReadMethodBody(),
            ReadStackMap(),
            ReadStackMap(),
            ReadArray(ReadEntityKey),
            ReadArray(() => new ReachabilityExceptionRequirement(
                ReadEnum<ManagedExceptionKind>(),
                ReadString())),
            ReadInstructionAnalysis());

        private ReachabilityInstructionAnalysis ReadInstructionAnalysis() => new(
            ReadArray(ReadType),
            ReadArray(ReadType),
            ReadArray(ReadType),
            ReadArray(ReadEntityKey),
            ReadArray(ReadString),
            ReadArray(() => new ReachabilityMethodReference(
                ReadEnum<CilOperation>(),
                ReadMethodInstance())),
            ReadArray(() => new ReachabilityEntityReference(
                ReadEnum<CilOperation>(),
                ReadEntityKey())),
            ReadArray(ReadFieldInstance),
            ReadArray(() =>
            {
                var key = ReadString();
                var declaration = new DispatchDeclaration(
                    ReadString(),
                    reader.ReadInt32(),
                    ReadMethodInstance(),
                    ReadEnum<CilOperation>());
                return new ReachabilityDispatch(key, declaration);
            }),
            ReadArray(ReadMethodInstance),
            ReadArray(ReadCallSite));

        private ManagedCallSite ReadCallSite()
        {
            var key = new ManagedCallSiteKey(
                new ManagedMethodIdentity(ReadString()),
                reader.ReadInt32());
            var operation = ReadEnum<ManagedCallOperation>();
            var identity = new ManagedMethodIdentity(ReadString());
            var target = ReadMethodInstance();
            var constrainedType = ReadNullableClass(ReadType);
            return new(key, operation, identity, target, constrainedType);
        }

        private CilMethodBody ReadMethodBody()
        {
            var method = ReadMethodDefinition();
            var maxStack = reader.ReadInt32();
            var locals = ReadArray(() => ReadEnum<CliValueKind>());
            var instructions = ReadArray(ReadInstruction);
            var methodInstance = ReadNullableClass(ReadMethodInstance);
            var localSignatureTypes = ReadArray(ReadType);
            var exceptionRegions = ReadArray(() => new CilExceptionRegion(
                ReadEnum<CilExceptionRegionKind>(),
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadInt32(),
                ReadNullableStruct(ReadEntityKey),
                ReadNullableStruct(reader.ReadInt32)));
            return new(method, maxStack, locals, instructions)
            {
                MethodInstance = methodInstance,
                LocalSignatureTypes = localSignatureTypes,
                ExceptionRegions = exceptionRegions,
            };
        }

        private CilInstruction ReadInstruction() =>
            tables.Instruction(reader.ReadInt32());

        private StructuredMethodConstruction ReadStructuredMethod()
        {
            var header = new StructuredMethodHeader(
                ReadMethodDefinition(),
                ReadNullableClass(ReadMethodInstance),
                reader.ReadInt32(),
                ReadArray(() => ReadEnum<CliValueKind>()),
                ReadArray(ReadType),
                ReadArray(ReadInstruction));
            var entry = new StructuredBlockId(reader.ReadInt32());
            var blockCount = ReadCount();
            var blocks = ImmutableDictionary.CreateBuilder<
                StructuredBlockId,
                StructuredBlockDefinition>();
            for (var index = 0; index < blockCount; index++)
            {
                var key = new StructuredBlockId(reader.ReadInt32());
                blocks.Add(key, ReadBlock());
            }

            var body = ReadSequence();
            var topLevel = ReadArray(() => new StructuredExceptionGroupId(reader.ReadInt32()));
            var groupCount = ReadCount();
            var groups = ImmutableDictionary.CreateBuilder<
                StructuredExceptionGroupId,
                StructuredExceptionGroup>();
            for (var index = 0; index < groupCount; index++)
            {
                var key = new StructuredExceptionGroupId(reader.ReadInt32());
                groups.Add(key, ReadExceptionGroup());
            }

            return new(
                header,
                entry,
                blocks.ToImmutable(),
                body,
                topLevel,
                groups.ToImmutable(),
                ReadStackMap());
        }

        private StructuredBlockDefinition ReadBlock()
        {
            var id = new StructuredBlockId(reader.ReadInt32());
            var startOffset = reader.ReadInt32();
            var endOffset = reader.ReadInt32();
            var instructions = ReadArray(ReadInstruction);
            var entryStack = ReadArray(() => ReadEnum<CliValueKind>());
            StructuredBlockExit exit = reader.ReadByte() switch
            {
                0 => new StructuredFallthroughExit(ReadNullableStruct(
                    () => new StructuredBlockId(reader.ReadInt32()))),
                1 => new StructuredBranchExit(
                    reader.ReadInt32(),
                    new StructuredBlockId(reader.ReadInt32())),
                2 => new StructuredConditionalExit(
                    ReadCondition(),
                    new StructuredBlockId(reader.ReadInt32()),
                    new StructuredBlockId(reader.ReadInt32())),
                3 => new StructuredLeaveExit(
                    reader.ReadInt32(),
                    new StructuredBlockId(reader.ReadInt32())),
                4 => new StructuredTerminalExit(ReadInstruction()),
                var tag => throw Invalid($"Structured block-exit tag {tag} is unsupported."),
            };
            return new(id, startOffset, instructions, entryStack, exit)
            {
                EndOffset = endOffset,
            };
        }

        private StructuredCondition ReadCondition() => new(
            reader.ReadInt32(),
            ReadEnum<CilOperation>(),
            reader.ReadInt32(),
            ReadEnum<CliValueKind>(),
            ReadNullableStruct(() => ReadEnum<CliValueKind>()));

        private StructuredSequence ReadSequence() => new(ReadArray(ReadRegion));

        private StructuredRegion ReadRegion() => reader.ReadByte() switch
        {
            0 => new StructuredCode(ReadOccurrence()),
            1 => new StructuredLoopBreak(),
            2 => new StructuredLoopContinue(),
            3 => new StructuredDispatcherContinue(
                new StructuredBlockId(reader.ReadInt32())),
            4 => ReadExceptionRegion(),
            5 => ReadDispatcher(),
            6 => new StructuredIf(ReadOccurrence(), ReadSequence(), ReadSequence()),
            7 => new StructuredLoop(
                ReadOccurrence(),
                ReadBoolean(),
                ReadSequence(),
                ReadSequence(),
                ReadSequence()),
            8 => new StructuredPostTestLoop(
                ReadSequence(),
                ReadOccurrence(),
                ReadBoolean(),
                ReadSequence(),
                ReadSequence()),
            var tag => throw Invalid($"Structured region tag {tag} is unsupported."),
        };

        private StructuredExceptionRegion ReadExceptionRegion()
        {
            var region = new StructuredExceptionRegion(
                new StructuredExceptionGroupId(reader.ReadInt32()),
                ReadNullableStruct(() => new StructuredContinuationId(reader.ReadInt32())));
            var count = ReadCount();
            var continuations = ImmutableHashSet.CreateBuilder<StructuredContinuationId>();
            for (var index = 0; index < count; index++)
            {
                continuations.Add(new StructuredContinuationId(reader.ReadInt32()));
            }

            return region with { DispatcherContinuations = continuations.ToImmutable() };
        }

        private StructuredDispatcher ReadDispatcher() => new(
            ReadNullableStruct(() => new StructuredBlockId(reader.ReadInt32())),
            ReadArray(() => new StructuredDispatcherBlock(
                ReadOccurrence(),
                ReadNullableStruct(() => new StructuredBlockId(reader.ReadInt32())),
                ReadNullableStruct(() => new StructuredBlockId(reader.ReadInt32())))),
            ReadArray(() => new StructuredDispatcherExit(
                new StructuredBlockId(reader.ReadInt32()),
                ReadSequence())));

        private StructuredBlockOccurrence ReadOccurrence() => new(
            new StructuredBlockId(reader.ReadInt32()),
            ReadEnum<StructuredBlockRole>(),
            ReadNullableStruct(() => new StructuredContinuationId(reader.ReadInt32())));

        private StructuredExceptionGroup ReadExceptionGroup()
        {
            var id = new StructuredExceptionGroupId(reader.ReadInt32());
            var parent = ReadNullableStruct(
                () => new StructuredExceptionGroupId(reader.ReadInt32()));
            var tryOffset = reader.ReadInt32();
            var tryLength = reader.ReadInt32();
            var protectedParts = ReadArray(ReadExceptionPart);
            var clauses = ReadArray(() =>
            {
                var kind = ReadEnum<CilExceptionRegionKind>();
                var handlerOffset = reader.ReadInt32();
                var handlerLength = reader.ReadInt32();
                var catchType = ReadNullableStruct(ReadEntityKey);
                var filterOffset = ReadNullableStruct(reader.ReadInt32);
                var handlerBody = ReadSequence();
                var filterBody = ReadNullableClass(ReadSequence);
                var handlerBlock = new StructuredBlockId(reader.ReadInt32());
                var filterBlock = ReadNullableStruct(
                    () => new StructuredBlockId(reader.ReadInt32()));
                return new StructuredExceptionClause(
                    kind,
                    handlerOffset,
                    handlerLength,
                    catchType,
                    filterOffset,
                    handlerBody,
                    filterBody)
                {
                    HandlerBlock = handlerBlock,
                    FilterBlock = filterBlock,
                };
            });
            var continuations = ReadArray(() => new StructuredExceptionContinuation(
                new StructuredContinuationId(reader.ReadInt32()),
                reader.ReadInt32(),
                new StructuredBlockId(reader.ReadInt32()),
                ReadSequence()));
            var dispatcher = ReadNullableClass(ReadDispatcher);
            var join = ReadNullableStruct(() => new StructuredBlockId(reader.ReadInt32()));
            var protectedBlocks = ReadArray(
                () => new StructuredBlockId(reader.ReadInt32()));
            return new(
                id,
                parent,
                tryOffset,
                tryLength,
                protectedParts,
                clauses,
                continuations,
                dispatcher,
                join)
            {
                ProtectedBlocks = protectedBlocks,
            };
        }

        private StructuredExceptionPart ReadExceptionPart() => reader.ReadByte() switch
        {
            0 => new StructuredExceptionCode(ReadSequence()),
            1 => new StructuredNestedExceptionGroup(
                new StructuredExceptionGroupId(reader.ReadInt32())),
            var tag => throw Invalid(
                $"Structured exception-part tag {tag} is unsupported."),
        };

        private MethodInstanceModel ReadMethodInstance() =>
            tables.MethodInstance(reader.ReadInt32());

        private MethodDefinitionModel ReadMethodDefinition() =>
            tables.MethodDefinition(reader.ReadInt32());

        private FieldInstanceModel ReadFieldInstance() =>
            tables.FieldInstance(reader.ReadInt32());

        private CliTypeIdentity ReadType() => tables.Type(reader.ReadInt32());

        private ImmutableDictionary<int, ImmutableArray<CliValueKind>> ReadStackMap()
        {
            var count = ReadCount();
            var values = ImmutableDictionary.CreateBuilder<
                int,
                ImmutableArray<CliValueKind>>();
            for (var index = 0; index < count; index++)
            {
                values.Add(
                    reader.ReadInt32(),
                    ReadArray(() => ReadEnum<CliValueKind>()));
            }

            return values.ToImmutable();
        }

        private EntityKey ReadEntityKey() => new(ReadAssembly(), reader.ReadInt32());

        private AssemblyIdentity ReadAssembly() => new(ReadString());

        private string ReadString() => tables.String(reader.ReadInt32());

        private ImmutableArray<T> ReadArray<T>(Func<T> read)
        {
            var count = ReadCount(allowDefault: true);
            if (count == -1)
            {
                return default;
            }

            var values = ImmutableArray.CreateBuilder<T>(count);
            for (var index = 0; index < count; index++)
            {
                values.Add(read());
            }

            return values.MoveToImmutable();
        }

        private int ReadCount(bool allowDefault = false)
        {
            var count = reader.ReadInt32();
            if ((allowDefault && count == -1) || count >= 0 && count <= Remaining)
            {
                return count;
            }

            throw Invalid("The frontend artifact contains an invalid collection length.");
        }

        private bool ReadBoolean() => reader.ReadByte() switch
        {
            0 => false,
            1 => true,
            _ => throw Invalid("The frontend artifact contains an invalid Boolean value."),
        };

        private T? ReadNullableClass<T>(Func<T> read)
            where T : class => ReadBoolean() ? read() : null;

        private T? ReadNullableStruct<T>(Func<T> read)
            where T : struct => ReadBoolean() ? read() : null;

        private T ReadEnum<T>()
            where T : struct, Enum
        {
            var value = reader.ReadInt32();
            if (!Enum.IsDefined(typeof(T), value))
            {
                throw Invalid($"The frontend artifact contains an invalid {typeof(T).Name} value.");
            }

            return (T)Enum.ToObject(typeof(T), value);
        }

        private long Remaining => stream.Length - stream.Position;

    }
}
