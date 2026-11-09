using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using NetWasm.Compiler.ComponentModel.Worlds;

namespace NetWasm.Wit.Bindings;

public interface IWitBindingIntrinsicsSectionWriter
{
    string Generate(WitDocument document, WitWorld world);
}

// The built-ins of each stream and future type the world's functions use, as the CoreLib
// intrinsics classes generated lifting and lowering hand them to. wit-component names a
// function's built-ins by the position of the type among the function's streams and futures
// (parameters then result, each type after the ones nested in it); built-ins of the same type
// are interchangeable, so each type uses the first function, an import's before an export's.
public sealed class WitBindingIntrinsicsSectionWriter(
    IWitCanonicalTypeResolver types,
    NetWasm.Compiler.Core.ICanonicalAbiMemoryLayoutPlanner layouts,
    IWitBindingSyntaxFormatter syntax,
    ICodeWriterFactory writers) : IWitBindingIntrinsicsSectionWriter
{
    private readonly IWitCanonicalTypeResolver _types = types ??
        throw new ArgumentNullException(nameof(types));
    private readonly NetWasm.Compiler.Core.ICanonicalAbiMemoryLayoutPlanner _layouts = layouts ??
        throw new ArgumentNullException(nameof(layouts));
    private readonly IWitBindingSyntaxFormatter _syntax = syntax ??
        throw new ArgumentNullException(nameof(syntax));
    private readonly ICodeWriterFactory _writers = writers ??
        throw new ArgumentNullException(nameof(writers));

    public string Generate(WitDocument document, WitWorld world)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(world);
        var sources = new Dictionary<int, (string Module, string Function, int Index)>();
        foreach (var (module, function) in Functions(document, world))
        {
            var found = WitPayloadTypes.Find(document, function);
            for (var index = 0; index < found.Length; index++)
            {
                sources.TryAdd(found[index], (module, function.Name, index));
            }
        }
        if (sources.Count == 0)
        {
            return string.Empty;
        }

        var writer = _writers.Create();
        writer.Line();
        writer.Line("internal static class __WitIntrinsics");
        writer.Line("{");
        writer.Indent();
        foreach (var (id, source) in sources.OrderBy(pair => pair.Key))
        {
            var kind = document.Types[id].Kind.EnumerateObject().Single();
            if (kind.Name == "stream" && kind.Value.ValueKind == JsonValueKind.String && kind.Value.GetString() == "u8")
            {
                WriteByteStream(writer, id, source);
            }
            else if (kind.Name == "stream")
            {
                WriteElementStream(writer, document, id, kind.Value, source);
            }
            else
            {
                WriteFuture(writer, document, id, kind.Value, source);
            }
            writer.Line();
        }
        writer.Unindent();
        writer.Line("}");
        return writer.Text;
    }

    // Imports first, so a type both directions use takes an import's built-ins.
    private static IEnumerable<(string Module, WitFunction Function)> Functions(WitDocument document, WitWorld world)
    {
        foreach (var (items, prefix) in new[] { (world.Imports, string.Empty), (world.Exports, "[export]") })
        {
            foreach (var item in items)
            {
                if (item.Function is { } function)
                {
                    yield return (prefix + "$root", function);
                }
                else if (item.InterfaceId is { } interfaceId)
                {
                    foreach (var interfaceFunction in document.Interfaces[interfaceId].Functions)
                    {
                        yield return (prefix + item.Name, interfaceFunction);
                    }
                }
            }
        }
    }

    private static void WriteByteStream(CodeWriter writer, int id, (string Module, string Function, int Index) source)
    {
        var (module, function, index) = source;
        writer.Line($"internal sealed class Stream{id} : WitStreamIntrinsics");
        writer.Line("{");
        writer.Indent();
        writer.Line($"internal static readonly Stream{id} Instance = new();");
        writer.Line();
        writer.Line("internal static global::System.IO.Stream Lift(int readable) => CanonicalAbi.LiftByteStream(readable, Instance);");
        writer.Line();
        writer.Line("internal static int Lower(global::System.IO.Stream source) => CanonicalAbi.LowerByteStream(source, Instance);");
        writer.Line();
        writer.Line("public override long New() => __New();");
        writer.Line("public override int Read(int readable, nuint buffer, nuint count) => __Read(readable, buffer, count);");
        writer.Line("public override int Write(int writable, nuint buffer, nuint count) => __Write(writable, buffer, count);");
        writer.Line("public override void DropReadable(int readable) => __DropReadable(readable);");
        writer.Line("public override void DropWritable(int writable) => __DropWritable(writable);");
        writer.Line();
        BuiltIn(writer, module, $"[stream-new-{index}]{function}", "long __New()");
        BuiltIn(writer, module, $"[async-lower][stream-read-{index}]{function}", "int __Read(int readable, nuint buffer, nuint count)");
        BuiltIn(writer, module, $"[async-lower][stream-write-{index}]{function}", "int __Write(int writable, nuint buffer, nuint count)");
        BuiltIn(writer, module, $"[stream-drop-readable-{index}]{function}", "void __DropReadable(int readable)");
        BuiltIn(writer, module, $"[stream-drop-writable-{index}]{function}", "void __DropWritable(int writable)");
        writer.Unindent();
        writer.Line("}");
    }

    private void WriteElementStream(
        CodeWriter writer,
        WitDocument document,
        int id,
        JsonElement element,
        (string Module, string Function, int Index) source)
    {
        var (module, function, index) = source;
        var (name, size, alignment, lift, lower, release) = PayloadOperations(document, element);
        writer.Line($"internal sealed class Stream{id} : WitStreamIntrinsics");
        writer.Line("{");
        writer.Indent();
        writer.Line($"internal static readonly Stream{id} Instance = new();");
        writer.Line();
        writer.Line($"internal static global::System.Collections.Generic.IAsyncEnumerable<{name}> Lift(int readable) =>");
        writer.Line($"    CanonicalAbi.LiftStream(readable, Instance, {size}, {alignment}, LiftElement);");
        writer.Line();
        writer.Line($"internal static int Lower(global::System.Collections.Generic.IAsyncEnumerable<{name}> source) =>");
        writer.Line($"    CanonicalAbi.LowerStream(source, Instance, {size}, {alignment}, LowerElement, ReleaseElement);");
        writer.Line();
        WritePayloadHelpers(writer, name, "Element", lift, lower, release);
        writer.Line("public override long New() => __New();");
        writer.Line("public override int Read(int readable, nuint buffer, nuint count) => __Read(readable, buffer, count);");
        writer.Line("public override int Write(int writable, nuint buffer, nuint count) => __Write(writable, buffer, count);");
        writer.Line("public override void DropReadable(int readable) => __DropReadable(readable);");
        writer.Line("public override void DropWritable(int writable) => __DropWritable(writable);");
        writer.Line();
        BuiltIn(writer, module, $"[stream-new-{index}]{function}", "long __New()");
        BuiltIn(writer, module, $"[async-lower][stream-read-{index}]{function}", "int __Read(int readable, nuint buffer, nuint count)");
        BuiltIn(writer, module, $"[async-lower][stream-write-{index}]{function}", "int __Write(int writable, nuint buffer, nuint count)");
        BuiltIn(writer, module, $"[stream-drop-readable-{index}]{function}", "void __DropReadable(int readable)");
        BuiltIn(writer, module, $"[stream-drop-writable-{index}]{function}", "void __DropWritable(int writable)");
        writer.Unindent();
        writer.Line("}");
    }

    // A payload's C# type, canonical size and alignment, and how to lift it from, lower it into
    // and release what it owns at an address.
    private (string Name, string Size, string Alignment, string Lift, string Lower, string Release) PayloadOperations(
        WitDocument document,
        JsonElement payload)
    {
        var reference = Reference(payload);
        var type = _types.Resolve(document, reference);
        var layout32 = _layouts.Plan(type, NetWasm.Compiler.Core.WasmTarget.Wasm32);
        var layout64 = _layouts.Plan(type, NetWasm.Compiler.Core.WasmTarget.Wasm64);
        var (lift, lower, release) = Payload(reference, document);
        return (
            _syntax.Format(new WitBindingSyntaxRequest.TypeName(document, reference)),
            Width(layout32.Size, layout64.Size),
            Width(layout32.Alignment, layout64.Alignment),
            lift,
            lower,
            release);
    }

    private static void WritePayloadHelpers(
        CodeWriter writer,
        string name,
        string suffix,
        string lift,
        string lower,
        string release)
    {
        writer.Line($"private static {name} Lift{suffix}(nuint address)");
        writer.Line("{");
        writer.Indent();
        writer.Line($"var value = {lift};");
        if (release.Length != 0)
        {
            writer.Line(release);
        }
        writer.Line("return value;");
        writer.Unindent();
        writer.Line("}");
        writer.Line();
        writer.Line($"private static void Lower{suffix}({name} value, nuint address) => {lower};");
        writer.Line();
        writer.Line($"private static void Release{suffix}(nuint address) {{ {release} }}");
        writer.Line();
    }

    private void WriteFuture(
        CodeWriter writer,
        WitDocument document,
        int id,
        JsonElement payload,
        (string Module, string Function, int Index) source)
    {
        var (module, function, index) = source;
        var (name, size, alignment, lift, lower, release) = PayloadOperations(document, payload);
        writer.Line($"internal sealed class Future{id} : WitFutureIntrinsics");
        writer.Line("{");
        writer.Indent();
        writer.Line($"internal static readonly Future{id} Instance = new();");
        writer.Line();
        writer.Line($"internal static global::System.Threading.Tasks.Task<{name}> Lift(int readable) =>");
        writer.Line($"    CanonicalAbi.LiftFuture(readable, Instance, {size}, {alignment}, LiftPayload);");
        writer.Line();
        writer.Line($"internal static int Lower(global::System.Threading.Tasks.Task<{name}> value) =>");
        writer.Line($"    CanonicalAbi.LowerFuture(value, Instance, {size}, {alignment}, LowerPayload, ReleasePayload);");
        writer.Line();
        WritePayloadHelpers(writer, name, "Payload", lift, lower, release);
        writer.Line("public override long New() => __New();");
        writer.Line("public override int Read(int readable, nuint buffer) => __Read(readable, buffer);");
        writer.Line("public override int Write(int writable, nuint buffer) => __Write(writable, buffer);");
        writer.Line("public override void DropReadable(int readable) => __DropReadable(readable);");
        writer.Line("public override void DropWritable(int writable) => __DropWritable(writable);");
        writer.Line();
        BuiltIn(writer, module, $"[future-new-{index}]{function}", "long __New()");
        BuiltIn(writer, module, $"[async-lower][future-read-{index}]{function}", "int __Read(int readable, nuint buffer)");
        BuiltIn(writer, module, $"[async-lower][future-write-{index}]{function}", "int __Write(int writable, nuint buffer)");
        BuiltIn(writer, module, $"[future-drop-readable-{index}]{function}", "void __DropReadable(int readable)");
        BuiltIn(writer, module, $"[future-drop-writable-{index}]{function}", "void __DropWritable(int writable)");
        writer.Unindent();
        writer.Line("}");
    }

    // Lifting the payload from, lowering it into, and releasing what it owns at an address.
    private static (string Lift, string Lower, string Release) Payload(WitTypeReference reference, WitDocument document)
    {
        if (reference is WitTypeReference.Primitive primitive)
        {
            var lift = WitCanonicalMarshallingTypeSectionWriter.LiftPrimitiveExpression(primitive.Name, "address", "0");
            var lower = WitCanonicalMarshallingTypeSectionWriter.WritePrimitiveStatement(primitive.Name, "address", "0", "value").TrimEnd(';');
            return primitive.Name == "string"
                ? (lift, $"{{ var text = CanonicalAbi.LowerString(value); CanonicalAbi.WriteAddress(address, 0, text.Address); CanonicalAbi.WriteAddress(address, (nuint)UIntPtr.Size, text.Length); }}",
                    "CanonicalAbi.Free(CanonicalAbi.ReadAddress(address, 0));")
                : (lift, lower, string.Empty);
        }
        var id = ((WitTypeReference.Defined)reference).Id;
        if (document.Types[id].Kind.ValueKind == JsonValueKind.Object &&
            document.Types[id].Kind.EnumerateObject().Single().Name == "list")
        {
            return (
                $"__CanonicalMarshalling.LiftType{id}(CanonicalAbi.ReadAddress(address, 0), CanonicalAbi.ReadAddress(address, (nuint)UIntPtr.Size), false)",
                $"{{ var list = __CanonicalMarshalling.LowerType{id}(value, false); CanonicalAbi.WriteAddress(address, 0, list.Address); CanonicalAbi.WriteAddress(address, (nuint)UIntPtr.Size, list.Length); }}",
                $"__CanonicalMarshalling.FreeType{id}(CanonicalAbi.ReadAddress(address, 0), CanonicalAbi.ReadAddress(address, (nuint)UIntPtr.Size));");
        }
        return (
            $"__CanonicalMarshalling.LiftType{id}(address, false)",
            $"__CanonicalMarshalling.WriteType{id}(address, value, false)",
            $"__CanonicalMarshalling.FreeType{id}Contents(address);");
    }

    private static void BuiltIn(CodeWriter writer, string module, string name, string signature)
    {
        writer.Line($"[ComponentBuiltIn(\"{module}\", \"{name}\")]");
        writer.Line($"private static extern {signature};");
    }

    private static string Width(int wasm32, int wasm64) =>
        wasm32 == wasm64
            ? $"(nuint){wasm32}"
            : $"(nuint)(UIntPtr.Size == 8 ? {wasm64} : {wasm32})";

    private static WitTypeReference Reference(JsonElement value) =>
        value.ValueKind == JsonValueKind.String
            ? new WitTypeReference.Primitive(value.GetString()!)
            : new WitTypeReference.Defined(value.GetInt32());
}
