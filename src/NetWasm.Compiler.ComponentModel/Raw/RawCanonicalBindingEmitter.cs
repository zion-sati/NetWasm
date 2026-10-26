using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace NetWasm.Compiler.ComponentModel.Raw;

/// <summary>
/// Emits each callable import's canonical-ABI lifting and lowering as
/// specialized JavaScript, so the host runs no generic interpreter. The code
/// calls the helpers in raw-adapter-runtime.mjs and follows the canonical ABI
/// as that interpreter did: arguments lift before owned handles transfer, a
/// result's resources register before its sequences allocate, and a failed
/// lowering frees and releases what it took.
/// </summary>
internal sealed class RawCanonicalBindingEmitter
{
    private readonly bool _wasm64;
    private readonly RawAdapterValuePlanner _planner;
    private readonly StringBuilder _functions = new();
    private readonly Dictionary<string, string> _names = new(StringComparer.Ordinal);
    private int _next;

    public RawCanonicalBindingEmitter(bool wasm64)
    {
        _wasm64 = wasm64;
        _planner = new RawAdapterValuePlanner(wasm64 ? 8 : 4);
    }

    /// <summary>The shared lifting and lowering functions the emitted bindings call.</summary>
    public string Functions => _functions.ToString();

    private string Zero => _wasm64 ? "0n" : "0";

    /// <summary>Emits <c>function bindCallable{index}(host)</c> for one callable binding plan.</summary>
    public string EmitCallable(int index, JsonElement binding)
    {
        var provider = binding.GetProperty("provider");
        var parameters = binding.GetProperty("parameters").EnumerateArray()
            .Select(parameter => RawAdapterValueType.Read(parameter.GetProperty("type")))
            .ToImmutableArray();
        var result = RawAdapterValueType.ReadOptional(binding.GetProperty("result"));
        var signature = binding.GetProperty("canonicalSignature");
        var arity = signature.GetProperty("parameters").GetArrayLength();
        var indirectParameters = signature.GetProperty("indirectParameters").GetBoolean();
        // Without a result there is no return area to write.
        var indirectResult = result is not null && signature.GetProperty("indirectResult").GetBoolean();
        var flatParameters = signature.GetProperty("flatParameters").EnumerateArray()
            .Select(value => value.GetString()!).ToImmutableArray();
        var arguments = Enumerable.Range(0, arity).Select(argument => $"a{Int(argument)}").ToArray();

        var source = new StringBuilder();
        source.Append("function bindCallable").Append(Int(index)).AppendLine("(host) {");
        source.Append("  const invoke = ").Append(Provider(provider)).AppendLine(";");
        source.Append("  return function (").Append(string.Join(", ", arguments)).AppendLine(") {");
        source.AppendLine("    const memory = host.memory();");
        source.AppendLine("    const store = host.store();");
        if (indirectResult)
        {
            var layout = _planner.Layout(result!);
            source.Append("    const out = rcIndex(memory, ").Append(arguments[^1]).Append(", ")
                .Append(Int(layout.Alignment)).Append(", ").Append(Int(layout.Size)).AppendLine(");");
        }

        var lifted = parameters.Any(HoldsResource);
        source.AppendLine(lifted
            ? "    const lift = rcLiftTransaction(host, store);"
            : "    const lift = undefined;");
        var names = new List<string>(parameters.Length);
        if (indirectParameters)
        {
            var tuple = RawAdapterValueType.Tuple(parameters);
            var layout = _planner.Layout(tuple);
            source.Append("    const parameters = ").Append(Reader(tuple))
                .Append("(memory, rcIndex(memory, a0, ").Append(Int(layout.Alignment)).Append(", ")
                .Append(Int(layout.Size)).AppendLine("), lift);");
            names.AddRange(parameters.Select((_, parameter) => $"parameters[{Int(parameter)}]"));
        }
        else
        {
            var slots = new Queue<Slot>(flatParameters.Select((core, slot) => new Slot(arguments[slot], core)));
            for (var parameter = 0; parameter < parameters.Length; parameter++)
            {
                var expected = _planner.Flatten(parameters[parameter]);
                if (!slots.Take(expected.Length).Select(slot => slot.Core).SequenceEqual(expected))
                {
                    throw new InvalidOperationException("raw adapter flat parameters disagree with their types");
                }
                source.Append("    const p").Append(Int(parameter)).Append(" = ")
                    .Append(LiftFlat(parameters[parameter], slots)).AppendLine(";");
                names.Add($"p{Int(parameter)}");
            }
            if (slots.Count != 0) throw new InvalidOperationException("raw adapter flat parameters disagree with their types");
        }
        if (lifted) source.AppendLine("    lift.commit();");

        var call = $"invoke({string.Join(", ", names)})";
        if (result is null)
        {
            source.Append("    ").Append(call).AppendLine(";");
            source.AppendLine("    return undefined;");
        }
        else
        {
            if (result.Kind == "result")
            {
                // A provider returns a result's ok payload and throws its error.
                source.AppendLine("    let value;");
                source.Append("    try { value = rcTaggedResult(\"ok\", ").Append(Bool(result.Ok is not null))
                    .Append(", ").Append(call).AppendLine("); }");
                source.Append("    catch (error) { value = rcTaggedResult(\"err\", ").Append(Bool(result.Error is not null))
                    .AppendLine(", rcErrorPayload(error)); }");
            }
            else
            {
                source.Append("    const value = ").Append(call).AppendLine(";");
            }
            EmitLowering(source, result, indirectResult);
        }
        source.AppendLine("  };");
        source.AppendLine("}");
        return source.ToString();
    }

    private void EmitLowering(StringBuilder source, RawAdapterValueType result, bool indirect)
    {
        var flat = indirect ? 0 : _planner.Flatten(result).Length;
        if (!indirect && flat > 1)
        {
            throw new InvalidOperationException("raw adapter flat results carry more than one core value");
        }
        var transactional = HoldsResource(result) || result.Allocates;
        var indent = transactional ? "      " : "    ";
        if (transactional)
        {
            source.Append("    const tx = rcLowerTransaction(host, memory, store, ").Append(Bool(_wasm64)).AppendLine(");");
            source.AppendLine("    try {");
        }
        else
        {
            source.AppendLine("    const tx = undefined;");
        }
        source.Append(indent).Append("const plan = ").Append(Plan(result, "value")).AppendLine(";");
        if (transactional) source.Append(indent).AppendLine("tx.register();");
        if (result.Allocates) source.Append(indent).Append(Materializer(result)).AppendLine("(memory, plan, tx);");
        string returned;
        if (indirect)
        {
            source.Append(indent).Append(Write(result, "out", "plan")).AppendLine();
            returned = "undefined";
        }
        else
        {
            returned = flat == 0 ? "undefined" : FlatResult(result, "plan");
        }
        if (transactional)
        {
            source.Append(indent).Append("const core = ").Append(returned).AppendLine(";");
            source.Append(indent).AppendLine("tx.commit();");
            source.Append(indent).AppendLine("return core;");
            source.AppendLine("    } catch (error) {");
            source.AppendLine("      tx.rollback(error);");
            source.AppendLine("    }");
        }
        else
        {
            source.Append("    return ").Append(returned).AppendLine(";");
        }
    }

    private static string Provider(JsonElement provider)
    {
        var interfaceName = Json(provider.GetProperty("interface").GetString()!);
        var name = Json(provider.GetProperty("javascriptName").GetString()!);
        var kind = provider.GetProperty("functionKind").GetString();
        if (kind == "freestanding") return $"rcFunction(host.providers, {interfaceName}, {name})";
        var resource = provider.GetProperty("resourceJavaScriptName");
        if (resource.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException("raw adapter resource provider has no JavaScript class");
        }
        var resourceName = Json(resource.GetString()!);
        return kind switch
        {
            "constructor" when name == resourceName => $"rcConstructor(host.providers, {interfaceName}, {resourceName})",
            "static" => $"rcStatic(host.providers, {interfaceName}, {resourceName}, {name})",
            "method" => $"rcMethod(host.providers, {interfaceName}, {resourceName}, {name})",
            _ => throw new InvalidOperationException($"raw adapter provider kind '{kind}' is unsupported"),
        };
    }

    // Lifting flat core values, which a WebAssembly call has already typed.
    private string LiftFlat(RawAdapterValueType type, Queue<Slot> slots)
    {
        switch (type.Kind)
        {
            case "unit": return "undefined";
            case "bool": return $"({slots.Dequeue().Name} !== 0)";
            case "s8": return $"({slots.Dequeue().Name} << 24 >> 24)";
            case "u8": return $"({slots.Dequeue().Name} & 0xff)";
            case "s16": return $"({slots.Dequeue().Name} << 16 >> 16)";
            case "u16": return $"({slots.Dequeue().Name} & 0xffff)";
            case "s32": return $"({slots.Dequeue().Name} | 0)";
            case "u32": return $"({slots.Dequeue().Name} >>> 0)";
            case "s64": return $"BigInt.asIntN(64, {slots.Dequeue().Name})";
            case "u64": return $"BigInt.asUintN(64, {slots.Dequeue().Name})";
            case "f32": return $"Math.fround({slots.Dequeue().Name})";
            case "f64": return slots.Dequeue().Name;
            case "character": return $"rcCharacter({slots.Dequeue().Name} >>> 0)";
            case "alias": return LiftFlat(type.Element!, slots);
            case "text":
                return $"rcReadText(memory, {slots.Dequeue().Name}, {slots.Dequeue().Name})";
            case "list":
                return type.Element!.Underlying.Kind == "u8"
                    ? $"rcReadBytes(memory, {slots.Dequeue().Name}, {slots.Dequeue().Name})"
                    : $"{ListReader(type)}(memory, {slots.Dequeue().Name}, {slots.Dequeue().Name}, lift)";
            case "record":
                return "{ " + string.Join(", ", type.Fields.Select(field =>
                    $"{Json(field.JavaScriptName!)}: {LiftFlat(field.Type, slots)}")) + " }";
            case "tuple":
                return "[" + string.Join(", ", type.Fields.Select(field => LiftFlat(field.Type, slots))) + "]";
            case "option" or "result" or "variant":
            {
                var count = 1 + _planner.JoinedSlots(type.SelectionTypes).Length;
                var values = Enumerable.Range(0, count).Select(_ => slots.Dequeue().Name);
                return $"{FlatVariantLifter(type)}(memory, lift, {string.Join(", ", values)})";
            }
            case "enum": return $"rcEnumCase({Names(type.Cases.Select(item => item.Name))}, {slots.Dequeue().Name} >>> 0)";
            case "flags":
            {
                var words = _planner.Flatten(type).Select(_ => $"{slots.Dequeue().Name} >>> 0");
                return $"rcFlags({Names(type.Flags)}, [{string.Join(", ", words)}])";
            }
            case "owned-resource": return $"lift.own({Int(type.ResourceType)}, {slots.Dequeue().Name} >>> 0)";
            case "borrowed-resource": return $"lift.borrow({Int(type.ResourceType)}, {slots.Dequeue().Name} >>> 0)";
            default: throw Unsupported(type);
        }
    }

    // liftN(memory, lift, discriminant, ...joined slots) for an option, result or variant.
    private string FlatVariantLifter(RawAdapterValueType type) => Shared("lift", type, name =>
    {
        var caseTypes = type.SelectionTypes;
        var joined = _planner.JoinedSlots(caseTypes);
        var slotNames = joined.Select((_, slot) => $"s{Int(slot)}").ToArray();
        var body = new StringBuilder();
        body.Append("function ").Append(name).Append("(memory, lift, d")
            .Append(string.Concat(slotNames.Select(slot => ", " + slot))).AppendLine(") {");
        body.AppendLine("  switch (d >>> 0) {");
        for (var index = 0; index < caseTypes.Length; index++)
        {
            string? payload = null;
            if (caseTypes[index] is { } caseType)
            {
                var caseSlots = _planner.Flatten(caseType);
                var slots = new Queue<Slot>(caseSlots.Select((core, slot) =>
                    new Slot(Coerce(slotNames[slot], joined[slot], core), core)));
                payload = LiftFlat(caseType, slots);
            }
            body.Append("    case ").Append(Int(index)).Append(": return ")
                .Append(Selected(type, index, payload)).AppendLine(";");
        }
        body.AppendLine("    default: throw new TypeError(\"raw canonical variant discriminant is invalid\");");
        body.AppendLine("  }");
        body.AppendLine("}");
        return body.ToString();
    });

    private static string Coerce(string value, string joined, string selected) => (joined, selected) switch
    {
        _ when joined == selected => value,
        ("i32", "f32") => $"rcF32FromI32({value})",
        ("i64", "i32") => $"rcI32FromI64({value})",
        ("i64", "f32") => $"rcF32FromI64({value})",
        ("i64", "f64") => $"rcF64FromI64({value})",
        ("f64", "f32") => $"Math.fround({value})",
        _ => throw new InvalidOperationException($"raw adapter cannot coerce {joined} to {selected}"),
    };

    // A lifted option, result or variant case: none is undefined unless options nest.
    private static string Selected(RawAdapterValueType type, int index, string? payload)
    {
        if (type.Kind == "option" && type.Element!.Underlying.Kind != "option")
        {
            return index == 0 ? "undefined" : payload!;
        }
        var tag = type.Kind switch
        {
            "option" => index == 0 ? "none" : "some",
            "result" => index == 0 ? "ok" : "err",
            _ => type.Cases[index].Name,
        };
        return payload is null ? $"{{ tag: {Json(tag)} }}" : $"{{ tag: {Json(tag)}, val: {payload} }}";
    }

    // An expression reading a value of `type` at byte `index` of linear memory.
    private string Read(RawAdapterValueType type, string index) => type.Kind switch
    {
        "unit" => "undefined",
        "bool" => $"(rcView(memory).getUint8({index}) !== 0)",
        "s8" => $"rcView(memory).getInt8({index})",
        "u8" => $"rcView(memory).getUint8({index})",
        "s16" => $"rcView(memory).getInt16({index}, true)",
        "u16" => $"rcView(memory).getUint16({index}, true)",
        "s32" => $"rcView(memory).getInt32({index}, true)",
        "u32" => $"rcView(memory).getUint32({index}, true)",
        "s64" => $"rcView(memory).getBigInt64({index}, true)",
        "u64" => $"rcView(memory).getBigUint64({index}, true)",
        "f32" => $"rcView(memory).getFloat32({index}, true)",
        "f64" => $"rcView(memory).getFloat64({index}, true)",
        "character" => $"rcCharacter(rcView(memory).getUint32({index}, true))",
        "alias" => Read(type.Element!, index),
        "owned-resource" => $"lift.own({Int(type.ResourceType)}, rcView(memory).getUint32({index}, true))",
        "borrowed-resource" => $"lift.borrow({Int(type.ResourceType)}, rcView(memory).getUint32({index}, true))",
        _ => $"{Reader(type)}(memory, {index}, lift)",
    };

    // readN(memory, index, lift) for a value that is not a scalar.
    private string Reader(RawAdapterValueType type) => type.Kind == "alias" ? Reader(type.Element!) : Shared("read", type, name =>
    {
        var body = new StringBuilder();
        body.Append("function ").Append(name).AppendLine("(memory, index, lift) {");
        var layout = _planner.Layout(type);
        switch (type.Kind)
        {
            case "text":
                body.Append("  return rcReadText(memory, ").Append(PointerRead("index")).Append(", ")
                    .Append(PointerRead($"index + {Int(_planner.AddressSize)}")).AppendLine(");");
                break;
            case "list" when type.Element!.Underlying.Kind == "u8":
                body.Append("  return rcReadBytes(memory, ").Append(PointerRead("index")).Append(", ")
                    .Append(PointerRead($"index + {Int(_planner.AddressSize)}")).AppendLine(");");
                break;
            case "list":
                body.Append("  return ").Append(ListReader(type)).Append("(memory, ").Append(PointerRead("index"))
                    .Append(", ").Append(PointerRead($"index + {Int(_planner.AddressSize)}")).AppendLine(", lift);");
                break;
            case "record":
                body.Append("  return { ").Append(string.Join(", ", type.Fields.Select((field, item) =>
                    $"{Json(field.JavaScriptName!)}: {Read(field.Type, Offset("index", layout.FieldOffsets[item]))}")))
                    .AppendLine(" };");
                break;
            case "tuple":
                body.Append("  return [").Append(string.Join(", ", type.Fields.Select((field, item) =>
                    Read(field.Type, Offset("index", layout.FieldOffsets[item]))))).AppendLine("];");
                break;
            case "option" or "result" or "variant":
            {
                var caseTypes = type.SelectionTypes;
                body.Append("  switch (").Append(Discriminant(layout.DiscriminantSize, "index")).AppendLine(") {");
                for (var item = 0; item < caseTypes.Length; item++)
                {
                    var payload = caseTypes[item] is { } caseType
                        ? Read(caseType, Offset("index", layout.PayloadOffset))
                        : null;
                    body.Append("    case ").Append(Int(item)).Append(": return ")
                        .Append(Selected(type, item, payload)).AppendLine(";");
                }
                body.AppendLine("    default: throw new TypeError(\"raw canonical variant discriminant is invalid\");");
                body.AppendLine("  }");
                break;
            }
            case "enum":
                body.Append("  return rcEnumCase(").Append(Names(type.Cases.Select(item => item.Name))).Append(", ")
                    .Append(Discriminant(layout.Size, "index")).AppendLine(");");
                break;
            case "flags":
            {
                var words = layout.Size switch
                {
                    1 => "rcView(memory).getUint8(index)",
                    2 => "rcView(memory).getUint16(index, true)",
                    _ => string.Join(", ", Enumerable.Range(0, layout.Size / 4)
                        .Select(word => $"rcView(memory).getUint32({Offset("index", word * 4)}, true)")),
                };
                body.Append("  return rcFlags(").Append(Names(type.Flags)).Append(", [").Append(words).AppendLine("]);");
                break;
            }
            default:
                throw Unsupported(type);
        }
        body.AppendLine("}");
        return body.ToString();
    });

    // listN(memory, address, length, lift) for a list of anything but bytes.
    private string ListReader(RawAdapterValueType type) => Shared("list", type, name =>
    {
        var element = _planner.Layout(type.Element!);
        var body = new StringBuilder();
        body.Append("function ").Append(name).AppendLine("(memory, address, rawLength, lift) {");
        body.AppendLine("  const length = rcSequenceLength(rawLength, \"list\");");
        body.Append("  const byteLength = rcListByteLength(").Append(Int(element.Size)).AppendLine(", length);");
        body.Append("  const base = rcIndex(memory, address, ").Append(Int(element.Alignment)).AppendLine(", byteLength);");
        body.AppendLine("  const items = new Array(length);");
        body.Append("  for (let item = 0; item < length; item++) items[item] = ")
            .Append(Read(type.Element!, $"base + item * {Int(element.Size)}")).AppendLine(";");
        body.AppendLine("  return items;");
        body.AppendLine("}");
        return body.ToString();
    });

    // An expression validating a host value of `type` into its lowering plan.
    private string Plan(RawAdapterValueType type, string value) => type.Kind switch
    {
        "unit" => $"rcUnit({value})",
        "bool" => $"rcBool({value})",
        "s8" => $"rcInteger({value}, -128, 127, \"s8\")",
        "u8" => $"rcInteger({value}, 0, 255, \"u8\")",
        "s16" => $"rcInteger({value}, -32768, 32767, \"s16\")",
        "u16" => $"rcInteger({value}, 0, 65535, \"u16\")",
        "s32" => $"rcInteger({value}, -2147483648, 2147483647, \"s32\")",
        "u32" => $"rcInteger({value}, 0, 4294967295, \"u32\")",
        "s64" => $"rcBigInteger({value}, -9223372036854775808n, 9223372036854775807n, \"s64\")",
        "u64" => $"rcBigInteger({value}, 0n, 18446744073709551615n, \"u64\")",
        "f32" => $"rcFloat32({value})",
        "f64" => $"rcFloat64({value})",
        "character" => $"rcCodePoint({value})",
        "text" => $"{{ bytes: rcEncodeText({value}), address: {Zero} }}",
        "list" when type.Element!.Underlying.Kind == "u8" => $"{{ bytes: rcByteList({value}), address: {Zero} }}",
        "alias" => Plan(type.Element!, value),
        "enum" => $"rcEnumIndex({value}, {Names(type.Cases.Select(item => item.Name))})",
        "flags" => $"rcFlagWords({value}, {Names(type.Flags)}, {Int(_planner.Flatten(type).Length)})",
        "owned-resource" => $"tx.acquire({Int(type.ResourceType)}, {value})",
        "borrowed-resource" => "rcBorrowedResult()",
        _ => $"{Planner(type)}({value}, tx)",
    };

    // planN(value, tx) for a list, record, tuple, option, result or variant.
    private string Planner(RawAdapterValueType type) => Shared("plan", type, name =>
    {
        var body = new StringBuilder();
        body.Append("function ").Append(name).AppendLine("(value, tx) {");
        switch (type.Kind)
        {
            case "list":
            {
                var element = type.Element!.Underlying.Kind;
                var snapshot = NumericListArrays.TryGetValue(element, out var typedArray)
                    ? $"rcNumberList(value, {typedArray})"
                    : "rcArray(value, undefined, \"list\")";
                body.Append("  const values = ").Append(snapshot).AppendLine(";");
                body.Append("  const byteLength = rcLoweredListByteLength(")
                    .Append(Int(_planner.Layout(type.Element!).Size)).AppendLine(", values.length);");
                body.Append("  return { items: values.map(item => (").Append(Plan(type.Element!, "item"))
                    .Append(")), byteLength, length: values.length, address: ").Append(Zero).AppendLine(" };");
                break;
            }
            case "record":
                body.Append("  const values = rcObject(value, ")
                    .Append(Names(type.Fields.Select(field => field.JavaScriptName!))).AppendLine(", \"record\");");
                body.Append("  return [").Append(string.Join(", ", type.Fields.Select((field, item) =>
                    Plan(field.Type, $"values[{Int(item)}]")))).AppendLine("];");
                break;
            case "tuple":
                body.Append("  const values = rcArray(value, ").Append(Int(type.Fields.Length)).AppendLine(", \"tuple\");");
                body.Append("  return [").Append(string.Join(", ", type.Fields.Select((field, item) =>
                    Plan(field.Type, $"values[{Int(item)}]")))).AppendLine("];");
                break;
            case "option" when type.Element!.Underlying.Kind != "option":
                body.Append("  return value === undefined ? { index: 0 } : { index: 1, payload: ")
                    .Append(Plan(type.Element!, "value")).AppendLine(" };");
                break;
            case "option" or "result" or "variant":
            {
                var caseTypes = type.SelectionTypes;
                var tags = type.Kind switch
                {
                    "option" => ["none", "some"],
                    "result" => ["ok", "err"],
                    _ => type.Cases.Select(item => item.Name).ToArray(),
                };
                body.Append("  const selected = rcTagged(value, ").Append(Names(tags)).Append(", [")
                    .Append(string.Join(", ", caseTypes.Select(caseType => Bool(caseType is not null))))
                    .Append("], ").Append(Json(type.Kind)).AppendLine(");");
                body.AppendLine("  switch (selected.index) {");
                for (var item = 0; item < caseTypes.Length; item++)
                {
                    body.Append("    case ").Append(Int(item)).Append(": return ");
                    if (caseTypes[item] is { } caseType)
                    {
                        body.Append("{ index: ").Append(Int(item)).Append(", payload: ")
                            .Append(Plan(caseType, "selected.value")).AppendLine(" };");
                    }
                    else
                    {
                        body.Append("{ index: ").Append(Int(item)).AppendLine(" };");
                    }
                }
                body.AppendLine("  }");
                break;
            }
            default:
                throw Unsupported(type);
        }
        body.AppendLine("}");
        return body.ToString();
    });

    // materializeN(memory, plan, tx): allocates a plan's sequences and fills those of a list.
    private string Materializer(RawAdapterValueType type) => type.Kind == "alias" ? Materializer(type.Element!) : Shared("materialize", type, name =>
    {
        var body = new StringBuilder();
        body.Append("function ").Append(name).AppendLine("(memory, plan, tx) {");
        switch (type.Kind)
        {
            case "text":
            case "list" when type.Element!.Underlying.Kind == "u8":
                body.AppendLine("  const length = plan.bytes.length;");
                body.AppendLine("  plan.address = tx.allocate(1, length);");
                body.AppendLine("  if (length !== 0) new Uint8Array(memory.buffer, rcIndex(memory, plan.address, 1, length), length).set(plan.bytes);");
                break;
            case "list":
            {
                var element = _planner.Layout(type.Element!);
                body.Append("  plan.address = tx.allocate(").Append(Int(element.Alignment)).AppendLine(", plan.byteLength);");
                if (type.Element!.Allocates)
                {
                    body.Append("  for (const item of plan.items) ").Append(Materializer(type.Element!)).AppendLine("(memory, item, tx);");
                }
                body.AppendLine("  if (plan.byteLength !== 0) {");
                body.Append("    const base = rcIndex(memory, plan.address, ").Append(Int(element.Alignment)).AppendLine(", plan.byteLength);");
                body.Append("    for (let item = 0; item < plan.length; item++) ")
                    .AppendLine(Write(type.Element!, $"base + item * {Int(element.Size)}", "plan.items[item]"));
                body.AppendLine("  }");
                break;
            }
            case "record" or "tuple":
                for (var item = 0; item < type.Fields.Length; item++)
                {
                    if (!type.Fields[item].Type.Allocates) continue;
                    body.Append("  ").Append(Materializer(type.Fields[item].Type))
                        .Append("(memory, plan[").Append(Int(item)).AppendLine("], tx);");
                }
                break;
            case "option" or "result" or "variant":
            {
                var caseTypes = type.SelectionTypes;
                for (var item = 0; item < caseTypes.Length; item++)
                {
                    if (caseTypes[item]?.Allocates != true) continue;
                    body.Append("  if (plan.index === ").Append(Int(item)).Append(") ")
                        .Append(Materializer(caseTypes[item]!)).AppendLine("(memory, plan.payload, tx);");
                }
                break;
            }
            default:
                throw Unsupported(type);
        }
        body.AppendLine("}");
        return body.ToString();
    });

    // A statement writing a lowering plan of `type` at byte `index` of linear memory.
    private string Write(RawAdapterValueType type, string index, string plan) => type.Kind switch
    {
        "unit" => ";",
        "bool" or "u8" => $"rcView(memory).setUint8({index}, {plan});",
        "s8" => $"rcView(memory).setInt8({index}, {plan});",
        "s16" => $"rcView(memory).setInt16({index}, {plan}, true);",
        "u16" => $"rcView(memory).setUint16({index}, {plan}, true);",
        "s32" => $"rcView(memory).setInt32({index}, {plan}, true);",
        "u32" or "character" => $"rcView(memory).setUint32({index}, {plan}, true);",
        "s64" => $"rcView(memory).setBigInt64({index}, {plan}, true);",
        "u64" => $"rcView(memory).setBigUint64({index}, {plan}, true);",
        "f32" => $"rcView(memory).setFloat32({index}, {plan}, true);",
        "f64" => $"rcView(memory).setFloat64({index}, {plan}, true);",
        "alias" => Write(type.Element!, index, plan),
        "enum" => $"{DiscriminantWrite(_planner.Layout(type).Size, index, plan)};",
        "owned-resource" => $"rcView(memory).setUint32({index}, {plan}.handle, true);",
        // Planning a borrowed handle as a result already threw.
        "borrowed-resource" => ";",
        _ => $"{Writer(type)}(memory, {index}, {plan});",
    };

    // writeN(memory, index, plan) for a value that is not a scalar.
    private string Writer(RawAdapterValueType type) => Shared("write", type, name =>
    {
        var body = new StringBuilder();
        body.Append("function ").Append(name).AppendLine("(memory, index, plan) {");
        var layout = _planner.Layout(type);
        switch (type.Kind)
        {
            case "text" or "list":
            {
                var length = type.Kind == "text" || type.Element!.Underlying.Kind == "u8" ? "plan.bytes.length" : "plan.length";
                body.Append("  ").Append(PointerWrite("index", "plan.address")).AppendLine(";");
                body.Append("  ").Append(PointerWrite($"index + {Int(_planner.AddressSize)}", length)).AppendLine(";");
                break;
            }
            case "record" or "tuple":
                for (var item = 0; item < type.Fields.Length; item++)
                {
                    body.Append("  ").AppendLine(Write(type.Fields[item].Type,
                        Offset("index", layout.FieldOffsets[item]), $"plan[{Int(item)}]"));
                }
                break;
            case "option" or "result" or "variant":
            {
                var caseTypes = type.SelectionTypes;
                for (var item = 0; item < caseTypes.Length; item++)
                {
                    if (caseTypes[item] is not { } caseType) continue;
                    body.Append("  if (plan.index === ").Append(Int(item)).Append(") ")
                        .AppendLine(Write(caseType, Offset("index", layout.PayloadOffset), "plan.payload"));
                }
                body.Append("  ").Append(DiscriminantWrite(layout.DiscriminantSize, "index", "plan.index")).AppendLine(";");
                break;
            }
            case "flags":
                if (layout.Size == 1) body.AppendLine("  rcView(memory).setUint8(index, plan[0]);");
                else if (layout.Size == 2) body.AppendLine("  rcView(memory).setUint16(index, plan[0], true);");
                else
                {
                    body.AppendLine("  for (let word = 0; word < plan.length; word++) rcView(memory).setUint32(index + word * 4, plan[word], true);");
                }
                break;
            default:
                throw Unsupported(type);
        }
        body.AppendLine("}");
        return body.ToString();
    });

    // The single core value of a flat result's lowering plan.
    private string FlatResult(RawAdapterValueType type, string plan) => type.Kind switch
    {
        "bool" or "s8" or "u8" or "s16" or "u16" or "s32" or "u32" or "character" => $"{plan} | 0",
        "s64" or "u64" => $"BigInt.asIntN(64, {plan})",
        "f32" or "f64" or "enum" => plan,
        "flags" => $"{plan}[0] | 0",
        "owned-resource" => $"{plan}.handle | 0",
        "borrowed-resource" => "0",
        "alias" => FlatResult(type.Element!, plan),
        "record" or "tuple" => type.Fields
            .Select((field, item) => (field, item))
            .Where(entry => _planner.Flatten(entry.field.Type).Length == 1)
            .Select(entry => FlatResult(entry.field.Type, $"{plan}[{Int(entry.item)}]"))
            .Single(),
        "option" or "result" or "variant" => $"{plan}.index",
        _ => throw Unsupported(type),
    };

    private string PointerRead(string index) => _wasm64
        ? $"rcView(memory).getBigUint64({index}, true)"
        : $"rcView(memory).getUint32({index}, true)";

    private string PointerWrite(string index, string value) => _wasm64
        ? $"rcView(memory).setBigUint64({index}, BigInt.asUintN(64, BigInt({value})), true)"
        : $"rcView(memory).setUint32({index}, {value} >>> 0, true)";

    private static string Discriminant(int size, string index) => size switch
    {
        1 => $"rcView(memory).getUint8({index})",
        2 => $"rcView(memory).getUint16({index}, true)",
        _ => $"rcView(memory).getUint32({index}, true)",
    };

    private static string DiscriminantWrite(int size, string index, string value) => size switch
    {
        1 => $"rcView(memory).setUint8({index}, {value})",
        2 => $"rcView(memory).setUint16({index}, {value}, true)",
        _ => $"rcView(memory).setUint32({index}, {value}, true)",
    };

    private static bool HoldsResource(RawAdapterValueType type) => type.Kind switch
    {
        "owned-resource" or "borrowed-resource" => true,
        "alias" or "list" or "option" => HoldsResource(type.Element!),
        "record" or "tuple" => type.Fields.Any(field => HoldsResource(field.Type)),
        "result" => type.Ok is { } ok && HoldsResource(ok) || type.Error is { } error && HoldsResource(error),
        "variant" => type.Cases.Any(item => item.Type is { } payload && HoldsResource(payload)),
        _ => false,
    };

    private string Shared(string role, RawAdapterValueType type, Func<string, string> emit)
    {
        var key = $"{role}:{type.Key}";
        if (_names.TryGetValue(key, out var existing)) return existing;
        var name = $"{role}{Int(_next++)}";
        _names.Add(key, name);
        _functions.AppendLine().Append(emit(name));
        return name;
    }

    private static readonly Dictionary<string, string> NumericListArrays = new(StringComparer.Ordinal)
    {
        ["s8"] = "Int8Array",
        ["u16"] = "Uint16Array",
        ["s16"] = "Int16Array",
        ["u32"] = "Uint32Array",
        ["s32"] = "Int32Array",
        ["u64"] = "BigUint64Array",
        ["s64"] = "BigInt64Array",
        ["f32"] = "Float32Array",
        ["f64"] = "Float64Array",
    };

    private static string Offset(string index, int offset) => offset == 0 ? index : $"{index} + {Int(offset)}";

    private static string Names(IEnumerable<string> names) => JsonSerializer.Serialize(names);

    private static string Json(string value) => JsonSerializer.Serialize(value);

    private static string Bool(bool value) => value ? "true" : "false";

    private static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static InvalidOperationException Unsupported(RawAdapterValueType type) =>
        new($"Raw adapter type '{type.Kind}' cannot be emitted here.");

    private sealed record Slot(string Name, string Core);
}
