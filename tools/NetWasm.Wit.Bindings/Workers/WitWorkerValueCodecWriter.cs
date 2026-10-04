using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using NetWasm.Hosting.Deployment;

namespace NetWasm.Wit.Bindings.Workers;

public sealed record WitWorkerValueCodecSource(string CSharpWrite, string CSharpRead, string JavaScriptWrite, string JavaScriptRead);

public sealed record WitWorkerValueCodecRequest(
    int Id, WitWorkerCSharpContract Contract, IWitBindingSyntaxFormatter Syntax)
{
    public WitWorkerValueLayout Layout => Contract.Layouts[Id];
    public string Type(WitWorkerValueReference reference) => Syntax.Format(
        new WitBindingSyntaxRequest.TypeName(Contract.Document, Contract.Reference(reference)));
    public string Type(JsonElement reference) => Type(WitWorkerValueLayoutPlanner.ReadReference(reference));
    public string Name => Type(new WitWorkerValueReference("defined", null, Id));
    public string Identifier(string value) => Syntax.Format(new WitBindingSyntaxRequest.Identifier(value));
    public static string Json(string value) => JsonSerializer.Serialize(value);
    public static string Suffix(WitWorkerValueReference reference) => reference.Definition is { } id
        ? "Type" + id.ToString(CultureInfo.InvariantCulture) : reference.Primitive!;
    public static string Suffix(JsonElement reference) => Suffix(WitWorkerValueLayoutPlanner.ReadReference(reference));
    public static string Write(JsonElement reference, string value, bool javascript = false) =>
        $"{(javascript ? "write" : "Write")}{Suffix(reference)}(writer, {value});";
    public static string Read(JsonElement reference, bool javascript = false) =>
        $"{(javascript ? "read" : "Read")}{Suffix(reference)}(reader)";
}

public interface IWitWorkerValueCodecWriter
{
    WitWorkerValueCodecSource Write(WitWorkerValueCodecRequest request);
}

public sealed class WitWorkerUnaryValueCodecWriter(string kind) : IWitWorkerValueCodecWriter
{
    public WitWorkerValueCodecSource Write(WitWorkerValueCodecRequest request)
    {
        var target = request.Layout.Value;
        if (kind == "type") return new(WitWorkerValueCodecRequest.Write(target, "value"),
            $"return {WitWorkerValueCodecRequest.Read(target)};", WitWorkerValueCodecRequest.Write(target, "value", true),
            $"return {WitWorkerValueCodecRequest.Read(target, true)};");
        if (kind == "list")
        {
            var array = WitWorkerValueLayoutPlanner.NumericArray(WitWorkerValueLayoutPlanner.ReadReference(target), request.Contract.Layouts);
            var csArray = request.Syntax.Format(new WitBindingSyntaxRequest.ArrayCreation(
                request.Contract.Document, request.Contract.Reference(WitWorkerValueLayoutPlanner.ReadReference(target)), "count"));
            return new(
                $"Writeu32(writer, checked((uint)value.Length)); foreach (var item in value) {{ {WitWorkerValueCodecRequest.Write(target, "item")} }}",
                $"var count = reader.Count(); var result = {csArray}; for (var index = 0; index < count; index++) result[index] = {WitWorkerValueCodecRequest.Read(target)}; return result;",
                $"writeu32(writer, value.length); for (const item of value) {{ {WitWorkerValueCodecRequest.Write(target, "item", true)} }}",
                $"const count = reader.count(); const result = new {(array ?? "Array")}(count); for (let index = 0; index < count; index++) result[index] = {WitWorkerValueCodecRequest.Read(target, true)}; return result;");
        }
        var nullable = request.Contract.Layouts[request.Id].Nullable;
        var jsNone = nullable ? "undefined" : "{ tag: 'none' }";
        var jsSome = nullable ? WitWorkerValueCodecRequest.Read(target, true)
            : $"{{ tag: 'some', val: {WitWorkerValueCodecRequest.Read(target, true)} }}";
        var jsTest = nullable ? "value !== undefined" : "value.tag === 'some'";
        var jsValue = nullable ? "value" : "value.val";
        return new(
            $"Writebool(writer, value.HasValue); if (value.HasValue) {{ {WitWorkerValueCodecRequest.Write(target, "value.Value")} }}",
            $"return Readbool(reader) ? new {request.Name}({WitWorkerValueCodecRequest.Read(target)}) : {request.Name}.None;",
            $"const some = {jsTest}; writebool(writer, some); if (some) {{ {WitWorkerValueCodecRequest.Write(target, jsValue, true)} }}",
            $"return readbool(reader) ? {jsSome} : {jsNone};");
    }
}

public sealed class WitWorkerRecordValueCodecWriter : IWitWorkerValueCodecWriter
{
    public WitWorkerValueCodecSource Write(WitWorkerValueCodecRequest request)
    {
        var fields = request.Layout.Value.GetProperty("fields").EnumerateArray().ToArray();
        if (fields.Length == 0) return new("Writeu8(writer, 0);", $"Readunit(reader); return new {request.Name}();",
            "writeu8(writer, 0);", "readunit(reader); return {};");
        var csWrite = fields.Select(field => WitWorkerValueCodecRequest.Write(field.GetProperty("type"),
            "value." + request.Identifier(field.GetProperty("name").GetString()!)));
        var jsWrite = fields.Select(field => WitWorkerValueCodecRequest.Write(field.GetProperty("type"),
            $"value[{WitWorkerValueCodecRequest.Json(WitWorkerValueLayoutPlanner.MemberName(field.GetProperty("name").GetString()!))}]", true));
        var csRead = fields.Select(field => WitWorkerValueCodecRequest.Read(field.GetProperty("type")));
        var jsRead = fields.Select(field => $"[{WitWorkerValueCodecRequest.Json(WitWorkerValueLayoutPlanner.MemberName(field.GetProperty("name").GetString()!))}]: {WitWorkerValueCodecRequest.Read(field.GetProperty("type"), true)}");
        return new(string.Join("\n", csWrite), $"return new {request.Name}({string.Join(", ", csRead)});",
            string.Join("\n", jsWrite), $"return {{ {string.Join(", ", jsRead)} }};");
    }
}

public sealed class WitWorkerTupleValueCodecWriter : IWitWorkerValueCodecWriter
{
    public WitWorkerValueCodecSource Write(WitWorkerValueCodecRequest request)
    {
        var types = request.Layout.Value.GetProperty("types").EnumerateArray().ToArray();
        if (types.Length == 0) return new("Writeu8(writer, 0);", "Readunit(reader); return default;", "writeu8(writer, 0);", "readunit(reader); return [];");
        var csWrite = types.Select((type, index) => WitWorkerValueCodecRequest.Write(type, $"value.Item{index + 1}"));
        var jsWrite = types.Select((type, index) => WitWorkerValueCodecRequest.Write(type, $"value[{index}]", true));
        var csRead = types.Select(type => WitWorkerValueCodecRequest.Read(type));
        var jsRead = types.Select(type => WitWorkerValueCodecRequest.Read(type, true));
        return new(string.Join("\n", csWrite), $"return {(types.Length == 1 ? $"new {request.Name}({string.Join(", ", csRead)})" : $"({string.Join(", ", csRead)})")};",
            string.Join("\n", jsWrite), $"return [{string.Join(", ", jsRead)}];");
    }
}

public sealed class WitWorkerTaggedValueCodecWriter(string kind) : IWitWorkerValueCodecWriter
{
    public WitWorkerValueCodecSource Write(WitWorkerValueCodecRequest request)
    {
        var cases = kind == "result"
            ? new[] { ("ok", request.Layout.Value.GetProperty("ok")), ("err", request.Layout.Value.GetProperty("err")) }
            : request.Layout.Value.GetProperty("cases").EnumerateArray()
                .Select(item => (item.GetProperty("name").GetString()!, item.GetProperty("type"))).ToArray();
        var csWrite = new List<string>(); var csRead = new List<string>();
        var jsWrite = new List<string>(); var jsRead = new List<string>();
        for (var index = 0; index < cases.Length; index++)
        {
            var (tag, type) = cases[index];
            var name = request.Identifier(tag);
            var hasValue = type.ValueKind != JsonValueKind.Null;
            var csPayload = hasValue ? WitWorkerValueCodecRequest.Write(type,
                kind == "result" ? "value." + (index == 0 ? "Ok" : "Error") : $"value.{name}Value") : "";
            var csReadPayload = hasValue ? WitWorkerValueCodecRequest.Read(type) : "default(WitUnit)";
            var csTest = kind == "result" ? (index == 0 ? "value.IsOk" : "!value.IsOk") : $"value.Tag == {request.Name}Tag.{name}";
            csWrite.Add($"if ({csTest}) {{ Writeu32(writer, {index}u); {csPayload} return; }}");
            csRead.Add($"case {index}u: return {request.Name}.{(kind == "result" ? index == 0 ? "FromOk" : "FromError" : name)}({(kind == "result" || hasValue ? csReadPayload : "")});");
            jsWrite.Add($"case {WitWorkerValueCodecRequest.Json(tag)}: writeu32(writer, {index}); {(hasValue ? WitWorkerValueCodecRequest.Write(type, "value.val", true) : "")} return;");
            var payload = hasValue ? WitWorkerValueCodecRequest.Read(type, true) : "undefined";
            jsRead.Add($"case {index}: return {{ tag: {WitWorkerValueCodecRequest.Json(tag)}{(hasValue || kind == "result" ? $", val: {payload}" : "")} }};");
        }
        return new(string.Join("\n", csWrite) + "\nthrow InvalidWire();",
            "switch (Readu32(reader)) { " + string.Join("\n", csRead) + " default: throw InvalidWire(); }",
            "switch (value.tag) { " + string.Join("\n", jsWrite) + " default: throw invalidWire(); }",
            "switch (readu32(reader)) { " + string.Join("\n", jsRead) + " default: throw invalidWire(); }");
    }
}

public sealed class WitWorkerEnumValueCodecWriter : IWitWorkerValueCodecWriter
{
    public WitWorkerValueCodecSource Write(WitWorkerValueCodecRequest request)
    {
        var cases = request.Layout.Value.GetProperty("cases").EnumerateArray()
            .Select(item => item.GetProperty("name").GetString()!).ToArray();
        var names = JsonSerializer.Serialize(cases);
        return new($"var tag = (uint)value; if (tag >= {cases.Length}u) throw InvalidWire(); Writeu32(writer, tag);",
            $"var tag = Readu32(reader); if (tag >= {cases.Length}u) throw InvalidWire(); return ({request.Name})tag;",
            $"const tag = {names}.indexOf(value); if (tag < 0) throw invalidWire(); writeu32(writer, tag);",
            $"const tag = readu32(reader); const names = {names}; if (tag >= names.length) throw invalidWire(); return names[tag];");
    }
}

public sealed class WitWorkerFlagsValueCodecWriter : IWitWorkerValueCodecWriter
{
    public WitWorkerValueCodecSource Write(WitWorkerValueCodecRequest request)
    {
        var flags = request.Layout.Value.GetProperty("flags").EnumerateArray()
            .Select(item => WitWorkerValueLayoutPlanner.MemberName(item.GetProperty("name").GetString()!)).ToArray();
        var mask = flags.Length == 32 ? uint.MaxValue : (1u << flags.Length) - 1u;
        var invalid = (~mask).ToString(CultureInfo.InvariantCulture);
        var jsWrite = flags.Select((flag, index) => $"if (value[{WitWorkerValueCodecRequest.Json(flag)}]) bits |= 1 << {index};");
        var jsRead = flags.Select((flag, index) => $"[{WitWorkerValueCodecRequest.Json(flag)}]: !!(bits & (1 << {index}))");
        return new($"var bits = (uint)value; if ((bits & {invalid}u) != 0) throw InvalidWire(); Writeu32(writer, bits);",
            $"var bits = Readu32(reader); if ((bits & {invalid}u) != 0) throw InvalidWire(); return ({request.Name})bits;",
            "let bits = 0; " + string.Join("\n", jsWrite) + " writeu32(writer, bits >>> 0);",
            $"const bits = readu32(reader); if ((bits & {invalid}) !== 0) throw invalidWire(); return {{ {string.Join(", ", jsRead)} }};");
    }
}

public static class WitWorkerValueCodecComposition
{
    public static ImmutableDictionary<string, IWitWorkerValueCodecWriter> CreateWriters() =>
        new Dictionary<string, IWitWorkerValueCodecWriter>(StringComparer.Ordinal)
        {
            ["type"] = new WitWorkerUnaryValueCodecWriter("type"),
            ["list"] = new WitWorkerUnaryValueCodecWriter("list"),
            ["option"] = new WitWorkerUnaryValueCodecWriter("option"),
            ["record"] = new WitWorkerRecordValueCodecWriter(),
            ["tuple"] = new WitWorkerTupleValueCodecWriter(),
            ["variant"] = new WitWorkerTaggedValueCodecWriter("variant"),
            ["result"] = new WitWorkerTaggedValueCodecWriter("result"),
            ["enum"] = new WitWorkerEnumValueCodecWriter(),
            ["flags"] = new WitWorkerFlagsValueCodecWriter(),
        }.ToImmutableDictionary(StringComparer.Ordinal);
}
