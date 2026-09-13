using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NetWasm.Compiler.ComponentModel.Worlds;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel.Raw;

public sealed record RawAdapterWriteRequest(
    ImmutableArray<RawValidatedBindingPlan> Plans);

/// <summary>Emits a dependency-free adapter from validated raw binding plans.</summary>
public interface IRawAdapterWriter
{
    byte[] Write(RawValidatedBindingPlan plan);

    byte[] WriteDeployment(RawAdapterWriteRequest request);
}

/// <summary>Emits selected-only, source-bound raw WIT binding plans.</summary>
public sealed class RawAdapterWriter(
    IWitInterfaceSpecifierFormatter interfaces) : IRawAdapterWriter
{
    public const int CurrentAbiVersion = 1;
    private const string ReactorHostInterface = "netwasm:runtime@1.0.0/reactor-host";
    private readonly IWitInterfaceSpecifierFormatter _interfaces = interfaces ??
        throw new ArgumentNullException(nameof(interfaces));

    public byte[] Write(RawValidatedBindingPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return WriteDeployment(new RawAdapterWriteRequest([plan]));
    }

    public byte[] WriteDeployment(RawAdapterWriteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var context = CreateContext(request);
        var bindings = context.Bindings.Select((binding, index) =>
            $"const binding{index.ToString(CultureInfo.InvariantCulture)} = " +
            $"deepFreeze({SerializeBinding(binding, context)});").ToArray();
        var capabilityNames = context.Bindings
            .Select(binding => Capability(binding.Layout))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var metadata = SerializeMetadata(context);
        var requestKeys = SerializeStrings(["metadata", .. capabilityNames]);

        var source = new StringBuilder();
        source.AppendLine(CultureInfo.InvariantCulture,
            $"export const rawAdapterMetadata = deepFreeze({metadata});");
        if (bindings.Length != 0)
        {
            source.AppendLine();
            foreach (var binding in bindings) source.AppendLine(binding);
        }
        source.AppendLine();
        source.AppendLine("export function createAdapter(request = {}) {");
        source.AppendLine(CultureInfo.InvariantCulture,
            $"  assertExactObject(request, {requestKeys}, \"raw adapter request\");");
        source.AppendLine("  if (request.metadata !== rawAdapterMetadata) {");
        source.AppendLine("    throw new TypeError(\"raw adapter metadata does not belong to this module\");");
        source.AppendLine("  }");
        source.AppendLine("  const imports = Object.create(null);");
        for (var index = 0; index < context.Bindings.Length; index++)
        {
            var layout = context.Bindings[index].Layout;
            var capability = Capability(layout);
            source.AppendLine(CultureInfo.InvariantCulture,
                $"  addBinding(imports, binding{index.ToString(CultureInfo.InvariantCulture)}, " +
                $"bind(request.{capability}, binding{index.ToString(CultureInfo.InvariantCulture)}));");
        }
        source.AppendLine("  for (const module of Object.values(imports)) Object.freeze(module);");
        source.AppendLine("  return Object.freeze({ metadata: rawAdapterMetadata, imports: Object.freeze(imports) });");
        source.AppendLine("}");
        source.AppendLine();
        source.AppendLine(CommonSource);
        return Encoding.UTF8.GetBytes(source.ToString().ReplaceLineEndings("\n"));
    }

    private RawAdapterWriteContext CreateContext(RawAdapterWriteRequest request)
    {
        if (request.Plans.IsDefaultOrEmpty)
        {
            throw Invalid("raw adapter binding plans must be explicit and non-empty");
        }
        var target = request.Plans[0]?.Plan.Selection.Catalog.Target ??
            throw Invalid("raw adapter binding plan cannot be null");
        var bindings = ImmutableArray.CreateBuilder<RawAdapterBindingContext>();
        var identities = new HashSet<RawCanonicalImportIdentity>();
        var documents = new HashSet<string>(StringComparer.Ordinal);
        foreach (var validated in request.Plans)
        {
            if (validated is null)
            {
                throw Invalid("raw adapter binding plan cannot be null");
            }
            var catalog = validated.Plan.Selection.Catalog;
            if (catalog.Target != target)
            {
                throw Invalid("raw adapter binding plans must target the same architecture");
            }
            documents.Add(catalog.Document.NormalizedJson);
            var expected = validated.ExpectedImports.ToDictionary(signature => signature.Identity);
            foreach (var layout in validated.Plan.Imports)
            {
                if (!identities.Add(layout.Identity))
                {
                    throw Invalid("raw adapter binding plans contain duplicate physical identities");
                }
                if (!expected.TryGetValue(layout.Identity, out var signature))
                {
                    throw Invalid("raw adapter binding has no validated final signature");
                }
                bindings.Add(new(
                    layout,
                    ResolveDeclaration(catalog, layout),
                    signature,
                    catalog.Document));
            }
        }
        ValidateProviderNames(bindings);
        var resourceTypes = ProjectResourceTypes(bindings);

        var orderedDocuments = documents.Order(StringComparer.Ordinal).ToArray();
        var normalizedJson = orderedDocuments.Length == 1
            ? orderedDocuments[0]
            : string.Join("\n", orderedDocuments.Select(document =>
                $"{Encoding.UTF8.GetByteCount(document).ToString(CultureInfo.InvariantCulture)}:{document}"));
        var fingerprint = "sha256:" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(normalizedJson))).ToLowerInvariant();
        return new(target, bindings.ToImmutable(), fingerprint, resourceTypes);
    }

    private ImmutableDictionary<RawResourceTypeReference, int> ProjectResourceTypes(
        IEnumerable<RawAdapterBindingContext> bindings)
    {
        var references = new Dictionary<RawResourceTypeReference, string>();
        foreach (var document in bindings
                     .Select(binding => binding.Document)
                     .GroupBy(document => document.NormalizedJson, StringComparer.Ordinal)
                     .Select(group => group.First()))
        {
            foreach (var definition in document.Types.Where(type =>
                         type.Kind.ValueKind == JsonValueKind.String
                         && type.Kind.GetString() == "resource"))
            {
                references.Add(
                    new(document.NormalizedJson, definition.Id),
                    ResourceIdentity(document, definition));
            }
        }
        var projected = references.Values
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select((identity, id) => (identity, id))
            .ToDictionary(pair => pair.identity, pair => pair.id, StringComparer.Ordinal);
        return references.ToImmutableDictionary(
            pair => pair.Key,
            pair => projected[pair.Value]);
    }

    private string ResourceIdentity(WitDocument document, WitTypeDefinition definition)
    {
        if (definition.Id < 0 || definition.Id >= document.Types.Length
            || !ReferenceEquals(document.Types[definition.Id], definition)
            || string.IsNullOrWhiteSpace(definition.Name)
            || definition.OwnerInterface is not { } owner
            || owner < 0 || owner >= document.Interfaces.Length)
        {
            throw Invalid("raw adapter resource type identity is invalid");
        }
        var interfaceDefinition = document.Interfaces[owner] ??
            throw Invalid("raw adapter resource type owner is missing");
        return $"{_interfaces.Format(interfaceDefinition)}#{definition.Name}";
    }

    private static RawWitImportDeclaration ResolveDeclaration(
        RawWitImportCatalog catalog,
        RawWitImportLayout layout)
    {
        if (!catalog.Imports.TryGetValue(layout.Identity, out var declaration)
            || layout is RawWitImportLayout.Callable
                && declaration is not RawWitImportDeclaration.Callable
            || layout is RawWitImportLayout.Resource
                && declaration is not RawWitImportDeclaration.Resource
            || string.IsNullOrWhiteSpace(declaration.DeploymentInterfaceName))
        {
            throw Invalid(
                "raw adapter binding must retain its selected versioned WIT identity");
        }
        return declaration;
    }

    private static string Capability(RawWitImportLayout layout)
    {
        if (layout is RawWitImportLayout.Resource) return "bindResource";
        var callable = (RawWitImportLayout.Callable)layout;
        if (callable.Function.Layout.Function.InterfaceName != ReactorHostInterface)
        {
            return "bindCallable";
        }
        if (callable.Function.Declaration.Name is not ("watch" or "cancel"))
        {
            throw Invalid("raw adapter reactor-host function is unsupported");
        }
        return "bindReactor";
    }

    private static void ValidateFunctionKind(WitFunction function, WitDocument document)
    {
        ArgumentNullException.ThrowIfNull(function.Kind);
        var kind = function.Kind.Name;
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(function.Name);
        if (kind == "freestanding" && function.Kind.ResourceType is null && function.Name[0] != '[') return;
        if (kind is not ("method" or "static" or "constructor") || function.Kind.ResourceType is null)
        {
            throw Invalid("raw adapter WIT function kind is unsupported");
        }
        var prefix = $"[{kind}]";
        if (!function.Name.StartsWith(prefix, StringComparison.Ordinal) ||
            function.Name.Length == prefix.Length)
        {
            throw Invalid("raw adapter resource function identity is invalid");
        }
        var logicalName = function.Name[prefix.Length..];
        var separator = logicalName.IndexOf('.');
        if (kind == "constructor"
            ? separator >= 0
            : separator <= 0 || separator == logicalName.Length - 1)
        {
            throw Invalid("raw adapter resource function identity is invalid");
        }
        if (function.Kind.ResourceType < 0)
        {
            throw Invalid("raw adapter resource function type is invalid");
        }
        if (ResourceName(function) != ReadResourceName(document, function.Kind.ResourceType.Value))
        {
            throw Invalid("raw adapter resource function does not match its WIT resource type");
        }
    }

    private static void ValidateProviderNames(
        IEnumerable<RawAdapterBindingContext> bindings)
    {
        var operations = new HashSet<string>(StringComparer.Ordinal);
        var resources = new Dictionary<string, (int Id, string Name)>(StringComparer.Ordinal);
        foreach (var binding in bindings)
        {
            var layout = binding.Layout;
            if (layout is RawWitImportLayout.Callable callable)
            {
                var function = callable.Function.Declaration;
                ValidateFunctionKind(function, binding.Document);
                var resource = function.Kind.ResourceType?.ToString(CultureInfo.InvariantCulture) ?? "";
                var key = string.Join('\0', binding.Declaration.DeploymentInterfaceName,
                    function.Kind.Name, resource, JavaScriptProviderName(function));
                if (!operations.Add(key))
                {
                    throw Invalid("raw adapter provider operations have colliding JavaScript names");
                }
                continue;
            }
            var intrinsic = ((RawWitImportLayout.Resource)layout).Intrinsic;
            var definition = intrinsic.Declaration.Definition;
            var keyName = string.Join('\0', binding.Declaration.DeploymentInterfaceName,
                JavaScriptClassName(definition.Name!));
            var identity = (definition.Id, definition.Name!);
            if (resources.TryGetValue(keyName, out var existing) && existing != identity)
            {
                throw Invalid("raw adapter resources have colliding JavaScript names");
            }
            resources.TryAdd(keyName, identity);
        }
    }

    private static string SerializeMetadata(RawAdapterWriteContext context) => Serialize(writer =>
    {
        writer.WriteStartObject();
        writer.WriteNumber("abiVersion", CurrentAbiVersion);
        writer.WriteString("target", Target(context.Target));
        writer.WriteString("witSourceFingerprint", context.WitSourceFingerprint);
        writer.WritePropertyName("bindingIdentities");
        writer.WriteStartArray();
        foreach (var binding in context.Bindings)
        {
            WriteIdentity(writer, binding.Layout.Identity);
        }
        writer.WriteEndArray();
        writer.WritePropertyName("requiredCapabilities");
        writer.WriteStartArray();
        foreach (var capability in context.Bindings
                     .Select(binding => Capability(binding.Layout))
                     .Distinct(StringComparer.Ordinal)
                     .Order(StringComparer.Ordinal))
        {
            writer.WriteStringValue(capability);
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    });

    private static string SerializeBinding(
        RawAdapterBindingContext binding,
        RawAdapterWriteContext context) => Serialize(writer =>
    {
        var layout = binding.Layout;
        writer.WriteStartObject();
        writer.WriteString("kind", layout is RawWitImportLayout.Callable ? "callable" : "resource");
        writer.WriteString("target", Target(context.Target));
        writer.WritePropertyName("physical");
        WriteIdentity(writer, layout.Identity);
        writer.WritePropertyName("coreSignature");
        WriteCoreSignature(writer, binding.Expected);
        switch (layout)
        {
            case RawWitImportLayout.Callable callable:
                WriteCallable(
                    writer,
                    callable.Function,
                    binding.Declaration.DeploymentInterfaceName,
                    binding.Document,
                    context);
                break;
            case RawWitImportLayout.Resource resource:
                WriteResource(
                    writer,
                    resource.Intrinsic,
                    binding.Declaration.DeploymentInterfaceName,
                    binding.Document,
                    context);
                break;
        }
        writer.WriteEndObject();
    });

    private static void WriteCallable(
        Utf8JsonWriter writer,
        RawWitFunctionLayout function,
        string deploymentInterfaceName,
        WitDocument document,
        RawAdapterWriteContext context)
    {
        var canonical = function.Layout.Function;
        writer.WritePropertyName("provider");
        writer.WriteStartObject();
        writer.WriteString("interface", deploymentInterfaceName);
        writer.WriteString("function", canonical.FunctionName);
        writer.WriteString("javascriptName", JavaScriptProviderName(function.Declaration));
        writer.WriteString("functionKind", function.Declaration.Kind.Name);
        if (function.Declaration.Kind.ResourceType is { } resourceType)
        {
            writer.WriteNumber("resourceType", ResourceType(context, document, resourceType));
            var resourceName = ReadResourceName(document, resourceType);
            writer.WriteString("resourceName", resourceName);
            writer.WriteString("resourceJavaScriptName", JavaScriptClassName(resourceName));
        }
        else
        {
            writer.WriteNull("resourceType");
            writer.WriteNull("resourceName");
            writer.WriteNull("resourceJavaScriptName");
        }
        writer.WriteEndObject();
        writer.WritePropertyName("parameters");
        writer.WriteStartArray();
        for (var index = 0; index < canonical.Parameters.Length; index++)
        {
            var parameter = canonical.Parameters[index];
            writer.WriteStartObject();
            writer.WriteString("name", parameter.Name);
            writer.WriteString("javascriptName", JavaScriptName(parameter.Name));
            writer.WritePropertyName("type");
            WriteType(writer, parameter.Type, document, function.Declaration.Parameters[index].Type, context);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WritePropertyName("result");
        if (canonical.Result is null) writer.WriteNullValue();
        else WriteType(writer, canonical.Result, document, function.Declaration.Result, context);
        writer.WritePropertyName("canonicalSignature");
        WriteCanonicalSignature(writer, function.Layout.Signature, function.Layout.Target);
        writer.WritePropertyName("parameterMemory");
        WriteMemory(writer, function.Layout.ParameterMemory);
        writer.WritePropertyName("resultMemory");
        if (function.Layout.ResultMemory is null) writer.WriteNullValue();
        else WriteMemory(writer, function.Layout.ResultMemory);
    }

    private static void WriteResource(
        Utf8JsonWriter writer,
        RawResourceIntrinsicLayout intrinsic,
        string deploymentInterfaceName,
        WitDocument document,
        RawAdapterWriteContext context)
    {
        writer.WritePropertyName("resource");
        writer.WriteStartObject();
        writer.WriteString("interface", deploymentInterfaceName);
        writer.WriteNumber("type", ResourceType(
            context,
            document,
            intrinsic.Declaration.Definition.Id));
        writer.WriteString("name", intrinsic.Declaration.Definition.Name);
        writer.WriteString("javascriptName", JavaScriptClassName(intrinsic.Declaration.Definition.Name!));
        writer.WriteString("intrinsic", IntrinsicKind(intrinsic.Declaration.Kind) ??
            throw Invalid("raw adapter resource intrinsic is unsupported"));
        writer.WritePropertyName("destructor");
        if (intrinsic.Declaration.Kind == CanonicalAbiFunctionKind.ImportedResourceDrop)
        {
            writer.WriteNullValue();
        }
        else
        {
            writer.WriteStringValue(CanonicalAbiNames.Export(
                intrinsic.Declaration.InterfaceName,
                $"[resource-dtor]{intrinsic.Declaration.Definition.Name}",
                intrinsic.Target));
        }
        writer.WriteEndObject();
    }

    private static void WriteType(
        Utf8JsonWriter writer,
        CanonicalAbiType type,
        WitDocument document,
        WitTypeReference? reference,
        RawAdapterWriteContext context)
    {
        ArgumentNullException.ThrowIfNull(type);
        writer.WriteStartObject();
        writer.WriteString("kind", TypeKind(type.Kind));
        switch (type.Kind)
        {
            case CanonicalAbiTypeKind.Alias:
                writer.WritePropertyName("element");
                WriteType(writer, type.ElementType!,
                    document, ReadDefinedReference(document, reference, "type"), context);
                break;
            case CanonicalAbiTypeKind.List:
                writer.WritePropertyName("element");
                WriteType(writer, type.ElementType!,
                    document, ReadDefinedReference(document, reference, "list"), context);
                break;
            case CanonicalAbiTypeKind.Option:
                writer.WritePropertyName("element");
                WriteType(writer, type.ElementType!,
                    document, ReadDefinedReference(document, reference, "option"), context);
                break;
            case CanonicalAbiTypeKind.Record:
                WriteFields(writer, type.Fields, document,
                    ReadDefinedArray(document, reference, "record", "fields"), true, context);
                break;
            case CanonicalAbiTypeKind.Tuple:
                WriteFields(writer, type.Fields, document,
                    ReadDefinedArray(document, reference, "tuple", "types"), false, context);
                break;
            case CanonicalAbiTypeKind.Result:
                var result = ReadDefinedObject(document, reference, "result");
                writer.WritePropertyName("ok");
                WriteOptionalType(writer, type.SuccessType, document, ReadOptionalReference(result, "ok"), context);
                writer.WritePropertyName("error");
                WriteOptionalType(writer, type.ErrorType, document, ReadOptionalReference(result, "err"), context);
                break;
            case CanonicalAbiTypeKind.Variant:
                WriteCases(writer, type.Cases, document,
                    ReadDefinedArray(document, reference, "variant", "cases"), context);
                break;
            case CanonicalAbiTypeKind.Enum:
                WriteCases(writer, type.Cases, document,
                    ReadDefinedArray(document, reference, "enum", "cases"), context);
                break;
            case CanonicalAbiTypeKind.Flags:
                writer.WriteNumber("count", type.FlagsCount);
                WriteFlags(writer, ReadDefinedArray(document, reference, "flags", "flags"));
                break;
            case CanonicalAbiTypeKind.OwnedResource:
            case CanonicalAbiTypeKind.BorrowedResource:
                writer.WriteNumber("resourceType", ResourceType(
                    context,
                    document,
                    type.ResourceTypeId));
                break;
            case CanonicalAbiTypeKind.Unit:
                break;
        }
        writer.WriteEndObject();
    }

    private static void WriteFields(
        Utf8JsonWriter writer,
        ImmutableArray<CanonicalAbiField> fields,
        WitDocument document,
        JsonElement references,
        bool named,
        RawAdapterWriteContext context)
    {
        HashSet<string>? javaScriptNames = named ? new(StringComparer.Ordinal) : null;
        writer.WritePropertyName("fields");
        writer.WriteStartArray();
        for (var index = 0; index < fields.Length; index++)
        {
            var field = fields[index];
            var reference = references[index];
            var javaScriptName = named ? JavaScriptName(field.Name) : null;
            if (javaScriptName is not null && !javaScriptNames!.Add(javaScriptName))
            {
                throw Invalid("raw adapter record fields have colliding JavaScript names");
            }
            writer.WriteStartObject();
            writer.WriteString("name", field.Name);
            if (javaScriptName is not null) writer.WriteString("javascriptName", javaScriptName);
            writer.WritePropertyName("type");
            WriteType(writer, field.Type, document,
                ReadReference(named ? reference.GetProperty("type") : reference), context);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static WitTypeReference ReadDefinedReference(
        WitDocument document,
        WitTypeReference? reference,
        string kind) => ReadReference(ReadDefinedObject(document, reference, kind));

    private static JsonElement ReadDefinedArray(
        WitDocument document,
        WitTypeReference? reference,
        string kind,
        string property)
    {
        return ReadDefinedObject(document, reference, kind).GetProperty(property);
    }

    private static JsonElement ReadDefinedObject(
        WitDocument document,
        WitTypeReference? reference,
        string expectedKind)
    {
        var defined = (WitTypeReference.Defined)reference!;
        return document.Types[defined.Id].Kind.GetProperty(expectedKind);
    }

    private static WitTypeReference? ReadOptionalReference(JsonElement value, string property)
    {
        var reference = value.GetProperty(property);
        return reference.ValueKind == JsonValueKind.Null ? null : ReadReference(reference);
    }

    private static WitTypeReference ReadReference(JsonElement value) =>
        value.ValueKind == JsonValueKind.String
            ? new WitTypeReference.Primitive(value.GetString()!)
            : new WitTypeReference.Defined(value.GetInt32());

    private static void WriteCases(
        Utf8JsonWriter writer,
        ImmutableArray<CanonicalAbiCase> cases,
        WitDocument document,
        JsonElement references,
        RawAdapterWriteContext context)
    {
        writer.WritePropertyName("cases");
        writer.WriteStartArray();
        for (var index = 0; index < cases.Length; index++)
        {
            var @case = cases[index];
            var reference = references[index];
            writer.WriteStartObject();
            writer.WriteString("name", @case.Name);
            writer.WritePropertyName("type");
            WriteOptionalType(writer, @case.Type, document,
                reference.TryGetProperty("type", out var type) && type.ValueKind != JsonValueKind.Null
                    ? ReadReference(type)
                    : null,
                context);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static void WriteFlags(Utf8JsonWriter writer, JsonElement flags)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        writer.WritePropertyName("flags");
        writer.WriteStartArray();
        foreach (var flag in flags.EnumerateArray())
        {
            var name = flag.GetProperty("name").GetString();
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            var javaScriptName = JavaScriptName(name);
            if (!names.Add(javaScriptName))
            {
                throw Invalid("raw adapter flags have colliding JavaScript names");
            }
            writer.WriteStartObject();
            writer.WriteString("name", name);
            writer.WriteString("javascriptName", javaScriptName);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static void WriteOptionalType(
        Utf8JsonWriter writer,
        CanonicalAbiType? type,
        WitDocument document,
        WitTypeReference? reference,
        RawAdapterWriteContext context)
    {
        if (type is null) writer.WriteNullValue();
        else WriteType(writer, type, document, reference, context);
    }

    private static void WriteCanonicalSignature(
        Utf8JsonWriter writer,
        CanonicalAbiCoreSignature signature,
        WasmTarget target)
    {
        writer.WriteStartObject();
        WriteCliValues(writer, "parameters", signature.Parameters, target);
        writer.WritePropertyName("result");
        if (signature.Result == CliValueKind.Void) writer.WriteNullValue();
        else writer.WriteStringValue(CoreType(Project(signature.Result, target)));
        WriteCliValues(writer, "flatParameters", signature.FlatParameters, target);
        WriteCliValues(writer, "flatResults", signature.FlatResults, target);
        writer.WriteBoolean("indirectParameters", signature.IndirectParameters);
        writer.WriteBoolean("indirectResult", signature.IndirectResult);
        writer.WriteEndObject();
    }

    private static void WriteCliValues(
        Utf8JsonWriter writer,
        string property,
        ImmutableArray<CliValueKind> values,
        WasmTarget target)
    {
        writer.WritePropertyName(property);
        writer.WriteStartArray();
        foreach (var value in values) writer.WriteStringValue(CoreType(Project(value, target)));
        writer.WriteEndArray();
    }

    private static void WriteCoreSignature(Utf8JsonWriter writer, RawCoreFunctionImportSignature signature)
    {
        writer.WriteStartObject();
        WriteCoreValues(writer, "parameters", signature.Parameters);
        WriteCoreValues(writer, "results", signature.Results);
        writer.WriteEndObject();
    }

    private static void WriteCoreValues(
        Utf8JsonWriter writer,
        string property,
        ImmutableArray<RawCoreValueType> values)
    {
        writer.WritePropertyName(property);
        writer.WriteStartArray();
        foreach (var value in values) writer.WriteStringValue(CoreType(value));
        writer.WriteEndArray();
    }

    private static void WriteMemory(Utf8JsonWriter writer, CanonicalAbiMemoryLayout memory)
    {
        writer.WriteStartObject();
        writer.WriteNumber("size", memory.Size);
        writer.WriteNumber("alignment", memory.Alignment);
        writer.WriteNumber("discriminantSize", memory.DiscriminantSize);
        writer.WriteNumber("payloadOffset", memory.PayloadOffset);
        writer.WritePropertyName("fields");
        writer.WriteStartArray();
        foreach (var field in memory.Fields)
        {
            writer.WriteStartObject();
            writer.WriteString("name", field.Field.Name);
            writer.WriteNumber("offset", field.Offset);
            writer.WritePropertyName("layout");
            WriteMemory(writer, field.Layout);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteIdentity(Utf8JsonWriter writer, RawCanonicalImportIdentity identity)
    {
        writer.WriteStartObject();
        writer.WriteString("module", identity.Module);
        writer.WriteString("name", identity.Name);
        writer.WriteEndObject();
    }

    private static RawCoreValueType Project(CliValueKind value, WasmTarget target) => value switch
    {
        CliValueKind.I4 or CliValueKind.ValueType => RawCoreValueType.I32,
        CliValueKind.I8 => RawCoreValueType.I64,
        CliValueKind.F4 => RawCoreValueType.F32,
        CliValueKind.F8 => RawCoreValueType.F64,
        CliValueKind.NativeInt or CliValueKind.ManagedReference or CliValueKind.ManagedAddress =>
            target == WasmTarget.Wasm64 ? RawCoreValueType.I64 : RawCoreValueType.I32,
        _ => throw Invalid("raw adapter canonical signature contains an unsupported core value type"),
    };

    private static string? IntrinsicKind(CanonicalAbiFunctionKind kind) => kind switch
    {
        CanonicalAbiFunctionKind.ImportedResourceDrop => "imported-resource-drop",
        CanonicalAbiFunctionKind.ExportedResourceNew => "exported-resource-new",
        CanonicalAbiFunctionKind.ExportedResourceRep => "exported-resource-rep",
        CanonicalAbiFunctionKind.ExportedResourceDrop => "exported-resource-drop",
        _ => null,
    };

    private static string Target(WasmTarget target) => target == WasmTarget.Wasm32 ? "wasm32" : "wasm64";

    private static string CoreType(RawCoreValueType type) => type switch
    {
        RawCoreValueType.I32 => "i32",
        RawCoreValueType.I64 => "i64",
        RawCoreValueType.F32 => "f32",
        RawCoreValueType.F64 => "f64",
        _ => throw Invalid("raw adapter signature contains an unsupported core value type"),
    };

    private static string TypeKind(CanonicalAbiTypeKind kind) => kind switch
    {
        CanonicalAbiTypeKind.Unit => "unit",
        CanonicalAbiTypeKind.Bool => "bool",
        CanonicalAbiTypeKind.S8 => "s8",
        CanonicalAbiTypeKind.U8 => "u8",
        CanonicalAbiTypeKind.S16 => "s16",
        CanonicalAbiTypeKind.U16 => "u16",
        CanonicalAbiTypeKind.S32 => "s32",
        CanonicalAbiTypeKind.U32 => "u32",
        CanonicalAbiTypeKind.S64 => "s64",
        CanonicalAbiTypeKind.U64 => "u64",
        CanonicalAbiTypeKind.F32 => "f32",
        CanonicalAbiTypeKind.F64 => "f64",
        CanonicalAbiTypeKind.Character => "character",
        CanonicalAbiTypeKind.Text => "text",
        CanonicalAbiTypeKind.Alias => "alias",
        CanonicalAbiTypeKind.List => "list",
        CanonicalAbiTypeKind.Record => "record",
        CanonicalAbiTypeKind.Tuple => "tuple",
        CanonicalAbiTypeKind.Option => "option",
        CanonicalAbiTypeKind.Result => "result",
        CanonicalAbiTypeKind.Variant => "variant",
        CanonicalAbiTypeKind.Enum => "enum",
        CanonicalAbiTypeKind.Flags => "flags",
        CanonicalAbiTypeKind.OwnedResource => "owned-resource",
        CanonicalAbiTypeKind.BorrowedResource => "borrowed-resource",
        _ => throw Invalid("raw adapter contains an unsupported canonical type"),
    };

    private static string JavaScriptName(string witName)
    {
        var marker = witName.LastIndexOf(']');
        var name = marker >= 0 ? witName[(marker + 1)..] : witName;
        var member = name.LastIndexOf('.');
        return Camel(member >= 0 ? name[(member + 1)..] : name, false);
    }

    private static string JavaScriptClassName(string witName) => Camel(witName, true);

    private static string JavaScriptProviderName(WitFunction function) =>
        function.Kind.Name == "constructor"
            ? JavaScriptClassName(function.Name[(function.Name.LastIndexOf(']') + 1)..])
            : JavaScriptName(function.Name);

    private static string ResourceName(WitFunction function)
    {
        var value = function.Name[(function.Name.LastIndexOf(']') + 1)..];
        var separator = value.IndexOf('.');
        return separator < 0 ? value : value[..separator];
    }

    private static string ReadResourceName(WitDocument document, int resourceType)
    {
        if ((uint)resourceType >= (uint)document.Types.Length)
        {
            throw Invalid("raw adapter resource function type is invalid");
        }
        var definition = document.Types[resourceType];
        if (definition.Id != resourceType || string.IsNullOrWhiteSpace(definition.Name) ||
            definition.Kind.ValueKind != JsonValueKind.String ||
            definition.Kind.GetString() != "resource")
        {
            throw Invalid("raw adapter resource function type is invalid");
        }
        return definition.Name;
    }

    private static int ResourceType(
        RawAdapterWriteContext context,
        WitDocument document,
        int resourceType)
    {
        if (!context.ResourceTypes.TryGetValue(
                new(document.NormalizedJson, resourceType),
                out var projected))
        {
            throw Invalid("raw adapter resource type has no deployment identity");
        }
        return projected;
    }

    private static string Camel(string value, bool upperFirst)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var result = new StringBuilder(value.Length);
        var upper = upperFirst;
        foreach (var character in value)
        {
            if (character == '-')
            {
                upper = true;
                continue;
            }
            result.Append(upper ? char.ToUpperInvariant(character) : character);
            upper = false;
        }
        if (result.Length == 0) throw Invalid("raw adapter JavaScript name is invalid");
        return result.ToString();
    }

    private static string SerializeStrings(IEnumerable<string> values) => Serialize(writer =>
    {
        writer.WriteStartArray();
        foreach (var value in values.Order(StringComparer.Ordinal)) writer.WriteStringValue(value);
        writer.WriteEndArray();
    });

    private static string Serialize(Action<Utf8JsonWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer)) write(writer);
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static CompilerException Invalid(string message) => ComponentException.Invalid(message);

    private sealed record RawAdapterWriteContext(
        WasmTarget Target,
        ImmutableArray<RawAdapterBindingContext> Bindings,
        string WitSourceFingerprint,
        ImmutableDictionary<RawResourceTypeReference, int> ResourceTypes);

    private readonly record struct RawResourceTypeReference
    {
        private readonly string _witSource;
        private readonly int _localType;

        public RawResourceTypeReference(string witSource, int localType)
        {
            _witSource = witSource;
            _localType = localType;
        }
    }

    private sealed record RawAdapterBindingContext(
        RawWitImportLayout Layout,
        RawWitImportDeclaration Declaration,
        RawCoreFunctionImportSignature Expected,
        WitDocument Document);

    private const string CommonSource = """
        function bind(capability, binding) {
          if (typeof capability !== "function") {
            throw new TypeError(`raw adapter ${binding.kind} capability is invalid`);
          }
          const value = capability(binding);
          if (typeof value !== "function") {
            throw new TypeError(`raw adapter ${binding.kind} binding is invalid`);
          }
          return value;
        }

        function addBinding(imports, binding, value) {
          const { module, name } = binding.physical;
          const target = imports[module] ?? Object.create(null);
          if (Object.hasOwn(target, name)) {
            throw new TypeError("raw adapter binding identity is duplicated");
          }
          target[name] = value;
          imports[module] = target;
        }

        function assertExactObject(value, keys, label) {
          if (value === null || typeof value !== "object" || Array.isArray(value)
              || Object.getOwnPropertySymbols(value).length !== 0) {
            throw new TypeError(`${label} is invalid`);
          }
          const descriptors = Object.getOwnPropertyDescriptors(value);
          const actualKeys = Object.keys(descriptors).sort();
          if (actualKeys.length !== keys.length
              || actualKeys.some((key, index) => key !== keys[index])
              || Object.values(descriptors).some(descriptor => !descriptor.enumerable
                || !("value" in descriptor))) {
            throw new TypeError(`${label} shape is invalid`);
          }
        }

        function deepFreeze(value) {
          if (value !== null && typeof value === "object" && !Object.isFrozen(value)) {
            for (const child of Object.values(value)) deepFreeze(child);
            Object.freeze(value);
          }
          return value;
        }
        """;
}
