using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace NetWasm.Compiler.Tests;

// Type-level metadata of NetWasm's implementation assemblies against .NET's, for every public type both define: the
// TypeAttributes a program can read (serializable, sealed, abstract, layout, kind, BeforeFieldInit), the custom
// attributes on the type, and the arguments of those whose values change behaviour or diagnostics. Compiler-written
// markers (nullability, ref safety and the like) are left out, and debugger and IntelliSense attributes are reported
// as their own kind. One row per difference, sorted, so a recorded audit shows any change as a diff.
internal static class FrameworkTypeAttributeAudit
{
    public const string Header = "kind\ttype\tdetail";

    // Compiler-written or tooling-only attributes, not part of a type's authored contract.
    private static readonly HashSet<string> Ignored = new(StringComparer.Ordinal)
    {
        "System.Runtime.CompilerServices.NullableAttribute",
        "System.Runtime.CompilerServices.NullableContextAttribute",
        "System.Runtime.CompilerServices.CompilerGeneratedAttribute",
        "System.Runtime.CompilerServices.IntrinsicAttribute",
        "System.Runtime.Versioning.NonVersionableAttribute",
        "System.Runtime.CompilerServices.TypeDependencyAttribute",
        "System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute",
        "System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessageAttribute",
        "System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute",
        "System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute",
        "System.Diagnostics.CodeAnalysis.RequiresDynamicCodeAttribute",
        "System.Runtime.CompilerServices.RefSafetyRulesAttribute",
        "System.Runtime.CompilerServices.ScopedRefAttribute",
        "System.Diagnostics.CodeAnalysis.ExperimentalAttribute",
        "System.Runtime.InteropServices.ClassInterfaceAttribute",
        "System.Runtime.InteropServices.ComVisibleAttribute",
        "System.Runtime.Versioning.UnsupportedOSPlatformAttribute",
        "System.Runtime.Versioning.SupportedOSPlatformAttribute",
        "System.Runtime.Versioning.RequiresPreviewFeaturesAttribute",
        "System.Runtime.CompilerServices.EagerStaticClassConstructionAttribute",
        "System.Runtime.CompilerServices.ReflectionBlockedAttribute",
        "System.Runtime.InteropServices.Marshalling.CustomMarshallerAttribute",
        "System.Runtime.InteropServices.Marshalling.NativeMarshallingAttribute",
        "System.Runtime.InteropServices.Marshalling.ContiguousCollectionMarshallerAttribute",
        "System.Runtime.CompilerServices.ExtensionAttribute",
        "System.Runtime.CompilerServices.CompilerFeatureRequiredAttribute",
        "System.Runtime.CompilerServices.IsByRefLikeAttribute",
    };

    // Attributes only debuggers and editors read.
    private static readonly HashSet<string> Cosmetic = new(StringComparer.Ordinal)
    {
        "System.Diagnostics.DebuggerDisplayAttribute",
        "System.Diagnostics.DebuggerTypeProxyAttribute",
        "System.ComponentModel.EditorBrowsableAttribute",
        "System.Diagnostics.DebuggerNonUserCodeAttribute",
        "System.Diagnostics.DebuggerStepThroughAttribute",
        "System.Diagnostics.StackTraceHiddenAttribute",
        "System.Diagnostics.DebuggerBrowsableAttribute",
    };

    // Attributes whose arguments are compared, not only their presence.
    private static readonly HashSet<string> Decoded = new(StringComparer.Ordinal)
    {
        "System.AttributeUsageAttribute",
        "System.Reflection.DefaultMemberAttribute",
        "System.ObsoleteAttribute",
        "System.Runtime.CompilerServices.TypeForwardedFromAttribute",
        "System.CLSCompliantAttribute",
        "System.Runtime.InteropServices.GuidAttribute",
        "System.Diagnostics.ConditionalAttribute",
    };

    public static IReadOnlyList<string> Compute(IEnumerable<string> netWasm, IEnumerable<string> upstream)
    {
        var ours = Load(netWasm);
        var theirs = Load(upstream);
        var rows = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (name, our) in ours)
        {
            if (!theirs.TryGetValue(name, out var their))
            {
                continue;
            }
            void Report(string kind, string detail = "") => rows.Add($"{kind}\t{name}\t{detail}");

#pragma warning disable SYSLIB0050 // Serializable is the flag the audit compares, not formatter use.
            Compare(TypeAttributes.Serializable, "serializable");
#pragma warning restore SYSLIB0050
            if (our.Kind is "class")
            {
                Compare(TypeAttributes.Sealed, "sealed");
                Compare(TypeAttributes.Abstract, "abstract");
            }
            if ((our.Attributes & TypeAttributes.LayoutMask) != (their.Attributes & TypeAttributes.LayoutMask))
            {
                Report("layout", $"{Layout(our.Attributes)} upstream {Layout(their.Attributes)}");
            }
            if (our.Kind != their.Kind)
            {
                Report("kind", $"{our.Kind} upstream {their.Kind}");
            }
            Compare(TypeAttributes.BeforeFieldInit, "beforefieldinit");
            foreach (var attribute in their.CustomAttributes.Keys.Except(our.CustomAttributes.Keys).Where(Audited))
            {
                Report(Cosmetic.Contains(attribute) ? "missing-cosmetic" : "missing", Describe(attribute, their));
            }
            foreach (var attribute in our.CustomAttributes.Keys.Except(their.CustomAttributes.Keys).Where(Audited))
            {
                Report(Cosmetic.Contains(attribute) ? "extra-cosmetic" : "extra", Describe(attribute, our));
            }
            foreach (var attribute in our.CustomAttributes.Keys.Intersect(their.CustomAttributes.Keys))
            {
                if (Audited(attribute) && !Cosmetic.Contains(attribute) &&
                    Normalize(attribute, our.CustomAttributes[attribute]) != Normalize(attribute, their.CustomAttributes[attribute]))
                {
                    Report("arguments", $"{Describe(attribute, our)} upstream {their.CustomAttributes[attribute]}");
                }
            }

            void Compare(TypeAttributes flag, string kind)
            {
                var mine = (our.Attributes & flag) != 0;
                if (mine != ((their.Attributes & flag) != 0))
                {
                    Report(mine ? $"{kind}-extra" : $"{kind}-missing");
                }
            }
        }
        return [Header, .. rows];
    }

    private static bool Audited(string attribute) => !Ignored.Contains(attribute);

    private static string Describe(string attribute, TypeRecord type) => attribute + type.CustomAttributes[attribute];

    private static string Layout(TypeAttributes attributes) => (attributes & TypeAttributes.LayoutMask) switch
    {
        TypeAttributes.AutoLayout => "auto",
        TypeAttributes.SequentialLayout => "sequential",
        TypeAttributes.ExplicitLayout => "explicit",
        _ => "?",
    };

    // AttributeUsage with its defaults (AllowMultiple false, Inherited true) spelled out, so equal usages compare equal.
    private static string Normalize(string attribute, string value)
    {
        if (attribute != "System.AttributeUsageAttribute" || value.Length < 2)
        {
            return value;
        }
        var parts = value[1..^1].Split(',');
        var allowMultiple = parts.FirstOrDefault(part => part.StartsWith("AllowMultiple=", StringComparison.Ordinal))?.Split('=')[1] ?? "False";
        var inherited = parts.FirstOrDefault(part => part.StartsWith("Inherited=", StringComparison.Ordinal))?.Split('=')[1] ?? "True";
        return $"({parts[0]},AllowMultiple={allowMultiple},Inherited={inherited})";
    }

    private static Dictionary<string, TypeRecord> Load(IEnumerable<string> files)
    {
        var result = new Dictionary<string, TypeRecord>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            using var stream = File.OpenRead(file);
            using var image = new PEReader(stream);
            if (!image.HasMetadata)
            {
                continue;
            }
            var metadata = image.GetMetadataReader();
            if (!metadata.IsAssembly)
            {
                continue;
            }
            foreach (var handle in metadata.TypeDefinitions)
            {
                var type = metadata.GetTypeDefinition(handle);
                if (!IsVisible(metadata, type))
                {
                    continue;
                }
                var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var attributeHandle in type.GetCustomAttributes())
                {
                    var attribute = metadata.GetCustomAttribute(attributeHandle);
                    var name = AttributeTypeName(metadata, attribute.Constructor);
                    attributes[name] = Decoded.Contains(name) ? Arguments(attribute) : "";
                }
                var baseType = type.BaseType.IsNil ? "" : TypeName(metadata, type.BaseType);
                var kind = (type.Attributes & TypeAttributes.Interface) != 0 ? "interface"
                    : baseType == "System.Enum" ? "enum"
                    : baseType == "System.ValueType" ? "struct"
                    : baseType == "System.MulticastDelegate" ? "delegate"
                    : "class";
                result.TryAdd(FullName(metadata, handle), new TypeRecord(type.Attributes, kind, attributes));
            }
        }
        return result;
    }

    private static string Arguments(CustomAttribute attribute)
    {
        try
        {
            var value = attribute.DecodeValue(new ArgumentTypeProvider());
            return "(" + string.Join(",", value.FixedArguments.Select(argument => Format(argument.Value))) +
                string.Concat(value.NamedArguments.Select(argument => $",{argument.Name}={Format(argument.Value)}")) + ")";
        }
        catch (BadImageFormatException)
        {
            return "(undecodable)";
        }
    }

    private static string Format(object? value) => value switch
    {
        null => "null",
        string text => "\"" + text + "\"",
        ImmutableArray<CustomAttributeTypedArgument<string>> items => "[" + string.Join(",", items.Select(item => Format(item.Value))) + "]",
        _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)!,
    };

    private static bool IsVisible(MetadataReader metadata, TypeDefinition type) => (type.Attributes & TypeAttributes.VisibilityMask) switch
    {
        TypeAttributes.Public => true,
        TypeAttributes.NestedPublic or TypeAttributes.NestedFamily or TypeAttributes.NestedFamORAssem =>
            IsVisible(metadata, metadata.GetTypeDefinition(type.GetDeclaringType())),
        _ => false,
    };

    private static string FullName(MetadataReader metadata, TypeDefinitionHandle handle)
    {
        var type = metadata.GetTypeDefinition(handle);
        var name = metadata.GetString(type.Name);
        if (!type.GetDeclaringType().IsNil)
        {
            return FullName(metadata, type.GetDeclaringType()) + "+" + name;
        }
        var space = metadata.GetString(type.Namespace);
        return space.Length == 0 ? name : space + "." + name;
    }

    private static string TypeName(MetadataReader metadata, EntityHandle handle) => handle.Kind switch
    {
        HandleKind.TypeDefinition => FullName(metadata, (TypeDefinitionHandle)handle),
        HandleKind.TypeReference => ReferenceName(metadata, (TypeReferenceHandle)handle),
        _ => "?",
    };

    private static string ReferenceName(MetadataReader metadata, TypeReferenceHandle handle)
    {
        var reference = metadata.GetTypeReference(handle);
        if (reference.ResolutionScope.Kind == HandleKind.TypeReference)
        {
            return ReferenceName(metadata, (TypeReferenceHandle)reference.ResolutionScope) + "+" + metadata.GetString(reference.Name);
        }
        var space = metadata.GetString(reference.Namespace);
        return space.Length == 0 ? metadata.GetString(reference.Name) : space + "." + metadata.GetString(reference.Name);
    }

    private static string AttributeTypeName(MetadataReader metadata, EntityHandle constructor) => constructor.Kind switch
    {
        HandleKind.MethodDefinition => FullName(metadata, metadata.GetMethodDefinition((MethodDefinitionHandle)constructor).GetDeclaringType()),
        HandleKind.MemberReference => TypeName(metadata, metadata.GetMemberReference((MemberReferenceHandle)constructor).Parent),
        _ => "?",
    };

    private sealed record TypeRecord(TypeAttributes Attributes, string Kind, Dictionary<string, string> CustomAttributes);

    // Enough of a type provider to decode the audited attributes' arguments; enums decode as their int values.
    private sealed class ArgumentTypeProvider : ICustomAttributeTypeProvider<string>
    {
        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();
        public string GetSystemType() => "System.Type";
        public string GetSZArrayType(string elementType) => elementType + "[]";
        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) =>
            reader.GetString(reader.GetTypeDefinition(handle).Name);
        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) =>
            reader.GetString(reader.GetTypeReference(handle).Name);
        public string GetTypeFromSerializedName(string name) => name;
        public PrimitiveTypeCode GetUnderlyingEnumType(string type) => PrimitiveTypeCode.Int32;
        public bool IsSystemType(string type) => type is "System.Type" or "Type";
    }
}
