using System.Collections.Immutable;
using System.Text.Json;
using System.Globalization;
using NetWasm.Hosting.Deployment;

namespace NetWasm.Hosting.Build.JavaScript;

public sealed record WitWorkerValueSource(string JavaScript, string TypeScript);

public sealed record WitWorkerValueSourceRequest(
    int Id, WitWorkerValueLayout Layout, ImmutableDictionary<int, WitWorkerValueLayout> Layouts)
{
    public static string Json(string value) => JsonSerializer.Serialize(value);
    public static string Copy(WitWorkerValueReference reference, string expression) =>
        $"{(reference.Definition is { } id ? $"copyType{id.ToString(CultureInfo.InvariantCulture)}" : $"copy{reference.Primitive}")}({expression})";
    public string Type(WitWorkerValueReference reference) => reference.Definition is { } id
        ? Layouts[id].Name : reference.Primitive switch
        {
            "bool" => "boolean",
            "char" or "string" => "string",
            "s64" or "u64" => "bigint",
            _ => "number",
        };
    public bool Nullable(WitWorkerValueReference reference) =>
        reference.Definition is { } id && Layouts[id].Nullable;
    public static string Copy(JsonElement reference, string expression) =>
        Copy(WitWorkerValueLayoutPlanner.ReadReference(reference), expression);
    public string Type(JsonElement reference) => Type(WitWorkerValueLayoutPlanner.ReadReference(reference));
    public static string Member(string name) => WitWorkerValueLayoutPlanner.MemberName(name);
    public string Property(JsonElement reference, string name) =>
        Copy(reference, $"property(fields, {Json(name)}, {Nullable(WitWorkerValueLayoutPlanner.ReadReference(reference)).ToString().ToLowerInvariant()})");
}

public interface IWitWorkerValueSourceWriter
{
    WitWorkerValueSource Write(WitWorkerValueSourceRequest request);
}

public sealed class WitWorkerUnaryValueSourceWriter(string kind) : IWitWorkerValueSourceWriter
{
    public WitWorkerValueSource Write(WitWorkerValueSourceRequest request)
    {
        var target = WitWorkerValueLayoutPlanner.ReadReference(request.Layout.Value);
        var type = request.Type(target);
        if (kind == "type") return new($"return {WitWorkerValueSourceRequest.Copy(target, "value")};", type);
        if (kind == "list")
        {
            var numeric = WitWorkerValueLayoutPlanner.NumericArray(target, request.Layouts);
            return numeric is null
                ? new($"return arrayData(value).map(value => {WitWorkerValueSourceRequest.Copy(target, "value")});", $"Array<{type}>")
                : new($"return typedData(value, {WitWorkerValueSourceRequest.Json(numeric)}, {numeric});", numeric);
        }
        if (request.Nullable(target))
            return new($"const fields = objectData(value); const tag = property(fields, 'tag');\n"
                + $"if (tag === 'none') return {{ tag }};\n"
                + $"if (tag === 'some') return {{ tag, val: {WitWorkerValueSourceRequest.Copy(target, "property(fields, 'val', true)")} }};\n"
                + "throw invalidValue();", $"{{ tag: 'none' }} | {{ tag: 'some', val: {type} }}");
        return new($"return value === undefined ? undefined : {WitWorkerValueSourceRequest.Copy(target, "value")};", $"{type} | undefined");
    }
}

public sealed class WitWorkerRecordValueSourceWriter : IWitWorkerValueSourceWriter
{
    public WitWorkerValueSource Write(WitWorkerValueSourceRequest request)
    {
        var fields = request.Layout.Value.GetProperty("fields").EnumerateArray().ToArray();
        var names = fields.Select(field => WitWorkerValueSourceRequest.Member(field.GetProperty("name").GetString()!)).ToArray();
        if (names.Distinct(StringComparer.Ordinal).Count() != names.Length)
            throw new InvalidDataException("WIT record members collide in JavaScript");
        var js = new List<string>();
        var ts = new List<string>();
        for (var index = 0; index < fields.Length; index++)
        {
            var reference = fields[index].GetProperty("type");
            var name = WitWorkerValueSourceRequest.Json(names[index]);
            js.Add($"[{name}]: {request.Property(reference, names[index])}");
            ts.Add($"{name}{(request.Nullable(WitWorkerValueLayoutPlanner.ReadReference(reference)) ? "?" : "")}: {request.Type(reference)}");
        }
        return new($"const fields = objectData(value); return {{ {string.Join(", ", js)} }};",
            $"{{ {string.Join("; ", ts)} }}");
    }
}

public sealed class WitWorkerTupleValueSourceWriter : IWitWorkerValueSourceWriter
{
    public WitWorkerValueSource Write(WitWorkerValueSourceRequest request)
    {
        var types = request.Layout.Value.GetProperty("types").EnumerateArray().ToArray();
        var copies = types.Select((type, index) => WitWorkerValueSourceRequest.Copy(type, $"values[{index}]"));
        return new($"const values = arrayData(value, {types.Length}); return [{string.Join(", ", copies)}];",
            $"[{string.Join(", ", types.Select(request.Type))}]");
    }
}

public sealed class WitWorkerTaggedValueSourceWriter(string kind) : IWitWorkerValueSourceWriter
{
    public WitWorkerValueSource Write(WitWorkerValueSourceRequest request)
    {
        var cases = kind == "result"
            ? new[] { ("ok", request.Layout.Value.GetProperty("ok")), ("err", request.Layout.Value.GetProperty("err")) }
            : request.Layout.Value.GetProperty("cases").EnumerateArray()
                .Select(item => (item.GetProperty("name").GetString()!, item.GetProperty("type"))).ToArray();
        var js = new List<string> { "const fields = objectData(value); const tag = property(fields, 'tag'); switch (tag) {" };
        var ts = new List<string>();
        foreach (var (tag, type) in cases)
        {
            var name = WitWorkerValueSourceRequest.Json(tag);
            var hasValue = type.ValueKind != JsonValueKind.Null;
            var value = hasValue ? request.Property(type, "val") : "undefined";
            js.Add($"case {name}: return {{ tag{(hasValue || kind == "result" ? $", val: {value}" : "")} }};");
            ts.Add($"{{ tag: {name}{(hasValue || kind == "result" ? $", val: {(hasValue ? request.Type(type) : "void")}" : "")} }}");
        }
        js.Add("default: throw invalidValue(); }");
        return new(string.Join("\n", js), string.Join(" | ", ts));
    }
}

public sealed class WitWorkerEnumValueSourceWriter : IWitWorkerValueSourceWriter
{
    public WitWorkerValueSource Write(WitWorkerValueSourceRequest request)
    {
        var cases = request.Layout.Value.GetProperty("cases").EnumerateArray()
            .Select(item => WitWorkerValueSourceRequest.Json(item.GetProperty("name").GetString()!)).ToArray();
        return new($"if (![ {string.Join(", ", cases)} ].includes(value)) throw invalidValue(); return value;",
            string.Join(" | ", cases));
    }
}

public sealed class WitWorkerFlagsValueSourceWriter : IWitWorkerValueSourceWriter
{
    public WitWorkerValueSource Write(WitWorkerValueSourceRequest request)
    {
        var flags = request.Layout.Value.GetProperty("flags").EnumerateArray()
            .Select(item => WitWorkerValueSourceRequest.Member(item.GetProperty("name").GetString()!)).ToArray();
        if (flags.Distinct(StringComparer.Ordinal).Count() != flags.Length)
            throw new InvalidDataException("WIT flags collide in JavaScript");
        var js = flags.Select(flag => $"[{WitWorkerValueSourceRequest.Json(flag)}]: optionalBool(property(fields, {WitWorkerValueSourceRequest.Json(flag)}, true))");
        var ts = flags.Select(flag => $"{WitWorkerValueSourceRequest.Json(flag)}?: boolean");
        return new($"const fields = objectData(value); return {{ {string.Join(", ", js)} }};", $"{{ {string.Join("; ", ts)} }}");
    }
}

public static class WitWorkerValueSourceComposition
{
    public static ImmutableDictionary<string, IWitWorkerValueSourceWriter> CreateWriters() =>
        new Dictionary<string, IWitWorkerValueSourceWriter>(StringComparer.Ordinal)
        {
            ["type"] = new WitWorkerUnaryValueSourceWriter("type"),
            ["list"] = new WitWorkerUnaryValueSourceWriter("list"),
            ["option"] = new WitWorkerUnaryValueSourceWriter("option"),
            ["record"] = new WitWorkerRecordValueSourceWriter(),
            ["tuple"] = new WitWorkerTupleValueSourceWriter(),
            ["variant"] = new WitWorkerTaggedValueSourceWriter("variant"),
            ["result"] = new WitWorkerTaggedValueSourceWriter("result"),
            ["enum"] = new WitWorkerEnumValueSourceWriter(),
            ["flags"] = new WitWorkerFlagsValueSourceWriter(),
        }.ToImmutableDictionary(StringComparer.Ordinal);
}
