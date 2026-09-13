using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using NetWasm.Compiler.Core;

namespace NetWasm.Wit.Bindings;

public interface IWitCanonicalMarshallingTypeSectionWriter
{
    string Generate(WitDocument document, WitWorld world);
}

public sealed class WitCanonicalMarshallingTypeSectionWriter(
    IWitCanonicalTypeResolver types,
    ICanonicalAbiMemoryLayoutPlanner layouts,
    IWitCanonicalTypeReachabilityResolver reachability,
    IWitBindingSyntaxFormatter syntax,
    ICodeWriterFactory writers) : IWitCanonicalMarshallingTypeSectionWriter
{
    private readonly IWitCanonicalTypeResolver _types = types ??
        throw new ArgumentNullException(nameof(types));
    private readonly ICanonicalAbiMemoryLayoutPlanner _layouts = layouts ??
        throw new ArgumentNullException(nameof(layouts));
    private readonly IWitCanonicalTypeReachabilityResolver _reachability = reachability ??
        throw new ArgumentNullException(nameof(reachability));
    private readonly IWitBindingSyntaxFormatter _syntax = syntax ??
        throw new ArgumentNullException(nameof(syntax));
    private readonly ICodeWriterFactory _writers = writers ??
        throw new ArgumentNullException(nameof(writers));

    public string Generate(WitDocument document, WitWorld world)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(world);
        var reachable = _reachability.Resolve(document, world);
        if (reachable.Length == 0)
        {
            return string.Empty;
        }

        var writer = _writers.Create();
        writer.Line();
        writer.Line("internal static class __CanonicalMarshalling");
        writer.Line("{");
        writer.Indent();
        foreach (var id in reachable)
        {
            WriteType(writer, document, id);
        }
        writer.Unindent();
        writer.Line("}");
        return writer.Text;
    }

    private void WriteType(CodeWriter writer, WitDocument document, int id)
    {
        var definition = document.Types[id];
        if (definition.Kind.ValueKind == JsonValueKind.String)
        {
            return;
        }
        var reference = new WitTypeReference.Defined(id);
        var typeName = _syntax.Format(new WitBindingSyntaxRequest.TypeName(
            document,
            reference));
        var kind = definition.Kind.EnumerateObject().Single().Name;
        if (kind == "list")
        {
            WriteList(writer, document, id, typeName, definition);
        }
        else
        {
            WriteFixed(writer, document, id, typeName, reference);
        }
        writer.Line();
    }

    private void WriteFixed(
        CodeWriter writer,
        WitDocument document,
        int id,
        string typeName,
        WitTypeReference reference)
    {
        var type = _types.Resolve(document, reference);
        writer.Line($"internal static CanonicalBuffer LowerType{id}({typeName} value, bool exportBoundary)");
        writer.Line("{");
        writer.Indent();
        writer.Line($"var address = CanonicalAbi.Allocate({Size(type)}, {Alignment(type)});");
        writer.Line("try");
        writer.Line("{");
        writer.Indent();
        writer.Line($"WriteType{id}(address, value, exportBoundary);");
        writer.Line("return new CanonicalBuffer(address, 0);");
        writer.Unindent();
        writer.Line("}");
        writer.Line("catch");
        writer.Line("{");
        writer.Indent();
        writer.Line($"FreeType{id}(address);");
        writer.Line("throw;");
        writer.Unindent();
        writer.Line("}");
        writer.Unindent();
        writer.Line("}");
        writer.Line();
        writer.Line($"internal static void WriteType{id}(nuint address, {typeName} value, bool exportBoundary)");
        writer.Line("{");
        writer.Indent();
        WriteValue(
            writer,
            document,
            reference,
            "address",
            "0",
            "value",
            "exportBoundary");
        writer.Unindent();
        writer.Line("}");
        writer.Line();
        writer.Line($"internal static {typeName} LiftType{id}(nuint address, bool exportBoundary) =>");
        writer.Indent();
        writer.Line(LiftValue(
            document,
            reference,
            "address",
            "0",
            "exportBoundary") + ";");
        writer.Unindent();
        writer.Line();
        writer.Line($"internal static void FreeType{id}(nuint address)");
        writer.Line("{");
        writer.Indent();
        writer.Line($"FreeType{id}Contents(address);");
        writer.Line("CanonicalAbi.Free(address);");
        writer.Unindent();
        writer.Line("}");
        writer.Line();
        writer.Line($"internal static void FreeType{id}Contents(nuint address)");
        writer.Line("{");
        writer.Indent();
        FreeValue(writer, document, reference, "address", "0");
        writer.Unindent();
        writer.Line("}");
    }

    private void WriteList(
        CodeWriter writer,
        WitDocument document,
        int id,
        string typeName,
        WitTypeDefinition definition)
    {
        var elementReference = Reference(
            definition.Kind.EnumerateObject().Single().Value);
        var elementType = _types.Resolve(document, elementReference);
        writer.Line($"internal static CanonicalBuffer LowerType{id}({typeName} value, bool exportBoundary)");
        writer.Line("{");
        writer.Indent();
        writer.Line("if (value == null) throw new ArgumentNullException();");
        writer.Line($"var buffer = CanonicalAbi.AllocateElements((nuint)value.Length, {Size(elementType)}, {Alignment(elementType)});");
        writer.Line("try");
        writer.Line("{");
        writer.Indent();
        writer.Line("for (var index = 0; index < value.Length; index++)");
        writer.Line("{");
        writer.Indent();
        WriteValue(
            writer,
            document,
            elementReference,
            "buffer.Address",
            $"(nuint)index * {Size(elementType)}",
            "value[index]",
            "exportBoundary");
        writer.Unindent();
        writer.Line("}");
        writer.Line("return buffer;");
        writer.Unindent();
        writer.Line("}");
        writer.Line("catch");
        writer.Line("{");
        writer.Indent();
        writer.Line($"FreeType{id}(buffer.Address, buffer.Length);");
        writer.Line("throw;");
        writer.Unindent();
        writer.Line("}");
        writer.Unindent();
        writer.Line("}");
        writer.Line();
        writer.Line($"internal static {typeName} LiftType{id}(nuint address, nuint length, bool exportBoundary)");
        writer.Line("{");
        writer.Indent();
        writer.Line("if (length > int.MaxValue) throw new ArgumentException();");
        writer.Line($"var value = {_syntax.Format(new WitBindingSyntaxRequest.ArrayCreation(document, elementReference, "(int)length"))};");
        writer.Line("for (var index = 0; index < value.Length; index++)");
        writer.Line("{");
        writer.Indent();
        writer.Line($"value[index] = {LiftValue(document, elementReference, "address", $"(nuint)index * {Size(elementType)}", "exportBoundary")};");
        writer.Unindent();
        writer.Line("}");
        writer.Line("return value;");
        writer.Unindent();
        writer.Line("}");
        writer.Line();
        writer.Line($"internal static void FreeType{id}(nuint address, nuint length)");
        writer.Line("{");
        writer.Indent();
        if (ContainsAllocation(elementType))
        {
            writer.Line("for (nuint index = 0; index < length; index++)");
            writer.Line("{");
            writer.Indent();
            FreeValue(
                writer,
                document,
                elementReference,
                "address",
                $"index * {Size(elementType)}");
            writer.Unindent();
            writer.Line("}");
        }
        writer.Line("CanonicalAbi.Free(address);");
        writer.Unindent();
        writer.Line("}");
    }

    private void WriteValue(
        CodeWriter writer,
        WitDocument document,
        WitTypeReference reference,
        string address,
        string offset,
        string value,
        string exportBoundary)
    {
        if (reference is WitTypeReference.Primitive primitive)
        {
            WritePrimitive(writer, primitive.Name, address, offset, value);
            return;
        }
        var defined = (WitTypeReference.Defined)reference;
        var definition = document.Types[defined.Id];
        if (definition.Kind.ValueKind == JsonValueKind.String)
        {
            WriteResource(
                writer,
                CanonicalAbiTypeKind.OwnedResource,
                address,
                offset,
                value,
                exportBoundary);
            return;
        }
        var kind = definition.Kind.EnumerateObject().Single();
        var operations = new Dictionary<string, Action>(StringComparer.Ordinal)
        {
            ["type"] = () => WriteValue(
                    writer,
                    document,
                    Reference(kind.Value),
                    address,
                    offset,
                    value,
                    exportBoundary),
            ["list"] = () =>
            {
                writer.Line($"var __list{defined.Id} = LowerType{defined.Id}({value}, {exportBoundary});");
                writer.Line($"CanonicalAbi.WriteAddress({address}, {offset}, __list{defined.Id}.Address);");
                writer.Line($"CanonicalAbi.WriteAddress({address}, {Add(offset, "(nuint)UIntPtr.Size")}, __list{defined.Id}.Length);");
            },
            ["record"] = () => WriteRecord(
                writer, document, reference, kind.Value, address, offset, value, exportBoundary),
            ["tuple"] = () => WriteTuple(
                writer, document, reference, kind.Value, address, offset, value, exportBoundary),
            ["option"] = () => WriteOption(
                writer, document, reference, kind.Value, address, offset, value, exportBoundary),
            ["result"] = () => WriteResult(
                writer, document, reference, kind.Value, address, offset, value, exportBoundary),
            ["variant"] = () => WriteVariant(
                writer, document, reference, kind.Value, address, offset, value, exportBoundary),
            ["enum"] = () => WriteDiscriminant(
                writer, document, reference, address, offset, $"(int){value}"),
            ["flags"] = () => WriteFlags(writer, kind.Value, address, offset, value),
            ["handle"] = () => WriteResource(
                    writer,
                    _types.Resolve(document, reference).Kind,
                    address,
                    offset,
                    value,
                    exportBoundary),
        };
        operations[kind.Name]();
    }

    private static void WriteResource(
        CodeWriter writer,
        CanonicalAbiTypeKind kind,
        string address,
        string offset,
        string value,
        string exportBoundary)
    {
        var transferOwnership = kind == CanonicalAbiTypeKind.OwnedResource
            ? "true"
            : "false";
        writer.Line(
            $"CanonicalAbi.WriteInt32({address}, {offset}, unchecked((int)({exportBoundary} ? {value}.LowerExport() : {value}.LowerImport({transferOwnership}))));");
    }

    private static void WritePrimitive(
        CodeWriter writer,
        string name,
        string address,
        string offset,
        string value)
    {
        var operations = new Dictionary<string, Action>(StringComparer.Ordinal)
        {
            ["bool"] = () => writer.Line($"CanonicalAbi.WriteByte({address}, {offset}, {value} ? (byte)1 : (byte)0);"),
            ["s8"] = () => writer.Line($"CanonicalAbi.WriteByte({address}, {offset}, unchecked((byte){value}));"),
            ["u8"] = () => writer.Line($"CanonicalAbi.WriteByte({address}, {offset}, unchecked((byte){value}));"),
            ["s16"] = () => writer.Line($"CanonicalAbi.WriteUInt16({address}, {offset}, unchecked((ushort){value}));"),
            ["u16"] = () => writer.Line($"CanonicalAbi.WriteUInt16({address}, {offset}, unchecked((ushort){value}));"),
            ["s32"] = () => writer.Line($"CanonicalAbi.WriteInt32({address}, {offset}, unchecked((int){value}));"),
            ["u32"] = () => writer.Line($"CanonicalAbi.WriteInt32({address}, {offset}, unchecked((int){value}));"),
            ["char"] = () => writer.Line($"CanonicalAbi.WriteInt32({address}, {offset}, unchecked((int){value}));"),
            ["s64"] = () => writer.Line($"CanonicalAbi.WriteInt64({address}, {offset}, unchecked((long){value}));"),
            ["u64"] = () => writer.Line($"CanonicalAbi.WriteInt64({address}, {offset}, unchecked((long){value}));"),
            ["f32"] = () => writer.Line($"CanonicalAbi.WriteSingle({address}, {offset}, {value});"),
            ["f64"] = () => writer.Line($"CanonicalAbi.WriteDouble({address}, {offset}, {value});"),
            ["string"] = () =>
            {
                writer.Line($"var __text = CanonicalAbi.LowerString({value});");
                writer.Line($"CanonicalAbi.WriteAddress({address}, {offset}, __text.Address);");
                writer.Line($"CanonicalAbi.WriteAddress({address}, {Add(offset, "(nuint)UIntPtr.Size")}, __text.Length);");
            },
        };
        operations[name]();
    }

    private void WriteRecord(
        CodeWriter writer,
        WitDocument document,
        WitTypeReference reference,
        JsonElement record,
        string address,
        string offset,
        string value,
        string exportBoundary)
    {
        var fields = record.GetProperty("fields").EnumerateArray().ToArray();
        var layout32 = _layouts.Plan(_types.Resolve(document, reference), WasmTarget.Wasm32);
        var layout64 = _layouts.Plan(_types.Resolve(document, reference), WasmTarget.Wasm64);
        for (var index = 0; index < fields.Length; index++)
        {
            var field = fields[index];
            WriteValue(
                writer,
                document,
                Reference(field.GetProperty("type")),
                address,
                Add(offset, Width(layout32.Fields[index].Offset,
                    layout64.Fields[index].Offset)),
                $"{value}.{_syntax.Format(new WitBindingSyntaxRequest.Identifier(field.GetProperty("name").GetString()!))}",
                exportBoundary);
        }
    }

    private void WriteTuple(
        CodeWriter writer,
        WitDocument document,
        WitTypeReference reference,
        JsonElement tuple,
        string address,
        string offset,
        string value,
        string exportBoundary)
    {
        var fields = tuple.GetProperty("types").EnumerateArray().ToArray();
        var layout32 = _layouts.Plan(_types.Resolve(document, reference), WasmTarget.Wasm32);
        var layout64 = _layouts.Plan(_types.Resolve(document, reference), WasmTarget.Wasm64);
        for (var index = 0; index < fields.Length; index++)
        {
            WriteValue(
                writer,
                document,
                Reference(fields[index]),
                address,
                Add(offset, Width(layout32.Fields[index].Offset,
                    layout64.Fields[index].Offset)),
                $"{value}.Item{index + 1}",
                exportBoundary);
        }
    }

    private void WriteOption(
        CodeWriter writer,
        WitDocument document,
        WitTypeReference reference,
        JsonElement element,
        string address,
        string offset,
        string value,
        string exportBoundary)
    {
        writer.Line($"CanonicalAbi.WriteByte({address}, {offset}, {value}.HasValue ? (byte)1 : (byte)0);");
        writer.Line($"if ({value}.HasValue)");
        writer.Line("{");
        writer.Indent();
        WriteValue(writer, document, Reference(element), address,
            Add(offset, PayloadOffset(document, reference)), $"{value}.Value",
            exportBoundary);
        writer.Unindent();
        writer.Line("}");
    }

    private void WriteResult(
        CodeWriter writer,
        WitDocument document,
        WitTypeReference reference,
        JsonElement result,
        string address,
        string offset,
        string value,
        string exportBoundary)
    {
        writer.Line($"CanonicalAbi.WriteByte({address}, {offset}, {value}.IsOk ? (byte)0 : (byte)1);");
        writer.Line($"if ({value}.IsOk)");
        writer.Line("{");
        writer.Indent();
        WriteResultArm(writer, document, result, "ok", address,
            Add(offset, PayloadOffset(document, reference)), $"{value}.Ok",
            exportBoundary);
        writer.Unindent();
        writer.Line("}");
        writer.Line("else");
        writer.Line("{");
        writer.Indent();
        WriteResultArm(writer, document, result, "err", address,
            Add(offset, PayloadOffset(document, reference)), $"{value}.Error",
            exportBoundary);
        writer.Unindent();
        writer.Line("}");
    }

    private void WriteResultArm(
        CodeWriter writer,
        WitDocument document,
        JsonElement result,
        string arm,
        string address,
        string offset,
        string value,
        string exportBoundary)
    {
        var type = result.GetProperty(arm);
        if (type.ValueKind != JsonValueKind.Null)
        {
            WriteValue(
                writer,
                document,
                Reference(type),
                address,
                offset,
                value,
                exportBoundary);
        }
    }

    private void WriteVariant(
        CodeWriter writer,
        WitDocument document,
        WitTypeReference reference,
        JsonElement variant,
        string address,
        string offset,
        string value,
        string exportBoundary)
    {
        var typeName = _syntax.Format(new WitBindingSyntaxRequest.TypeName(document, reference));
        var cases = variant.GetProperty("cases").EnumerateArray().ToArray();
        WriteDiscriminant(writer, document, reference, address, offset, $"(int){value}.Tag");
        writer.Line($"switch ({value}.Tag)");
        writer.Line("{");
        writer.Indent();
        foreach (var item in cases)
        {
            var name = _syntax.Format(new WitBindingSyntaxRequest.Identifier(
                item.GetProperty("name").GetString()!));
            writer.Line($"case {typeName}Tag.{name}:");
            writer.Indent();
            var caseType = item.GetProperty("type");
            if (caseType.ValueKind != JsonValueKind.Null)
            {
                WriteValue(writer, document, Reference(caseType), address,
                    Add(offset, PayloadOffset(document, reference)),
                    $"{value}.{name}Value",
                    exportBoundary);
            }
            writer.Line("break;");
            writer.Unindent();
        }
        writer.Line("default: throw new ArgumentException();");
        writer.Unindent();
        writer.Line("}");
    }

    private void WriteDiscriminant(
        CodeWriter writer,
        WitDocument document,
        WitTypeReference reference,
        string address,
        string offset,
        string value)
    {
        var type = _types.Resolve(document, reference);
        var size = _layouts.Plan(type, WasmTarget.Wasm32).DiscriminantSize;
        if (size == 0)
        {
            size = _layouts.Plan(type, WasmTarget.Wasm32).Size;
        }
        if (size == 1)
        {
            writer.Line($"CanonicalAbi.WriteByte({address}, {offset}, checked((byte){value}));");
        }
        else if (size == 2)
        {
            writer.Line($"CanonicalAbi.WriteUInt16({address}, {offset}, checked((ushort){value}));");
        }
        else
        {
            writer.Line($"CanonicalAbi.WriteInt32({address}, {offset}, {value});");
        }
    }

    private static void WriteFlags(
        CodeWriter writer,
        JsonElement flags,
        string address,
        string offset,
        string value)
    {
        var count = flags.GetProperty("flags").GetArrayLength();
        if (count <= 8)
        {
            writer.Line($"CanonicalAbi.WriteByte({address}, {offset}, unchecked((byte){value}));");
        }
        else if (count <= 16)
        {
            writer.Line($"CanonicalAbi.WriteUInt16({address}, {offset}, unchecked((ushort){value}));");
        }
        else if (count <= 32)
        {
            writer.Line($"CanonicalAbi.WriteInt32({address}, {offset}, unchecked((int){value}));");
        }
        else if (count <= 64)
        {
            writer.Line($"CanonicalAbi.WriteInt32({address}, {offset}, unchecked((int)(uint)(ulong){value}));");
            writer.Line($"CanonicalAbi.WriteInt32({address}, {Add(offset, "(nuint)4")}, unchecked((int)((ulong){value} >> 32)));");
        }
        else
        {
            var words = (count + 31) / 32;
            writer.Line($"for (var index = 0; index < {words}; index++)");
            writer.Line("{");
            writer.Indent();
            writer.Line($"CanonicalAbi.WriteInt32({address}, {Add(offset, "(nuint)index * 4")}, unchecked((int){value}.GetWord(index)));");
            writer.Unindent();
            writer.Line("}");
        }
    }

#pragma warning disable CS8509 // definition kinds and primitive names are resolver-validated
    private string LiftValue(
        WitDocument document,
        WitTypeReference reference,
        string address,
        string offset,
        string exportBoundary)
    {
        if (reference is WitTypeReference.Primitive primitive)
        {
            return LiftPrimitive(primitive.Name, address, offset);
        }
        var defined = (WitTypeReference.Defined)reference;
        var definition = document.Types[defined.Id];
        if (definition.Kind.ValueKind == JsonValueKind.String)
        {
            return LiftResource(
                document,
                reference,
                CanonicalAbiTypeKind.OwnedResource,
                address,
                offset,
                exportBoundary);
        }
        var kind = definition.Kind.EnumerateObject().Single();
        var operations = new Dictionary<string, Func<string>>(StringComparer.Ordinal)
        {
            ["type"] = () => LiftValue(
                document,
                Reference(kind.Value),
                address,
                offset,
                exportBoundary),
            ["list"] = () => $"LiftType{defined.Id}(CanonicalAbi.ReadAddress({address}, {offset}), CanonicalAbi.ReadAddress({address}, {Add(offset, "(nuint)UIntPtr.Size")}), {exportBoundary})",
            ["record"] = () => LiftRecord(document, reference, kind.Value, address, offset, exportBoundary),
            ["tuple"] = () => LiftTuple(document, reference, kind.Value, address, offset, exportBoundary),
            ["option"] = () => LiftOption(document, reference, kind.Value, address, offset, exportBoundary),
            ["result"] = () => LiftResult(document, reference, kind.Value, address, offset, exportBoundary),
            ["variant"] = () => LiftVariant(document, reference, kind.Value, address, offset, exportBoundary),
            ["enum"] = () => $"({_syntax.Format(new WitBindingSyntaxRequest.TypeName(document, reference))})CanonicalAbi.LiftDiscriminant({ReadDiscriminant(document, reference, address, offset)}, {kind.Value.GetProperty("cases").GetArrayLength()})",
            ["flags"] = () => LiftFlags(document, reference, kind.Value, address, offset),
            ["handle"] = () => LiftResource(
                document,
                reference,
                _types.Resolve(document, reference).Kind,
                address,
                offset,
                exportBoundary),
        };
        return operations[kind.Name]();
    }

    private string LiftResource(
        WitDocument document,
        WitTypeReference reference,
        CanonicalAbiTypeKind kind,
        string address,
        string offset,
        string exportBoundary)
    {
        var typeName = _syntax.Format(new WitBindingSyntaxRequest.TypeName(document, reference));
        var ownsHandle = kind == CanonicalAbiTypeKind.OwnedResource
            ? "true"
            : "false";
        var handle = $"unchecked((uint)CanonicalAbi.ReadInt32({address}, {offset}))";
        return $"{exportBoundary} ? {typeName}.LiftExport({handle}, {ownsHandle}) : new {typeName}({handle}, {ownsHandle})";
    }

    private static readonly Dictionary<
        string,
        Func<string, string, string>> LiftPrimitiveOperations =
        new Dictionary<string, Func<string, string, string>>(StringComparer.Ordinal)
        {
            ["bool"] = (address, offset) => $"CanonicalAbi.LiftBoolean(CanonicalAbi.ReadByte({address}, {offset}))",
            ["s8"] = (address, offset) => $"unchecked((sbyte)CanonicalAbi.ReadByte({address}, {offset}))",
            ["u8"] = (address, offset) => $"CanonicalAbi.ReadByte({address}, {offset})",
            ["s16"] = (address, offset) => $"unchecked((short)CanonicalAbi.ReadUInt16({address}, {offset}))",
            ["u16"] = (address, offset) => $"CanonicalAbi.ReadUInt16({address}, {offset})",
            ["s32"] = (address, offset) => $"CanonicalAbi.ReadInt32({address}, {offset})",
            ["u32"] = (address, offset) => $"unchecked((uint)CanonicalAbi.ReadInt32({address}, {offset}))",
            ["char"] = (address, offset) => $"CanonicalAbi.LiftCharacter(CanonicalAbi.ReadInt32({address}, {offset}))",
            ["s64"] = (address, offset) => $"CanonicalAbi.ReadInt64({address}, {offset})",
            ["u64"] = (address, offset) => $"unchecked((ulong)CanonicalAbi.ReadInt64({address}, {offset}))",
            ["f32"] = (address, offset) => $"CanonicalAbi.ReadSingle({address}, {offset})",
            ["f64"] = (address, offset) => $"CanonicalAbi.ReadDouble({address}, {offset})",
            ["string"] = (address, offset) => $"CanonicalAbi.LiftString(CanonicalAbi.ReadAddress({address}, {offset}), CanonicalAbi.ReadAddress({address}, {Add(offset, "(nuint)UIntPtr.Size")}))",
        };

    private static string LiftPrimitive(
        string name,
        string address,
        string offset) => LiftPrimitiveOperations[name](address, offset);
#pragma warning restore CS8509

    private string LiftRecord(
        WitDocument document,
        WitTypeReference reference,
        JsonElement record,
        string address,
        string offset,
        string exportBoundary)
    {
        var fields = record.GetProperty("fields").EnumerateArray().ToArray();
        var layout32 = _layouts.Plan(_types.Resolve(document, reference), WasmTarget.Wasm32);
        var layout64 = _layouts.Plan(_types.Resolve(document, reference), WasmTarget.Wasm64);
        var arguments = fields.Select((field, index) => LiftValue(
            document,
            Reference(field.GetProperty("type")),
            address,
            Add(offset, Width(layout32.Fields[index].Offset,
                layout64.Fields[index].Offset)),
            exportBoundary));
        return $"new {_syntax.Format(new WitBindingSyntaxRequest.TypeName(document, reference))}({string.Join(", ", arguments)})";
    }

    private string LiftTuple(
        WitDocument document,
        WitTypeReference reference,
        JsonElement tuple,
        string address,
        string offset,
        string exportBoundary)
    {
        var fields = tuple.GetProperty("types").EnumerateArray().ToArray();
        var layout32 = _layouts.Plan(_types.Resolve(document, reference), WasmTarget.Wasm32);
        var layout64 = _layouts.Plan(_types.Resolve(document, reference), WasmTarget.Wasm64);
        return $"({string.Join(", ", fields.Select((field, index) => LiftValue(document, Reference(field), address, Add(offset, Width(layout32.Fields[index].Offset, layout64.Fields[index].Offset)), exportBoundary)))})";
    }

    private string LiftOption(
        WitDocument document,
        WitTypeReference reference,
        JsonElement element,
        string address,
        string offset,
        string exportBoundary)
    {
        var typeName = _syntax.Format(new WitBindingSyntaxRequest.TypeName(document, reference));
        var tag = $"CanonicalAbi.LiftDiscriminant(CanonicalAbi.ReadByte({address}, {offset}), 2)";
        var some = LiftValue(document, Reference(element), address,
            Add(offset, PayloadOffset(document, reference)), exportBoundary);
        return $"{tag} == 0 ? {typeName}.None : new {typeName}({some})";
    }

    private string LiftResult(
        WitDocument document,
        WitTypeReference reference,
        JsonElement result,
        string address,
        string offset,
        string exportBoundary)
    {
        var typeName = _syntax.Format(new WitBindingSyntaxRequest.TypeName(document, reference));
        var tag = $"CanonicalAbi.LiftDiscriminant(CanonicalAbi.ReadByte({address}, {offset}), 2)";
        var payloadOffset = Add(offset, PayloadOffset(document, reference));
        var ok = LiftResultArm(
            document,
            result,
            "ok",
            address,
            payloadOffset,
            exportBoundary);
        var error = LiftResultArm(
            document,
            result,
            "err",
            address,
            payloadOffset,
            exportBoundary);
        return $"{tag} == 0 ? {typeName}.FromOk({ok}) : {typeName}.FromError({error})";
    }

    private string LiftResultArm(
        WitDocument document,
        JsonElement result,
        string arm,
        string address,
        string offset,
        string exportBoundary)
    {
        var value = result.GetProperty(arm);
        return value.ValueKind == JsonValueKind.Null
            ? "default"
            : LiftValue(
                document,
                Reference(value),
                address,
                offset,
                exportBoundary);
    }

    private string LiftVariant(
        WitDocument document,
        WitTypeReference reference,
        JsonElement variant,
        string address,
        string offset,
        string exportBoundary)
    {
        var cases = variant.GetProperty("cases").EnumerateArray().ToArray();
        var typeName = _syntax.Format(new WitBindingSyntaxRequest.TypeName(document, reference));
        var tag = $"CanonicalAbi.LiftDiscriminant({ReadDiscriminant(document, reference, address, offset)}, {cases.Length})";
        var payloadOffset = Add(offset, PayloadOffset(document, reference));
        var arms = cases.Select((item, index) =>
        {
            var name = _syntax.Format(new WitBindingSyntaxRequest.Identifier(
                item.GetProperty("name").GetString()!));
            var caseType = item.GetProperty("type");
            var argument = caseType.ValueKind == JsonValueKind.Null
                ? string.Empty
                : LiftValue(
                    document,
                    Reference(caseType),
                    address,
                    payloadOffset,
                    exportBoundary);
            return $"{index} => {typeName}.{name}({argument})";
        });
        return $"{tag} switch {{ {string.Join(", ", arms)}, _ => throw new ArgumentException() }}";
    }

    private string LiftFlags(
        WitDocument document,
        WitTypeReference reference,
        JsonElement flags,
        string address,
        string offset)
    {
        var count = flags.GetProperty("flags").GetArrayLength();
        var typeName = _syntax.Format(new WitBindingSyntaxRequest.TypeName(document, reference));
        if (count <= 8)
        {
            return $"({typeName})CanonicalAbi.LiftFlagsWord(CanonicalAbi.ReadByte({address}, {offset}), {count})";
        }
        if (count <= 16)
        {
            return $"({typeName})CanonicalAbi.LiftFlagsWord(CanonicalAbi.ReadUInt16({address}, {offset}), {count})";
        }
        if (count <= 32)
        {
            return $"({typeName})CanonicalAbi.LiftFlagsWord(unchecked((uint)CanonicalAbi.ReadInt32({address}, {offset})), {count})";
        }
        if (count <= 64)
        {
            var finalBits = count - 32;
            return $"({typeName})(unchecked((ulong)(uint)CanonicalAbi.ReadInt32({address}, {offset})) | (ulong)CanonicalAbi.LiftFlagsWord(unchecked((uint)CanonicalAbi.ReadInt32({address}, {Add(offset, "(nuint)4")})), {finalBits}) << 32)";
        }
        var words = (count + 31) / 32;
        var expressions = Enumerable.Range(0, words).Select(index =>
        {
            var read = $"unchecked((uint)CanonicalAbi.ReadInt32({address}, {Add(offset, $"(nuint){index * 4}")}))";
            return index + 1 == words && count % 32 != 0
                ? $"CanonicalAbi.LiftFlagsWord({read}, {count % 32})"
                : read;
        });
        return $"{typeName}.FromWords(new uint[] {{ {string.Join(", ", expressions)} }})";
    }

    private void FreeValue(
        CodeWriter writer,
        WitDocument document,
        WitTypeReference reference,
        string address,
        string offset)
    {
        var type = _types.Resolve(document, reference);
        if (!ContainsAllocation(type))
        {
            return;
        }
        if (reference is WitTypeReference.Primitive { Name: "string" })
        {
            writer.Line($"CanonicalAbi.Free(CanonicalAbi.ReadAddress({address}, {offset}));");
            return;
        }
        var defined = (WitTypeReference.Defined)reference;
        var definition = document.Types[defined.Id];
        var kind = definition.Kind.EnumerateObject().Single();
        var operations = new Dictionary<string, Action>(StringComparer.Ordinal)
        {
            ["type"] = () => FreeValue(writer, document, Reference(kind.Value), address, offset),
            ["list"] = () => writer.Line(
                $"FreeType{defined.Id}(CanonicalAbi.ReadAddress({address}, {offset}), CanonicalAbi.ReadAddress({address}, {Add(offset, "(nuint)UIntPtr.Size")}));"),
            ["record"] = () => FreeAggregate(
                writer, document, reference, kind.Value, address, offset),
            ["tuple"] = () => FreeAggregate(
                writer, document, reference, kind.Value, address, offset),
            ["option"] = () =>
            {
                writer.Line($"if (CanonicalAbi.ReadByte({address}, {offset}) == 1)");
                writer.Line("{");
                writer.Indent();
                FreeValue(writer, document, Reference(kind.Value), address,
                    Add(offset, PayloadOffset(document, reference)));
                writer.Unindent();
                writer.Line("}");
            },
            ["result"] = () => FreeResult(
                writer, document, reference, kind.Value, address, offset),
            ["variant"] = () => FreeVariant(
                writer, document, reference, kind.Value, address, offset),
        };
        operations[kind.Name]();
    }

    private void FreeAggregate(
        CodeWriter writer,
        WitDocument document,
        WitTypeReference reference,
        JsonElement aggregate,
        string address,
        string offset)
    {
        var values = aggregate.TryGetProperty("fields", out var fields)
            ? fields.EnumerateArray().Select(field => field.GetProperty("type")).ToArray()
            : aggregate.GetProperty("types").EnumerateArray().ToArray();
        var layout32 = _layouts.Plan(_types.Resolve(document, reference), WasmTarget.Wasm32);
        var layout64 = _layouts.Plan(_types.Resolve(document, reference), WasmTarget.Wasm64);
        for (var index = 0; index < values.Length; index++)
        {
            FreeValue(writer, document, Reference(values[index]), address,
                Add(offset, Width(layout32.Fields[index].Offset,
                    layout64.Fields[index].Offset)));
        }
    }

    private void FreeResult(
        CodeWriter writer,
        WitDocument document,
        WitTypeReference reference,
        JsonElement result,
        string address,
        string offset)
    {
        var payloadOffset = Add(offset, PayloadOffset(document, reference));
        writer.Line($"if (CanonicalAbi.ReadByte({address}, {offset}) == 0)");
        writer.Line("{");
        writer.Indent();
        FreeResultArm(writer, document, result, "ok", address, payloadOffset);
        writer.Unindent();
        writer.Line("}");
        writer.Line("else");
        writer.Line("{");
        writer.Indent();
        FreeResultArm(writer, document, result, "err", address, payloadOffset);
        writer.Unindent();
        writer.Line("}");
    }

    private void FreeResultArm(
        CodeWriter writer,
        WitDocument document,
        JsonElement result,
        string arm,
        string address,
        string offset)
    {
        var value = result.GetProperty(arm);
        if (value.ValueKind != JsonValueKind.Null)
        {
            FreeValue(writer, document, Reference(value), address, offset);
        }
    }

    private void FreeVariant(
        CodeWriter writer,
        WitDocument document,
        WitTypeReference reference,
        JsonElement variant,
        string address,
        string offset)
    {
        var cases = variant.GetProperty("cases").EnumerateArray().ToArray();
        var payloadOffset = Add(offset, PayloadOffset(document, reference));
        writer.Line($"switch ({ReadDiscriminant(document, reference, address, offset)})");
        writer.Line("{");
        writer.Indent();
        for (var index = 0; index < cases.Length; index++)
        {
            var caseType = cases[index].GetProperty("type");
            if (caseType.ValueKind == JsonValueKind.Null ||
                !ContainsAllocation(_types.Resolve(document, Reference(caseType))))
            {
                continue;
            }
            writer.Line($"case {index}:");
            writer.Indent();
            FreeValue(writer, document, Reference(caseType), address, payloadOffset);
            writer.Line("break;");
            writer.Unindent();
        }
        writer.Unindent();
        writer.Line("}");
    }

    private string ReadDiscriminant(
        WitDocument document,
        WitTypeReference reference,
        string address,
        string offset)
    {
        var layout = _layouts.Plan(_types.Resolve(document, reference), WasmTarget.Wasm32);
        var size = layout.DiscriminantSize == 0 ? layout.Size : layout.DiscriminantSize;
        return size switch
        {
            1 => $"CanonicalAbi.ReadByte({address}, {offset})",
            2 => $"CanonicalAbi.ReadUInt16({address}, {offset})",
            _ => $"CanonicalAbi.ReadInt32({address}, {offset})",
        };
    }

    private string PayloadOffset(
        WitDocument document,
        WitTypeReference reference)
    {
        var type = _types.Resolve(document, reference);
        return Width(
            _layouts.Plan(type, WasmTarget.Wasm32).PayloadOffset,
            _layouts.Plan(type, WasmTarget.Wasm64).PayloadOffset);
    }

    private string Size(CanonicalAbiType type) => Width(
        _layouts.Plan(type, WasmTarget.Wasm32).Size,
        _layouts.Plan(type, WasmTarget.Wasm64).Size);

    private string Alignment(CanonicalAbiType type) => Width(
        _layouts.Plan(type, WasmTarget.Wasm32).Alignment,
        _layouts.Plan(type, WasmTarget.Wasm64).Alignment);

    private static string Width(int wasm32, int wasm64) => wasm32 == wasm64
        ? $"(nuint){wasm32}"
        : $"(nuint)(UIntPtr.Size == 8 ? {wasm64} : {wasm32})";

    private static string Add(string left, string right) =>
        right is "0" or "(nuint)0" ? left : $"({left} + {right})";

    private static bool ContainsAllocation(CanonicalAbiType type) =>
        type.Kind switch
        {
            CanonicalAbiTypeKind.Text or CanonicalAbiTypeKind.List => true,
            CanonicalAbiTypeKind.Alias => ContainsAllocation(type.ElementType!),
            CanonicalAbiTypeKind.Record or CanonicalAbiTypeKind.Tuple =>
                type.Fields.Any(field => ContainsAllocation(field.Type)),
            CanonicalAbiTypeKind.Option => ContainsAllocation(type.ElementType!),
            CanonicalAbiTypeKind.Result =>
                ContainsAllocation(type.SuccessType!) ||
                ContainsAllocation(type.ErrorType!),
            CanonicalAbiTypeKind.Variant => type.Cases.Any(@case =>
                @case.Type is not null && ContainsAllocation(@case.Type)),
            _ => false,
        };

    private static WitTypeReference Reference(JsonElement value) =>
        value.ValueKind == JsonValueKind.String
            ? new WitTypeReference.Primitive(value.GetString()!)
            : new WitTypeReference.Defined(value.GetInt32());
}
