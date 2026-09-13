using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using NetWasm.Wit.Bindings.Aliases;
using NetWasm.Wit.Bindings.TypeDefinitions;

namespace NetWasm.Wit.Bindings;

public interface IWitTypeDeclarationWriter
{
    string Generate(WitDocument document, WitWorld world);
}

public sealed class WitTypeDeclarationWriter(
    IWitBindingSyntaxFormatter syntax,
    ICodeWriterFactory writers,
    IWitAliasValidator aliasValidator,
    IWitTypeDefinitionClassifier definitions) : IWitTypeDeclarationWriter
{
    private readonly IWitTypeDefinitionClassifier _definitions = definitions
        ?? throw new ArgumentNullException(nameof(definitions));

    private readonly IWitBindingSyntaxFormatter _syntax = syntax ??
        throw new ArgumentNullException(nameof(syntax));
    private readonly ICodeWriterFactory _writers = writers ??
        throw new ArgumentNullException(nameof(writers));
    private readonly IWitAliasValidator _aliasValidator = aliasValidator ??
        throw new ArgumentNullException(nameof(aliasValidator));


    public string Generate(WitDocument document, WitWorld world)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(world);
        var importedInterfaces = world.Imports
            .Where(item => item.InterfaceId is not null)
            .Select(item => item.InterfaceId!.Value)
            .ToHashSet();
        var exportedInterfaces = world.Exports
            .Where(item => item.InterfaceId is not null)
            .Select(item => item.InterfaceId!.Value)
            .ToHashSet();
        var writer = _writers.Create();
        foreach (var interfaceId in world.Imports.Concat(world.Exports)
                     .Where(item => item.InterfaceId is not null)
                     .Select(item => item.InterfaceId!.Value)
                     .Distinct()
                     .Order())
        {
            var @interface = document.Interfaces[interfaceId];
            foreach (var pair in @interface.Types.OrderBy(pair => pair.Value))
            {
                WriteNamedType(
                    writer,
                    document,
                    @interface,
                    document.Types[pair.Value],
                    importedInterfaces.Contains(interfaceId),
                    exportedInterfaces.Contains(interfaceId));
                writer.Line();
            }
        }
        return writer.Text;
    }

    private void WriteNamedType(
        CodeWriter writer,
        WitDocument document,
        WitInterface @interface,
        WitTypeDefinition definition,
        bool imported,
        bool exported)
    {
        var name = _syntax.Format(new WitBindingSyntaxRequest.Identifier(definition.Name!));
        if (definition.Kind.ValueKind == JsonValueKind.String &&
            definition.Kind.GetString() == "resource")
        {
            WriteResource(
                writer,
                @interface,
                name,
                definition.Name!,
                imported,
                exported);
            return;
        }

        var kind = definition.Kind.EnumerateObject().Single();
        if (_definitions.Classify(definition) == WitTypeDefinitionCategory.TransparentAlias)
        {
            _aliasValidator.Validate(document, definition);
            return;
        }

        switch (kind.Name)
        {
            case "record":
                WriteRecord(writer, document, name, kind.Value);
                break;
            case "variant":
                WriteVariant(writer, document, name, kind.Value);
                break;
            case "enum":
                WriteEnum(writer, name, kind.Value);
                break;
            case "flags":
                WriteFlags(writer, name, kind.Value);
                break;
            default:
                throw WitBindingException.Invalid(
                    $"unsupported named WIT type '{kind.Name}' for '{definition.Name}'");
        }
    }

    private void WriteRecord(
        CodeWriter writer,
        WitDocument document,
        string name,
        JsonElement record)
    {
        var fields = record.GetProperty("fields").EnumerateArray().ToArray();
        writer.Line($"public readonly struct {name}");
        writer.Line("{");
        writer.Indent();
        writer.Line($"public {name}(");
        writer.Indent();
        for (var index = 0; index < fields.Length; index++)
        {
            var suffix = index + 1 == fields.Length ? ")" : ",";
            var fieldName = fields[index].GetProperty("name").GetString()!;
            writer.Line($"{_syntax.Format(new WitBindingSyntaxRequest.JsonTypeName(document, fields[index].GetProperty("type")))} {_syntax.Format(new WitBindingSyntaxRequest.Variable(fieldName))}{suffix}");
        }
        writer.Unindent();
        writer.Line("{");
        writer.Indent();
        foreach (var field in fields)
        {
            var fieldName = field.GetProperty("name").GetString()!;
            writer.Line($"{_syntax.Format(new WitBindingSyntaxRequest.Identifier(fieldName))} = {_syntax.Format(new WitBindingSyntaxRequest.Variable(fieldName))};");
        }
        writer.Unindent();
        writer.Line("}");
        writer.Line();
        foreach (var field in fields)
        {
            writer.Line($"public {_syntax.Format(new WitBindingSyntaxRequest.JsonTypeName(document, field.GetProperty("type")))} {_syntax.Format(new WitBindingSyntaxRequest.Identifier(field.GetProperty("name").GetString()!))} {{ get; }}");
        }
        writer.Unindent();
        writer.Line("}");
    }

    private void WriteVariant(
        CodeWriter writer,
        WitDocument document,
        string name,
        JsonElement variant)
    {
        var cases = variant.GetProperty("cases").EnumerateArray().ToArray();
        writer.Line($"public enum {name}Tag");
        writer.Line("{");
        writer.Indent();
        foreach (var item in cases)
        {
            writer.Line($"{_syntax.Format(new WitBindingSyntaxRequest.Identifier(item.GetProperty("name").GetString()!))},");
        }
        writer.Unindent();
        writer.Line("}");
        writer.Line();
        writer.Line($"public readonly struct {name}");
        writer.Line("{");
        writer.Indent();
        foreach (var item in cases)
        {
            var caseName = item.GetProperty("name").GetString()!;
            var type = item.GetProperty("type");
            if (type.ValueKind == JsonValueKind.Null)
            {
                writer.Line($"public static {name} {_syntax.Format(new WitBindingSyntaxRequest.Identifier(caseName))}() => new({name}Tag.{_syntax.Format(new WitBindingSyntaxRequest.Identifier(caseName))});");
            }
            else
            {
                writer.Line($"public static {name} {_syntax.Format(new WitBindingSyntaxRequest.Identifier(caseName))}({_syntax.Format(new WitBindingSyntaxRequest.JsonTypeName(document, type))} value) => new({name}Tag.{_syntax.Format(new WitBindingSyntaxRequest.Identifier(caseName))}, {_syntax.Format(new WitBindingSyntaxRequest.Variable(caseName))}: value);");
            }
        }
        writer.Line();
        writer.Line($"private {name}({name}Tag tag{VariantConstructorParameters(document, cases)})");
        writer.Line("{");
        writer.Indent();
        writer.Line("Tag = tag;");
        foreach (var item in cases.Where(item =>
                     item.GetProperty("type").ValueKind != JsonValueKind.Null))
        {
            var caseName = item.GetProperty("name").GetString()!;
            writer.Line($"{_syntax.Format(new WitBindingSyntaxRequest.Identifier(caseName))}Value = {_syntax.Format(new WitBindingSyntaxRequest.Variable(caseName))};");
        }
        writer.Unindent();
        writer.Line("}");
        writer.Line();
        writer.Line($"public {name}Tag Tag {{ get; }}");
        foreach (var item in cases.Where(item =>
                     item.GetProperty("type").ValueKind != JsonValueKind.Null))
        {
            var caseName = item.GetProperty("name").GetString()!;
            writer.Line($"public {_syntax.Format(new WitBindingSyntaxRequest.JsonTypeName(document, item.GetProperty("type")))} {_syntax.Format(new WitBindingSyntaxRequest.Identifier(caseName))}Value {{ get; }}");
        }
        writer.Unindent();
        writer.Line("}");
    }

    private string VariantConstructorParameters(
        WitDocument document,
        IEnumerable<JsonElement> cases) => string.Concat(cases
        .Where(item => item.GetProperty("type").ValueKind != JsonValueKind.Null)
        .Select(item =>
        {
            var name = item.GetProperty("name").GetString()!;
            return $", {_syntax.Format(new WitBindingSyntaxRequest.JsonTypeName(document, item.GetProperty("type")))} {_syntax.Format(new WitBindingSyntaxRequest.Variable(name))} = default!";
        }));

    private void WriteEnum(CodeWriter writer, string name, JsonElement value)
    {
        writer.Line($"public enum {name}");
        writer.Line("{");
        writer.Indent();
        foreach (var item in value.GetProperty("cases").EnumerateArray())
        {
            writer.Line($"{_syntax.Format(new WitBindingSyntaxRequest.Identifier(item.GetProperty("name").GetString()!))},");
        }
        writer.Unindent();
        writer.Line("}");
    }

    private void WriteFlags(CodeWriter writer, string name, JsonElement value)
    {
        var flags = value.GetProperty("flags").EnumerateArray().ToArray();
        if (flags.Length > 64)
        {
            WriteWideFlags(writer, name, flags);
            return;
        }
        var underlying = flags.Length <= 32 ? "uint" : "ulong";
        writer.Line("[Flags]");
        writer.Line($"public enum {name} : {underlying}");
        writer.Line("{");
        writer.Indent();
        for (var index = 0; index < flags.Length; index++)
        {
            var suffix = underlying == "uint" ? "u" : "ul";
            writer.Line($"{_syntax.Format(new WitBindingSyntaxRequest.Identifier(flags[index].GetProperty("name").GetString()!))} = 1{suffix} << {index},");
        }
        writer.Unindent();
        writer.Line("}");
    }

    private void WriteWideFlags(
        CodeWriter writer,
        string name,
        JsonElement[] flags)
    {
        var wordCount = (flags.Length + 31) / 32;
        writer.Line($"public readonly struct {name} : IEquatable<{name}>");
        writer.Line("{");
        writer.Indent();
        writer.Line("private readonly uint[]? _words;");
        writer.Line();
        writer.Line($"private {name}(uint[] words) => _words = words;");
        writer.Line($"internal static {name} FromWords(uint[] words) => new(words);");
        writer.Line("internal uint GetWord(int index) => _words == null ? 0u : _words[index];");
        writer.Line();
        for (var index = 0; index < flags.Length; index++)
        {
            writer.Line($"public static {name} {_syntax.Format(new WitBindingSyntaxRequest.Identifier(flags[index].GetProperty("name").GetString()!))} => FromBit({index});");
        }
        writer.Line();
        writer.Line($"public static {name} operator |({name} left, {name} right) => Combine(left, right, 0);");
        writer.Line($"public static {name} operator &({name} left, {name} right) => Combine(left, right, 1);");
        writer.Line($"public static {name} operator ^({name} left, {name} right) => Combine(left, right, 2);");
        writer.Line($"public static {name} operator ~({name} value) => Complement(value);");
        writer.Line($"public static bool operator ==({name} left, {name} right) => left.Equals(right);");
        writer.Line($"public static bool operator !=({name} left, {name} right) => !left.Equals(right);");
        writer.Line();
        writer.Line($"public bool Equals({name} other)");
        writer.Line("{");
        writer.Indent();
        writer.Line($"for (var index = 0; index < {wordCount}; index++)");
        writer.Line("{");
        writer.Indent();
        writer.Line("if (GetWord(index) != other.GetWord(index)) return false;");
        writer.Unindent();
        writer.Line("}");
        writer.Line("return true;");
        writer.Unindent();
        writer.Line("}");
        writer.Line($"public override bool Equals(object? value) => value is {name} other && Equals(other);");
        writer.Line("public override int GetHashCode()");
        writer.Line("{");
        writer.Indent();
        writer.Line("var hash = 17;");
        writer.Line($"for (var index = 0; index < {wordCount}; index++) hash = unchecked(hash * 31 + (int)GetWord(index));");
        writer.Line("return hash;");
        writer.Unindent();
        writer.Line("}");
        writer.Line();
        writer.Line($"private static {name} FromBit(int bit)");
        writer.Line("{");
        writer.Indent();
        writer.Line($"var words = new uint[{wordCount}];");
        writer.Line("words[bit / 32] = 1u << (bit % 32);");
        writer.Line($"return new {name}(words);");
        writer.Unindent();
        writer.Line("}");
        writer.Line();
        writer.Line($"private static {name} Combine({name} left, {name} right, int operation)");
        writer.Line("{");
        writer.Indent();
        writer.Line($"var words = new uint[{wordCount}];");
        writer.Line($"for (var index = 0; index < {wordCount}; index++)");
        writer.Line("{");
        writer.Indent();
        writer.Line("var leftWord = left.GetWord(index);");
        writer.Line("var rightWord = right.GetWord(index);");
        writer.Line("words[index] = operation == 0 ? leftWord | rightWord : operation == 1 ? leftWord & rightWord : leftWord ^ rightWord;");
        writer.Unindent();
        writer.Line("}");
        writer.Line($"return new {name}(words);");
        writer.Unindent();
        writer.Line("}");
        writer.Line();
        writer.Line($"private static {name} Complement({name} value)");
        writer.Line("{");
        writer.Indent();
        writer.Line($"var words = new uint[{wordCount}];");
        writer.Line($"for (var index = 0; index < {wordCount}; index++) words[index] = ~value.GetWord(index);");
        var finalBits = flags.Length % 32;
        if (finalBits != 0)
        {
            writer.Line($"words[{wordCount - 1}] &= (1u << {finalBits}) - 1u;");
        }
        writer.Line($"return new {name}(words);");
        writer.Unindent();
        writer.Line("}");
        writer.Unindent();
        writer.Line("}");
    }

    private static void WriteResource(
        CodeWriter writer,
        WitInterface @interface,
        string name,
        string witName,
        bool imported,
        bool exported)
    {
        var interfaceName = $"{@interface.Package}/{@interface.Name}";
        writer.Line($"[WitResource(\"{interfaceName}\", \"{witName}\")]");
        writer.Line($"public sealed class {name} : IDisposable");
        writer.Line("{");
        writer.Indent();
        writer.Line("private uint _handle;");
        if (exported) writer.Line("private object? _state;");
        writer.Line("private bool _alive;");
        writer.Line("private bool _ownsHandle;");
        writer.Line("private byte _kind;");
        writer.Line();
        writer.Line($"internal {name}(uint handle, bool ownsHandle)");
        writer.Line("{");
        writer.Indent();
        writer.Line("_handle = handle;");
        writer.Line("_alive = true;");
        writer.Line("_ownsHandle = ownsHandle;");
        writer.Line("_kind = 0;");
        writer.Unindent();
        writer.Line("}");
        if (exported)
        {
            writer.Line();
            writer.Line($"private {name}(object state)");
            writer.Line("{");
            writer.Indent();
            writer.Line("_state = state ?? throw new ArgumentNullException();");
            writer.Line("_alive = true;");
            writer.Line("_ownsHandle = true;");
            writer.Line("_kind = 1;");
            writer.Unindent();
            writer.Line("}");
            writer.Line();
            writer.Line($"private {name}(uint handle, bool ownsHandle, byte kind)");
            writer.Line("{");
            writer.Indent();
            writer.Line("_handle = handle;");
            writer.Line("_alive = true;");
            writer.Line("_ownsHandle = ownsHandle;");
            writer.Line("_kind = kind;");
            writer.Unindent();
            writer.Line("}");
            writer.Line();
            writer.Line($"public static {name} Create(object state) => new(state);");
            writer.Line("public object State");
            writer.Line("{");
            writer.Indent();
            writer.Line("get");
            writer.Line("{");
            writer.Indent();
            writer.Line("if (!_alive) throw new InvalidOperationException();");
            writer.Line("if (_kind == 1) return _state!;");
            writer.Line("if (_kind != 2 && _kind != 3) throw new InvalidOperationException();");
            writer.Line("var representation = _kind == 3 ? _handle : ExportRep(_handle);");
            writer.Line($"var owner = CanonicalAbi.GetResourceHandle(representation) as {name} ?? throw new InvalidOperationException();");
            writer.Line("return owner._state ?? throw new InvalidOperationException();");
            writer.Unindent();
            writer.Line("}");
            writer.Unindent();
            writer.Line("}");
        }
        writer.Line();
        writer.Line($"~{name}() => Release();");
        writer.Line("public void Dispose() => Release();");
        writer.Line("internal uint RawHandle => _alive && _kind == 0 ? _handle : throw new InvalidOperationException();");
        writer.Line("internal uint LowerImport(bool transferOwnership)");
        writer.Line("{");
        writer.Indent();
        writer.Line("if (!_alive || _kind != 0) throw new InvalidOperationException();");
        writer.Line("var handle = _handle;");
        writer.Line("if (transferOwnership)");
        writer.Line("{");
        writer.Indent();
        writer.Line("if (!_ownsHandle) throw new InvalidOperationException();");
        writer.Line("Invalidate();");
        writer.Unindent();
        writer.Line("}");
        writer.Line("return handle;");
        writer.Unindent();
        writer.Line("}");
        if (!exported)
        {
            writer.Line($"internal static {name} LiftExport(uint handle, bool ownsHandle) => throw new InvalidOperationException();");
            writer.Line("internal uint LowerExport() => throw new InvalidOperationException();");
        }
        writer.Line("internal void Invalidate() { _alive = false; _handle = 0; _ownsHandle = false; _kind = 4; }");
        writer.Line();
        writer.Line("private void Release()");
        writer.Line("{");
        writer.Indent();
        writer.Line("if (!_alive) return;");
        if (imported) writer.Line("if (_ownsHandle && _kind == 0) DropImported(_handle);");
        if (exported) writer.Line("if (_ownsHandle && _kind == 1) DestroyState();");
        if (exported) writer.Line("if (_ownsHandle && _kind == 2) DropExported(_handle);");
        writer.Line("_alive = false;");
        writer.Line("_handle = 0;");
        writer.Line("_ownsHandle = false;");
        writer.Line("_kind = 4;");
        writer.Unindent();
        writer.Line("}");
        if (exported)
        {
            writer.Line();
            writer.Line("private void DestroyState()");
            writer.Line("{");
            writer.Indent();
            writer.Line("if (_state is IDisposable disposable) disposable.Dispose();");
            writer.Line("_state = null;");
            writer.Unindent();
            writer.Line("}");
        }
        writer.Line();
        if (imported)
        {
            writer.Line($"[WitImport(\"{interfaceName}\", \"[resource-drop]{witName}\")]");
            writer.Line("private static extern void DropImported(uint handle);");
        }
        if (exported)
        {
            writer.Line();
            writer.Line($"internal static {name} LiftExport(uint handle, bool ownsHandle) => new(handle, ownsHandle, ownsHandle ? (byte)2 : (byte)3);");
            writer.Line("internal uint LowerExport()");
            writer.Line("{");
            writer.Indent();
            writer.Line("if (!_alive || !_ownsHandle) throw new InvalidOperationException();");
            writer.Line("if (_kind == 2)");
            writer.Line("{");
            writer.Indent();
            writer.Line("var existing = _handle;");
            writer.Line("Invalidate();");
            writer.Line("return existing;");
            writer.Unindent();
            writer.Line("}");
            writer.Line("if (_kind != 1) throw new InvalidOperationException();");
            writer.Line("var representation = CanonicalAbi.CreateResourceHandle(this);");
            writer.Line("if (representation == 0) throw new OutOfMemoryException();");
            writer.Line("try");
            writer.Line("{");
            writer.Indent();
            writer.Line("var handle = ExportNew(representation);");
            writer.Line("_alive = false;");
            writer.Line("_ownsHandle = false;");
            writer.Line("_kind = 4;");
            writer.Line("return handle;");
            writer.Unindent();
            writer.Line("}");
            writer.Line("catch");
            writer.Line("{");
            writer.Indent();
            writer.Line("CanonicalAbi.ReleaseResourceHandle(representation);");
            writer.Line("throw;");
            writer.Unindent();
            writer.Line("}");
            writer.Unindent();
            writer.Line("}");
            writer.Line();
            writer.Line($"[WitImport(\"{interfaceName}\", \"[export-resource-new]{witName}\")]");
            writer.Line("private static extern uint ExportNew(uint representation);");
            writer.Line($"[WitImport(\"{interfaceName}\", \"[export-resource-rep]{witName}\")]");
            writer.Line("private static extern uint ExportRep(uint handle);");
            writer.Line($"[WitImport(\"{interfaceName}\", \"[export-resource-drop]{witName}\")]");
            writer.Line("private static extern void DropExported(uint handle);");
            writer.Line();
            writer.Line($"[WitExport(\"{interfaceName}\", \"[resource-dtor]{witName}\")]");
            writer.Line("private static void DestroyExported(uint representation)");
            writer.Line("{");
            writer.Indent();
            writer.Line($"var resource = CanonicalAbi.GetResourceHandle(representation) as {name} ?? throw new InvalidOperationException();");
            writer.Line("CanonicalAbi.ReleaseResourceHandle(representation);");
            writer.Line("resource.DestroyState();");
            writer.Unindent();
            writer.Line("}");
        }
        writer.Unindent();
        writer.Line("}");
    }
}
