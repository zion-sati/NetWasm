using NetWasm.Compiler.Analysis;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Calls;

namespace NetWasm.Compiler.Caching.Frontend;

internal sealed class FrontendArtifactEncoder : IFrontendArtifactEncoder
{
    private const uint Magic = 0x3146434E;
    private const ushort SchemaVersion = 3;

    public ImmutableArray<byte> Encode(FrontendArtifactSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(snapshot.Analysis);
        ArgumentNullException.ThrowIfNull(snapshot.StructuredMethod);

        var tables = new WriteTables();
        using var rootStream = new MemoryStream();
        using (var collectionWriter = new BinaryWriter(
                   rootStream,
                   new UTF8Encoding(false, true),
                   leaveOpen: true))
        {
            new WriteSession(collectionWriter, tables).WriteSnapshot(snapshot);
        }

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, new UTF8Encoding(false, true), leaveOpen: true))
        {
            writer.Write(Magic);
            writer.Write(SchemaVersion);
            tables.Write(writer);
            writer.Write(rootStream.GetBuffer().AsSpan(0, checked((int)rootStream.Length)));
        }

        return [.. stream.GetBuffer().AsSpan(0, checked((int)stream.Length))];
    }

    private sealed class WriteTables
    {
        private readonly List<string> _strings = [];
        private readonly Dictionary<string, int> _stringIds = new(StringComparer.Ordinal);
        private readonly ValueTable _types = new();
        private readonly ValueTable _signatures = new();
        private readonly ValueTable _methodDefinitions = new();
        private readonly ValueTable _methodInstances = new();
        private readonly ValueTable _fieldInstances = new();
        private readonly ValueTable _instructions = new();
        internal int String(string value)
        {
            ArgumentNullException.ThrowIfNull(value);
            if (_stringIds.TryGetValue(value, out var existing))
            {
                return existing;
            }

            var id = _strings.Count;
            _strings.Add(value);
            _stringIds.Add(value, id);
            return id;
        }

        internal int Type(CliTypeIdentity value) => _types.Intern(Entry(writer =>
        {
            WriteEnum(writer, value.Shape);
            writer.Write(String(value.CanonicalName));
            WriteEnum(writer, value.StackKind);
            writer.Write(value.IsValueType);
            WriteNullableId(writer, value.ElementType, Type);
            WriteArray(writer, value.TypeArguments, Type);
            writer.Write(value.GenericParameterIndex);
            writer.Write(value.ArrayRank);
            writer.Write(value.Assembly.HasValue);
            if (value.Assembly is AssemblyIdentity assembly)
            {
                writer.Write(String(assembly.Name));
            }
            WriteNullableId(writer, value.FullName, String);
            WriteNullableId(writer, value.StackStorageType, Type);
        }));

        internal int Signature(MethodSignatureModel value) => _signatures.Intern(Entry(writer =>
        {
            writer.Write(Type(value.ReturnSignatureType));
            WriteArray(writer, value.ParameterSignatureTypes, Type);
        }));

        internal int MethodDefinition(MethodDefinitionModel value) =>
            _methodDefinitions.Intern(Entry(writer =>
            {
                WriteEntityKey(writer, value.Key);
                WriteEntityKey(writer, value.DeclaringType);
                writer.Write(String(value.Name));
                writer.Write(value.IsStatic);
                writer.Write(Signature(value.Signature));
                writer.Write(value.RelativeVirtualAddress);
                writer.Write(value.GenericArity);
                writer.Write(value.IsVirtual);
                writer.Write(value.IsNewSlot);
                writer.Write(value.IsFinal);
                writer.Write(value.IsAbstract);
                WriteNullable(writer, value.JSImport, item =>
                {
                    writer.Write(String(item.FunctionName));
                    WriteNullableId(writer, item.ModuleName, String);
                    writer.Write(item.IsPromise);
                });
                WriteNullable(writer, value.JSExport, item =>
                    WriteNullableId(writer, item.ExportName, String));
                WriteNullable(writer, value.WitImport, item =>
                {
                    writer.Write(String(item.InterfaceName));
                    writer.Write(String(item.FunctionName));
                });
                WriteNullable(writer, value.WitExport, item =>
                {
                    writer.Write(String(item.InterfaceName));
                    writer.Write(String(item.FunctionName));
                });
                WriteNullable(writer, value.WitPostReturn, item =>
                {
                    writer.Write(String(item.InterfaceName));
                    writer.Write(String(item.FunctionName));
                });
            }));

        internal int MethodInstance(MethodInstanceModel value) =>
            _methodInstances.Intern(Entry(writer =>
            {
                writer.Write(MethodDefinition(value.Definition));
                writer.Write(Type(value.DeclaringType));
                WriteArray(writer, value.MethodArguments, Type);
                writer.Write(Signature(value.Signature));
            }));

        internal int FieldInstance(FieldInstanceModel value) =>
            _fieldInstances.Intern(Entry(writer =>
            {
                WriteEntityKey(writer, value.Definition.Key);
                WriteEntityKey(writer, value.Definition.DeclaringType);
                writer.Write(String(value.Definition.Name));
                writer.Write(value.Definition.IsStatic);
                WriteBytes(writer, value.Definition.InitialData);
                writer.Write(value.Definition.LiteralValue.HasValue);
                if (value.Definition.LiteralValue is ulong literal)
                {
                    writer.Write(literal);
                }
                writer.Write(Type(value.Definition.SignatureType));
                writer.Write(Type(value.DeclaringType));
                writer.Write(Type(value.FieldType));
            }));

        internal int Instruction(CilInstruction value) => _instructions.Intern(Entry(writer =>
        {
            writer.Write(value.Offset);
            writer.Write(value.NextOffset);
            WriteEnum(writer, value.Operation);
            switch (value.Operand)
            {
                case CilOperand.None:
                    writer.Write((byte)0);
                    break;
                case CilOperand.ConstantI4 operand:
                    writer.Write((byte)1);
                    writer.Write(operand.Value);
                    break;
                case CilOperand.ConstantI8 operand:
                    writer.Write((byte)2);
                    writer.Write(operand.Value);
                    break;
                case CilOperand.ConstantF4 operand:
                    writer.Write((byte)3);
                    writer.Write(operand.Value);
                    break;
                case CilOperand.ConstantF8 operand:
                    writer.Write((byte)4);
                    writer.Write(operand.Value);
                    break;
                case CilOperand.Index operand:
                    writer.Write((byte)5);
                    writer.Write(operand.Value);
                    break;
                case CilOperand.BranchTarget operand:
                    writer.Write((byte)6);
                    writer.Write(operand.Offset);
                    break;
                case CilOperand.SwitchTargets operand:
                    writer.Write((byte)7);
                    WriteArray(writer, operand.Offsets, writer.Write);
                    break;
                case CilOperand.Entity operand:
                    writer.Write((byte)8);
                    WriteEntityKey(writer, operand.Key);
                    break;
                case CilOperand.MethodInstance operand:
                    writer.Write((byte)9);
                    writer.Write(MethodInstance(operand.Value));
                    break;
                case CilOperand.FieldInstance operand:
                    writer.Write((byte)10);
                    writer.Write(FieldInstance(operand.Value));
                    break;
                case CilOperand.TypeIdentity operand:
                    writer.Write((byte)11);
                    writer.Write(Type(operand.Value));
                    break;
                case CilOperand.CallSite operand:
                    writer.Write((byte)12);
                    writer.Write(Signature(operand.Signature));
                    break;
                case CilOperand.NumericConversion operand:
                    writer.Write((byte)13);
                    writer.Write(operand.BitWidth);
                    writer.Write(operand.DestinationUnsigned);
                    writer.Write(operand.Checked);
                    writer.Write(operand.SourceUnsigned);
                    writer.Write(operand.Native);
                    break;
                case CilOperand.UserString operand:
                    writer.Write((byte)14);
                    writer.Write(String(operand.Value));
                    break;
                case CilOperand.ByteData operand:
                    writer.Write((byte)15);
                    WriteBytes(writer, operand.Value);
                    break;
                default:
                    throw new InvalidDataException(
                        "The CIL operand is not a supported frontend artifact variant.");
            }
        }));

        internal void Write(BinaryWriter writer)
        {
            writer.Write(_strings.Count);
            foreach (var value in _strings)
            {
                WriteRawString(writer, value);
            }
            WriteTable(writer, _types);
            WriteTable(writer, _signatures);
            WriteTable(writer, _methodDefinitions);
            WriteTable(writer, _methodInstances);
            WriteTable(writer, _fieldInstances);
            WriteTable(writer, _instructions);
        }

        private void WriteEntityKey(BinaryWriter writer, EntityKey value)
        {
            writer.Write(String(value.Assembly.Name));
            writer.Write(value.MetadataToken);
        }

        private static ImmutableArray<byte> Entry(Action<BinaryWriter> write)
        {
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(
                       stream,
                       new UTF8Encoding(false, true),
                       leaveOpen: true))
            {
                write(writer);
            }
            return [.. stream.GetBuffer().AsSpan(0, checked((int)stream.Length))];
        }

        private static void WriteTable(BinaryWriter writer, ValueTable table)
        {
            writer.Write(table.Values.Count);
            foreach (var value in table.Values)
            {
                writer.Write(value.Length);
                writer.Write(value.AsSpan());
            }
        }

        private static void WriteRawString(BinaryWriter writer, string value)
        {
            writer.Write(value.Length);
            foreach (var character in value)
            {
                writer.Write((ushort)character);
            }
        }

        private static void WriteBytes(BinaryWriter writer, ImmutableArray<byte> values)
        {
            writer.Write(values.IsDefault ? -1 : values.Length);
            if (!values.IsDefaultOrEmpty)
            {
                writer.Write(values.AsSpan());
            }
        }

        private static void WriteArray<T>(
            BinaryWriter writer,
            ImmutableArray<T> values,
            Func<T, int> resolve)
        {
            writer.Write(values.IsDefault ? -1 : values.Length);
            if (values.IsDefault)
            {
                return;
            }
            foreach (var value in values)
            {
                writer.Write(resolve(value));
            }
        }

        private static void WriteArray<T>(
            BinaryWriter writer,
            ImmutableArray<T> values,
            Action<T> write)
        {
            writer.Write(values.IsDefault ? -1 : values.Length);
            if (values.IsDefault)
            {
                return;
            }
            foreach (var value in values)
            {
                write(value);
            }
        }

        private static void WriteNullableId<T>(
            BinaryWriter writer,
            T? value,
            Func<T, int> resolve)
            where T : class
        {
            writer.Write(value is not null);
            if (value is not null)
            {
                writer.Write(resolve(value));
            }
        }

        private static void WriteNullable<T>(
            BinaryWriter writer,
            T? value,
            Action<T> write)
            where T : class
        {
            writer.Write(value is not null);
            if (value is not null)
            {
                write(value);
            }
        }

        private static void WriteEnum<T>(BinaryWriter writer, T value)
            where T : struct, Enum =>
            writer.Write(Convert.ToInt32(value, CultureInfo.InvariantCulture));

        private sealed class ValueTable
        {
            private readonly Dictionary<ImmutableArray<byte>, int> _ids =
                new(ImmutableByteArrayComparer.Instance);

            internal List<ImmutableArray<byte>> Values { get; } = [];

            internal int Intern(ImmutableArray<byte> value)
            {
                if (_ids.TryGetValue(value, out var existing))
                {
                    return existing;
                }
                var id = Values.Count;
                Values.Add(value);
                _ids.Add(value, id);
                return id;
            }
        }

        private sealed class ImmutableByteArrayComparer : IEqualityComparer<ImmutableArray<byte>>
        {
            internal static ImmutableByteArrayComparer Instance { get; } = new();

            public bool Equals(ImmutableArray<byte> left, ImmutableArray<byte> right) =>
                left.AsSpan().SequenceEqual(right.AsSpan());

            public int GetHashCode(ImmutableArray<byte> value)
            {
                var hash = new HashCode();
                hash.AddBytes(value.AsSpan());
                return hash.ToHashCode();
            }
        }
    }

    private sealed class WriteSession(BinaryWriter writer, WriteTables tables)
    {
        internal void WriteSnapshot(FrontendArtifactSnapshot snapshot)
        {
            WriteAnalysis(snapshot.Analysis);
            WriteStructuredMethod(snapshot.StructuredMethod);
        }

        private void WriteAnalysis(ReachableMethodAnalysisSnapshot analysis)
        {
            WriteMethodInstance(analysis.Method);
            WriteMethodBody(analysis.Body);
            WriteStackMap(analysis.EntryStacks);
            WriteStackMap(analysis.InstructionEntryStacks);
            WriteArray(analysis.CatchTypes, WriteEntityKey);
            WriteArray(analysis.Exceptions, item =>
            {
                WriteEnum(item.Kind);
                WriteString(item.TypeName);
            });
            WriteInstructionAnalysis(analysis.Instructions);
        }

        private void WriteInstructionAnalysis(ReachabilityInstructionAnalysis analysis)
        {
            WriteArray(analysis.RuntimeTypes, WriteType);
            WriteArray(analysis.ConstructedTypes, WriteType);
            WriteArray(analysis.AllocatedTypes, WriteType);
            WriteArray(analysis.Types, WriteEntityKey);
            WriteArray(analysis.Strings, WriteString);
            WriteArray(analysis.Methods, item =>
            {
                WriteEnum(item.Operation);
                WriteMethodInstance(item.Method);
            });
            WriteArray(analysis.Entities, item =>
            {
                WriteEnum(item.Operation);
                WriteEntityKey(item.Entity);
            });
            WriteArray(analysis.Fields, WriteFieldInstance);
            WriteArray(analysis.Dispatches, item =>
            {
                WriteString(item.Key);
                WriteString(item.Declaration.Caller);
                writer.Write(item.Declaration.IlOffset);
                WriteMethodInstance(item.Declaration.Declaration);
                WriteEnum(item.Declaration.Operation);
            });
            WriteArray(analysis.CallableMethods, WriteMethodInstance);
            WriteArray(analysis.CallSites, WriteCallSite);
        }

        private void WriteCallSite(ManagedCallSite site)
        {
            WriteString(site.Key.Caller.CanonicalName);
            writer.Write(site.Key.InstructionOffset);
            WriteEnum(site.Operation);
            WriteString(site.TargetIdentity.CanonicalName);
            WriteMethodInstance(site.Target);
            WriteNullable(site.ConstrainedType, WriteType);
        }

        private void WriteMethodBody(CilMethodBody body)
        {
            WriteMethodDefinition(body.Method);
            writer.Write(body.MaxStack);
            WriteArray(body.Locals, WriteEnum);
            WriteArray(body.Instructions, WriteInstruction);
            WriteNullable(body.MethodInstance, WriteMethodInstance);
            WriteArray(body.LocalSignatureTypes, WriteType);
            WriteArray(body.ExceptionRegions, region =>
            {
                WriteEnum(region.Kind);
                writer.Write(region.TryOffset);
                writer.Write(region.TryLength);
                writer.Write(region.HandlerOffset);
                writer.Write(region.HandlerLength);
                WriteNullable(region.CatchType, WriteEntityKey);
                WriteNullable(region.FilterOffset, writer.Write);
            });
        }

        private void WriteInstruction(CilInstruction instruction) =>
            writer.Write(tables.Instruction(instruction));

        private void WriteStructuredMethod(StructuredMethodConstruction method)
        {
            WriteMethodDefinition(method.Header.Method);
            WriteNullable(method.Header.MethodInstance, WriteMethodInstance);
            writer.Write(method.Header.MaxStack);
            WriteArray(method.Header.Locals, WriteEnum);
            WriteArray(method.Header.LocalSignatureTypes, WriteType);
            WriteArray(method.Header.Instructions, WriteInstruction);
            writer.Write(method.EntryBlock.Value);
            WriteDictionary(
                method.Blocks.OrderBy(pair => pair.Key.Value),
                pair => writer.Write(pair.Key.Value),
                pair => WriteBlock(pair.Value));
            WriteSequence(method.Body);
            WriteArray(method.TopLevelExceptionGroups, item => writer.Write(item.Value));
            WriteDictionary(
                method.ExceptionGroups.OrderBy(pair => pair.Key.Value),
                pair => writer.Write(pair.Key.Value),
                pair => WriteExceptionGroup(pair.Value));
            WriteStackMap(method.InstructionEntryStacks);
        }

        private void WriteBlock(StructuredBlockDefinition block)
        {
            writer.Write(block.Id.Value);
            writer.Write(block.StartOffset);
            writer.Write(block.EndOffset);
            WriteArray(block.Instructions, WriteInstruction);
            WriteArray(block.EntryStack, WriteEnum);
            switch (block.Exit)
            {
                case StructuredFallthroughExit exit:
                    writer.Write((byte)0);
                    WriteNullable(exit.Target, item => writer.Write(item.Value));
                    break;
                case StructuredBranchExit exit:
                    writer.Write((byte)1);
                    writer.Write(exit.InstructionOffset);
                    writer.Write(exit.Target.Value);
                    break;
                case StructuredConditionalExit exit:
                    writer.Write((byte)2);
                    WriteCondition(exit.Condition);
                    writer.Write(exit.WhenTaken.Value);
                    writer.Write(exit.WhenNotTaken.Value);
                    break;
                case StructuredLeaveExit exit:
                    writer.Write((byte)3);
                    writer.Write(exit.InstructionOffset);
                    writer.Write(exit.Target.Value);
                    break;
                case StructuredTerminalExit exit:
                    writer.Write((byte)4);
                    WriteInstruction(exit.Instruction);
                    break;
                default:
                    throw new InvalidDataException(
                        $"Unsupported structured block exit type '{block.Exit.GetType().Name}'.");
            }
        }

        private void WriteCondition(StructuredCondition condition)
        {
            writer.Write(condition.InstructionOffset);
            WriteEnum(condition.Operation);
            writer.Write(condition.StackSlot);
            WriteEnum(condition.LeftKind);
            WriteNullable(condition.RightKind, WriteEnum);
        }

        private void WriteSequence(StructuredSequence sequence) =>
            WriteArray(sequence.Regions, WriteRegion);

        private void WriteRegion(StructuredRegion region)
        {
            switch (region)
            {
                case StructuredCode code:
                    writer.Write((byte)0);
                    WriteOccurrence(code.Occurrence);
                    break;
                case StructuredLoopBreak:
                    writer.Write((byte)1);
                    break;
                case StructuredLoopContinue:
                    writer.Write((byte)2);
                    break;
                case StructuredDispatcherContinue item:
                    writer.Write((byte)3);
                    writer.Write(item.Target.Value);
                    break;
                case StructuredExceptionRegion item:
                    writer.Write((byte)4);
                    writer.Write(item.Group.Value);
                    WriteNullable(item.FallthroughContinuation, value => writer.Write(value.Value));
                    WriteContinuationSet(item.DispatcherContinuations);
                    break;
                case StructuredDispatcher item:
                    writer.Write((byte)5);
                    WriteDispatcher(item);
                    break;
                case StructuredIf item:
                    writer.Write((byte)6);
                    WriteOccurrence(item.Condition);
                    WriteSequence(item.WhenTrue);
                    WriteSequence(item.WhenFalse);
                    break;
                case StructuredLoop item:
                    writer.Write((byte)7);
                    WriteOccurrence(item.Condition);
                    writer.Write(item.ContinueWhenConditionTrue);
                    WriteSequence(item.Body);
                    WriteSequence(item.ContinueBody);
                    WriteSequence(item.ExitBody);
                    break;
                case StructuredPostTestLoop item:
                    writer.Write((byte)8);
                    WriteSequence(item.Body);
                    WriteOccurrence(item.Condition);
                    writer.Write(item.ContinueWhenConditionTrue);
                    WriteSequence(item.ContinueBody);
                    WriteSequence(item.ExitBody);
                    break;
                default:
                    throw new InvalidDataException(
                        $"Unsupported structured region type '{region.GetType().Name}'.");
            }
        }

        private void WriteDispatcher(StructuredDispatcher dispatcher)
        {
            WriteNullable(dispatcher.EntryBlock, value => writer.Write(value.Value));
            WriteArray(dispatcher.Blocks, block =>
            {
                WriteOccurrence(block.Occurrence);
                WriteNullable(block.WhenTrue, value => writer.Write(value.Value));
                WriteNullable(block.WhenFalse, value => writer.Write(value.Value));
            });
            WriteArray(dispatcher.Exits, exit =>
            {
                writer.Write(exit.Target.Value);
                WriteSequence(exit.Body);
            });
        }

        private void WriteOccurrence(StructuredBlockOccurrence occurrence)
        {
            writer.Write(occurrence.Block.Value);
            WriteEnum(occurrence.Role);
            WriteNullable(occurrence.LeaveContinuation, value => writer.Write(value.Value));
        }

        private void WriteExceptionGroup(StructuredExceptionGroup group)
        {
            writer.Write(group.Id.Value);
            WriteNullable(group.Parent, value => writer.Write(value.Value));
            writer.Write(group.TryOffset);
            writer.Write(group.TryLength);
            WriteArray(group.ProtectedParts, WriteExceptionPart);
            WriteArray(group.Clauses, clause =>
            {
                WriteEnum(clause.Kind);
                writer.Write(clause.HandlerOffset);
                writer.Write(clause.HandlerLength);
                WriteNullable(clause.CatchType, WriteEntityKey);
                WriteNullable(clause.FilterOffset, writer.Write);
                WriteSequence(clause.HandlerBody);
                WriteNullable(clause.FilterBody, WriteSequence);
                writer.Write(clause.HandlerBlock.Value);
                WriteNullable(clause.FilterBlock, value => writer.Write(value.Value));
            });
            WriteArray(group.NormalContinuations, continuation =>
            {
                writer.Write(continuation.Id.Value);
                writer.Write(continuation.TargetOffset);
                writer.Write(continuation.Target.Value);
                WriteSequence(continuation.Body);
            });
            WriteNullable(group.ContinuationDispatcher, WriteDispatcher);
            WriteNullable(group.ContinuationJoinBlock, value => writer.Write(value.Value));
            WriteArray(group.ProtectedBlocks, value => writer.Write(value.Value));
        }

        private void WriteExceptionPart(StructuredExceptionPart part)
        {
            switch (part)
            {
                case StructuredExceptionCode code:
                    writer.Write((byte)0);
                    WriteSequence(code.Body);
                    break;
                case StructuredNestedExceptionGroup nested:
                    writer.Write((byte)1);
                    writer.Write(nested.Group.Value);
                    break;
                default:
                    throw new InvalidDataException(
                        $"Unsupported structured exception part type '{part.GetType().Name}'.");
            }
        }

        private void WriteMethodInstance(MethodInstanceModel method) =>
            writer.Write(tables.MethodInstance(method));

        private void WriteMethodDefinition(MethodDefinitionModel method) =>
            writer.Write(tables.MethodDefinition(method));

        private void WriteFieldInstance(FieldInstanceModel field) =>
            writer.Write(tables.FieldInstance(field));

        private void WriteType(CliTypeIdentity type) =>
            writer.Write(tables.Type(type));

        private void WriteStackMap(
            ImmutableDictionary<int, ImmutableArray<CliValueKind>> values) =>
            WriteDictionary(
                values.OrderBy(pair => pair.Key),
                pair => writer.Write(pair.Key),
                pair => WriteArray(pair.Value, WriteEnum));

        private void WriteEntityKey(EntityKey key)
        {
            WriteAssembly(key.Assembly);
            writer.Write(key.MetadataToken);
        }

        private void WriteAssembly(AssemblyIdentity assembly) => WriteString(assembly.Name);

        private void WriteString(string value) =>
            writer.Write(tables.String(value));

        private void WriteArray<T>(ImmutableArray<T> values, Action<T> write)
        {
            WriteArrayHeader(values);
            if (values.IsDefault)
            {
                return;
            }

            foreach (var value in values)
            {
                write(value);
            }
        }

        private void WriteArrayHeader<T>(ImmutableArray<T> values) =>
            writer.Write(values.IsDefault ? -1 : values.Length);

        private void WriteDictionary<T>(
            IEnumerable<T> values,
            Action<T> writeKey,
            Action<T> writeValue)
        {
            var materialized = values.ToArray();
            writer.Write(materialized.Length);
            foreach (var value in materialized)
            {
                writeKey(value);
                writeValue(value);
            }
        }

        private void WriteContinuationSet(
            ImmutableHashSet<StructuredContinuationId> values)
        {
            var materialized = values.OrderBy(value => value.Value).ToArray();
            writer.Write(materialized.Length);
            foreach (var value in materialized)
            {
                writer.Write(value.Value);
            }
        }

        private void WriteNullable<T>(T? value, Action<T> write)
            where T : class
        {
            writer.Write(value is not null);
            if (value is not null)
            {
                write(value);
            }
        }

        private void WriteNullable<T>(T? value, Action<T> write)
            where T : struct
        {
            writer.Write(value.HasValue);
            if (value is T present)
            {
                write(present);
            }
        }

        private void WriteEnum<T>(T value)
            where T : struct, Enum =>
            writer.Write(Convert.ToInt32(value, CultureInfo.InvariantCulture));
    }
}
