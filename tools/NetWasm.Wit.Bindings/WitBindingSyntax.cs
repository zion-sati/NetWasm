using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using NetWasm.Wit.Bindings.TypeDefinitions;

namespace NetWasm.Wit.Bindings;

public abstract record WitBindingSyntaxRequest
{
    public sealed record PackageNamespace(string Package) : WitBindingSyntaxRequest;

    public sealed record FunctionName(string Name) : WitBindingSyntaxRequest;

    public sealed record QualifiedFunctionName(string InterfaceName, string Name)
        : WitBindingSyntaxRequest;

    public sealed record Identifier(string Value) : WitBindingSyntaxRequest;

    public sealed record Variable(string Value) : WitBindingSyntaxRequest;

    public sealed record ReturnType(
        WitDocument Document,
        WitFunction Function) : WitBindingSyntaxRequest;

    public sealed record Parameters(
        WitDocument Document,
        WitFunction Function) : WitBindingSyntaxRequest;

    public sealed record TypeName(
        WitDocument Document,
        WitTypeReference Reference) : WitBindingSyntaxRequest;

    public sealed record TypeDefinitionName(
        WitDocument Document,
        WitTypeDefinition Definition) : WitBindingSyntaxRequest;

    public sealed record JsonTypeName(
        WitDocument Document,
        JsonElement Reference) : WitBindingSyntaxRequest;

    public sealed record ArrayCreation(
        WitDocument Document,
        WitTypeReference Element,
        string LengthExpression) : WitBindingSyntaxRequest;
}

public interface IWitBindingSyntaxFormatter
{
    string Format(WitBindingSyntaxRequest request);
}

public sealed class WitBindingSyntaxFormatter(
    IWitTypeDefinitionClassifier definitions) : IWitBindingSyntaxFormatter
{
    private readonly IWitTypeDefinitionClassifier _definitions = definitions
        ?? throw new ArgumentNullException(nameof(definitions));

    private static readonly HashSet<string> CSharpKeywords = new(
        [
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch",
            "char", "checked", "class", "const", "continue", "decimal", "default",
            "delegate", "do", "double", "else", "enum", "event", "explicit",
            "extern", "false", "finally", "fixed", "float", "for", "foreach",
            "goto", "if", "implicit", "in", "int", "interface", "internal", "is",
            "lock", "long", "namespace", "new", "null", "object", "operator", "out",
            "override", "params", "private", "protected", "public", "readonly", "ref",
            "return", "sbyte", "sealed", "short", "sizeof", "stackalloc", "static",
            "string", "struct", "switch", "this", "throw", "true", "try", "typeof",
            "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual",
            "void", "volatile", "while",
        ],
        StringComparer.Ordinal);

    public string Format(WitBindingSyntaxRequest request) => request switch
    {
        WitBindingSyntaxRequest.PackageNamespace value => FormatNamespace(value.Package),
        WitBindingSyntaxRequest.FunctionName value => FormatFunctionName(value.Name),
        WitBindingSyntaxRequest.QualifiedFunctionName value =>
            $"{FormatIdentifier(value.InterfaceName)}_{FormatIdentifier(value.Name)}",
        WitBindingSyntaxRequest.Identifier value => FormatIdentifier(value.Value),
        WitBindingSyntaxRequest.Variable value => FormatVariable(value.Value),
        WitBindingSyntaxRequest.ReturnType value => FormatReturnType(
            value.Document,
            value.Function),
        WitBindingSyntaxRequest.Parameters value => FormatParameters(
            value.Document,
            value.Function),
        WitBindingSyntaxRequest.TypeName value => FormatTypeName(
            value.Document,
            value.Reference),
        WitBindingSyntaxRequest.TypeDefinitionName value =>
            FormatTypeName(value.Document, value.Definition),
        WitBindingSyntaxRequest.JsonTypeName value => FormatTypeName(
            value.Document,
            value.Reference.ValueKind == JsonValueKind.String
                ? new WitTypeReference.Primitive(value.Reference.GetString()!)
                : new WitTypeReference.Defined(value.Reference.GetInt32())),
        WitBindingSyntaxRequest.ArrayCreation value => FormatArrayCreation(
            value.Document,
            value.Element,
            value.LengthExpression),
        _ => throw new ArgumentOutOfRangeException(nameof(request)),
    };

    private static string FormatNamespace(string package)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(package);
        var components = package.Split(
            [':', '/', '@', '.', '-'],
            StringSplitOptions.RemoveEmptyEntries);
        return "NetWasm.Wit." + string.Join('.', components.Select(FormatIdentifier));
    }

    private static string FormatFunctionName(string name)
    {
        const string constructor = "[constructor]";
        if (name.StartsWith(constructor, StringComparison.Ordinal))
        {
            return "Create" + FormatIdentifier(name[constructor.Length..]);
        }
        var separator = name.LastIndexOf(']');
        var unqualified = separator >= 0 ? name[(separator + 1)..] : name;
        var dot = unqualified.LastIndexOf('.');
        return FormatIdentifier(dot >= 0 ? unqualified[(dot + 1)..] : unqualified);
    }

    private static string FormatIdentifier(string value)
    {
        var result = new StringBuilder();
        var capitalize = true;
        foreach (var character in value)
        {
            if (!char.IsLetterOrDigit(character) && character != '_')
            {
                capitalize = true;
                continue;
            }
            result.Append(capitalize ? char.ToUpperInvariant(character) : character);
            capitalize = false;
        }
        if (result.Length == 0 || char.IsDigit(result[0]))
        {
            result.Insert(0, '_');
        }
        return result.ToString();
    }

    private static string FormatVariable(string value)
    {
        var identifier = FormatIdentifier(value);
        var variable = char.ToLowerInvariant(identifier[0]) + identifier[1..];
        return CSharpKeywords.Contains(variable) ? "@" + variable : variable;
    }

    private string FormatReturnType(WitDocument document, WitFunction function) =>
        function.Result is null ? "void" : FormatTypeName(document, function.Result);

    private string FormatParameters(WitDocument document, WitFunction function) =>
        string.Join(", ", function.Parameters.Select(parameter =>
            $"{FormatTypeName(document, parameter.Type)} {FormatVariable(parameter.Name)}"));

    private string FormatTypeName(
        WitDocument document,
        WitTypeReference reference) => reference switch
        {
            WitTypeReference.Primitive primitive => FormatNamedTypeName(document, primitive.Name),
            WitTypeReference.Defined defined => FormatTypeName(
                document,
                document.Types[defined.Id]),
            _ => throw new InvalidOperationException(),
        };

    private string FormatNamedTypeName(WitDocument document, string name)
    {
        var definition = document.Types.FirstOrDefault(candidate => candidate.Name == name);
        return definition is not null
            ? FormatTypeName(document, definition)
            : PrimitiveType(name);
    }

    private string FormatTypeName(WitDocument document, WitTypeDefinition definition)
    {
        if (definition.Kind.ValueKind == JsonValueKind.Object)
        {
            var alias = definition.Kind.EnumerateObject().Single();
            if (alias.Name == "type")
            {
                return FormatTypeName(document, alias.Value);
            }
        }
        if (definition.Name is not null
            && _definitions.Classify(definition) != WitTypeDefinitionCategory.TransparentAlias)
        {
            return FormatIdentifier(definition.Name);
        }
        var kind = definition.Kind.EnumerateObject().Single();
        return kind.Name switch
        {
            "list" => $"{FormatTypeName(document, kind.Value)}[]",
            "option" => $"WitOption<{FormatTypeName(document, kind.Value)}>",
            "tuple" => $"({string.Join(", ", kind.Value.GetProperty("types").EnumerateArray().Select(type => FormatTypeName(document, type)))})",
            "result" => $"WitResult<{ResultType(document, kind.Value, "ok")}, {ResultType(document, kind.Value, "err")}>",
            "handle" => FormatTypeName(document, new WitTypeReference.Defined(
                kind.Value.EnumerateObject().Single().Value.GetInt32())),
            _ => throw WitBindingException.Invalid(
                $"unsupported anonymous WIT type '{kind.Name}'"),
        };
    }

    private string FormatTypeName(WitDocument document, JsonElement reference) =>
        FormatTypeName(
            document,
            reference.ValueKind == JsonValueKind.String
                ? new WitTypeReference.Primitive(reference.GetString()!)
                : new WitTypeReference.Defined(reference.GetInt32()));

    private string FormatArrayCreation(
        WitDocument document,
        WitTypeReference element,
        string lengthExpression)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lengthExpression);
        var elementType = FormatTypeName(document, element);
        var baseTypeLength = elementType.Length;
        while (elementType.AsSpan(0, baseTypeLength)
               .EndsWith("[]", StringComparison.Ordinal))
        {
            baseTypeLength -= 2;
        }

        return $"new {elementType[..baseTypeLength]}[{lengthExpression}]{elementType[baseTypeLength..]}";
    }

    private string ResultType(WitDocument document, JsonElement result, string name)
    {
        var value = result.GetProperty(name);
        return value.ValueKind == JsonValueKind.Null
            ? "WitUnit"
            : FormatTypeName(document, value);
    }

    private static string PrimitiveType(string name) => name switch
    {
        "bool" => "bool",
        "u8" => "byte",
        "s8" => "sbyte",
        "u16" => "ushort",
        "s16" => "short",
        "u32" => "uint",
        "s32" => "int",
        "u64" => "ulong",
        "s64" => "long",
        "f32" => "float",
        "f64" => "double",
        "char" => "uint",
        "string" => "string",
        _ => throw WitBindingException.Invalid($"unsupported WIT primitive '{name}'"),
    };
}
