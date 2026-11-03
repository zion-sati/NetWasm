using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace NetWasm.Compiler.Tests.Correctness;

// The released compatibility libraries the qualification pins were built against an older
// CoreLib. Every CoreLib type and member they reference must still exist, with the same
// signature, or the compiler rejects them where they are used; a member kept only for them is
// internal, as their references to CoreLib internals are.
public sealed class CompatibilityLibraryLinkTests
{
    // The reference assemblies the compiler maps onto CoreLib.
    private static readonly ImmutableHashSet<string> CoreLibNames =
    [
        "NetWasm.CoreLib", "System.Private.CoreLib", "System.Runtime", "System.Collections",
        "System.Reflection", "System.Runtime.InteropServices", "System.Text",
        "System.Runtime.CompilerServices.Unsafe", "System.Buffers",
    ];

    [Fact]
    public void EveryCoreLibMemberTheCompatibilityLibrariesReferenceExists()
    {
        var environment = CompilerCorrectnessEnvironment.Discover();
        var coreLib = CoreLibMembers(environment.CoreLibPath);
        var missing = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var path in SourceGenerationFixtureBuild.CreateNetWasmReferences(
            environment.RepositoryRoot, usesStjDependencies: true, usesRegexDependencies: true))
        {
            using var stream = File.OpenRead(path);
            using var assembly = new PEReader(stream);
            var metadata = assembly.GetMetadataReader();
            var names = new SignatureNames();
            foreach (var handle in metadata.TypeReferences)
            {
                if (FromCoreLib(metadata, handle) && !coreLib.ContainsKey(SignatureNames.Name(metadata, handle)))
                {
                    missing.Add($"{Path.GetFileName(path)}: {SignatureNames.Name(metadata, handle)}");
                }
            }
            foreach (var handle in metadata.MemberReferences)
            {
                var member = metadata.GetMemberReference(handle);
                if (Owner(metadata, member.Parent) is not { } owner || !FromCoreLib(metadata, owner) ||
                    !coreLib.TryGetValue(SignatureNames.Name(metadata, owner), out var members))
                {
                    continue;
                }
                var key = member.GetKind() == MemberReferenceKind.Field
                    ? metadata.GetString(member.Name)
                    : Key(metadata.GetString(member.Name), member.DecodeMethodSignature(names, null));
                if (!members.Contains(key))
                {
                    missing.Add($"{Path.GetFileName(path)}: {SignatureNames.Name(metadata, owner)}::{key}");
                }
            }
        }

        Assert.Empty(missing);
    }

    private static Dictionary<string, HashSet<string>> CoreLibMembers(string path)
    {
        using var stream = File.OpenRead(path);
        using var assembly = new PEReader(stream);
        var metadata = assembly.GetMetadataReader();
        var names = new SignatureNames();
        var types = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var handle in metadata.TypeDefinitions)
        {
            var type = metadata.GetTypeDefinition(handle);
            var members = type.GetFields()
                .Select(field => metadata.GetString(metadata.GetFieldDefinition(field).Name))
                .ToHashSet(StringComparer.Ordinal);
            foreach (var methodHandle in type.GetMethods())
            {
                var method = metadata.GetMethodDefinition(methodHandle);
                members.Add(Key(metadata.GetString(method.Name), method.DecodeSignature(names, null)));
            }
            types[SignatureNames.Name(metadata, handle)] = members;
        }
        return types;
    }

    private static string Key(string name, MethodSignature<string> signature) =>
        $"{name}`{signature.GenericParameterCount}({string.Join(",", signature.ParameterTypes)}):{signature.ReturnType}";

    // The type a member reference belongs to, through a generic instantiation's definition.
    private static TypeReferenceHandle? Owner(MetadataReader metadata, EntityHandle parent)
    {
        if (parent.Kind == HandleKind.TypeReference) return (TypeReferenceHandle)parent;
        if (parent.Kind != HandleKind.TypeSpecification) return null;
        var blob = metadata.GetBlobReader(metadata.GetTypeSpecification((TypeSpecificationHandle)parent).Signature);
        if (blob.ReadSignatureTypeCode() != SignatureTypeCode.GenericTypeInstance) return null;
        blob.ReadSignatureTypeCode();
        var type = blob.ReadTypeHandle();
        return type.Kind == HandleKind.TypeReference ? (TypeReferenceHandle)type : null;
    }

    private static bool FromCoreLib(MetadataReader metadata, TypeReferenceHandle handle)
    {
        var reference = metadata.GetTypeReference(handle);
        while (reference.ResolutionScope.Kind == HandleKind.TypeReference)
        {
            reference = metadata.GetTypeReference((TypeReferenceHandle)reference.ResolutionScope);
        }
        return reference.ResolutionScope.Kind == HandleKind.AssemblyReference &&
            CoreLibNames.Contains(metadata.GetString(
                metadata.GetAssemblyReference((AssemblyReferenceHandle)reference.ResolutionScope).Name));
    }

    // Types as names, so a reference's signature and a definition's compare as text.
    private sealed class SignatureNames : ISignatureTypeProvider<string, object?>
    {
        public string GetArrayType(string elementType, ArrayShape shape) => $"{elementType}[{new string(',', shape.Rank - 1)}]";
        public string GetByReferenceType(string elementType) => elementType + "&";
        public string GetFunctionPointerType(MethodSignature<string> signature) =>
            $"method {signature.ReturnType}({string.Join(",", signature.ParameterTypes)})";
        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) =>
            $"{genericType}<{string.Join(",", typeArguments)}>";
        public string GetGenericMethodParameter(object? genericContext, int index) => $"!!{index}";
        public string GetGenericTypeParameter(object? genericContext, int index) => $"!{index}";
        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
        public string GetPinnedType(string elementType) => elementType;
        public string GetPointerType(string elementType) => elementType + "*";
        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();
        public string GetSZArrayType(string elementType) => elementType + "[]";
        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => Name(reader, handle);
        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => Name(reader, handle);
        public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) =>
            reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

        public static string Name(MetadataReader metadata, TypeDefinitionHandle handle)
        {
            var type = metadata.GetTypeDefinition(handle);
            var declaring = type.GetDeclaringType();
            return declaring.IsNil
                ? Qualified(metadata.GetString(type.Namespace), metadata.GetString(type.Name))
                : $"{Name(metadata, declaring)}/{metadata.GetString(type.Name)}";
        }

        public static string Name(MetadataReader metadata, TypeReferenceHandle handle)
        {
            var reference = metadata.GetTypeReference(handle);
            return reference.ResolutionScope.Kind == HandleKind.TypeReference
                ? $"{Name(metadata, (TypeReferenceHandle)reference.ResolutionScope)}/{metadata.GetString(reference.Name)}"
                : Qualified(metadata.GetString(reference.Namespace), metadata.GetString(reference.Name));
        }

        private static string Qualified(string @namespace, string name) =>
            @namespace.Length == 0 ? name : $"{@namespace}.{name}";
    }
}
