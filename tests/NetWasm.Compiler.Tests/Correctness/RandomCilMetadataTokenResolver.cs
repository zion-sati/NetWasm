using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace NetWasm.Compiler.Tests.Correctness;

internal sealed class RandomCilMetadataTokenResolver : IRandomCilMetadataTokenResolver
{
    public RandomCilMetadataTokens Resolve(string assemblyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath);
        using var stream = File.OpenRead(assemblyPath);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        var box = FindType(reader, "Box");
        var boxInterface = FindType(reader, "IBox");
        var staticTransformInterface = FindType(reader, "IStaticTransform");
        var staticTransform = FindType(reader, "StaticTransform");
        var defaultBoxInterface = FindType(reader, "IDefaultBox");
        var defaultBox = FindType(reader, "DefaultBox");
        var baseBox = FindType(reader, "BaseBox");
        var derivedBox = FindType(reader, "DerivedBox");
        var entry = FindType(reader, "EntryPoint");
        return new(
            FindMethod(reader, box, ".ctor"),
            FindField(reader, box, "Value"),
            FindMethod(reader, box, "Add"),
            FindField(reader, entry, "StaticValue"),
            FindMethod(reader, entry, "Helper"),
            FindMethod(reader, entry, "HelperUnary"),
            FindCallSite(reader),
            FindUserStringToken(pe, reader, entry, "StringValue"),
            FindMemberReference(reader, "GetHashCode"),
            FindTypeReference(reader, "System", "Int32"),
            MetadataTokens.GetToken(box),
            FindMethod(reader, boxInterface, "Add"),
            FindInitializerField(pe, reader, entry),
            FindMemberReference(reader, "InitializeArray"),
            MetadataTokens.GetToken(staticTransform),
            FindMethod(reader, staticTransformInterface, "Apply"),
            FindMethod(reader, defaultBox, ".ctor"),
            FindMethod(reader, defaultBoxInterface, "Add"),
            FindMethod(reader, derivedBox, ".ctor"),
            FindMethod(reader, baseBox, "Copy"),
            FindMethod(reader, baseBox, "Read"),
            FindTypeReference(reader, "System", "Exception"),
            FindMethod(reader, entry, "RangeSlice"));
    }

    private static int FindMemberReference(MetadataReader reader, string name)
    {
        var matches = reader.MemberReferences
            .Where(handle => reader.GetString(reader.GetMemberReference(handle).Name) == name)
            .ToArray();
        if (matches.Length != 1)
        {
            throw new InvalidDataException(
                $"random CIL metadata member '{name}' resolved {matches.Length} times");
        }
        return MetadataTokens.GetToken(matches[0]);
    }

    private static int FindUserStringToken(
        PEReader pe,
        MetadataReader reader,
        TypeDefinitionHandle type,
        string methodName)
    {
        var methodToken = FindMethod(reader, type, methodName);
        var definition = reader.GetMethodDefinition(
            (MethodDefinitionHandle)MetadataTokens.EntityHandle(methodToken));
        var bytes = pe.GetMethodBody(definition.RelativeVirtualAddress).GetILBytes()
            ?? throw new InvalidDataException("string token method has no CIL");
        var opcode = unchecked((byte)System.Reflection.Emit.OpCodes.Ldstr.Value);
        for (var offset = 0; offset <= bytes.Length - 5; offset++)
        {
            if (bytes[offset] == opcode)
            {
                return System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(
                    bytes.AsSpan(offset + 1, sizeof(int)));
            }
        }
        throw new InvalidDataException("random CIL user-string token was not found");
    }

    private static int FindCallSite(MetadataReader reader)
    {
        var count = reader.GetTableRowCount(TableIndex.StandAloneSig);
        var matches = Enumerable.Range(1, count)
            .Select(MetadataTokens.StandaloneSignatureHandle)
            .Where(handle => reader.GetStandaloneSignature(handle).GetKind() ==
                StandaloneSignatureKind.Method)
            .ToArray();
        if (matches.Length != 1)
        {
            throw new InvalidDataException(
                $"random CIL metadata expected one call-site signature, found " +
                matches.Length);
        }
        return MetadataTokens.GetToken(matches[0]);
    }

    private static int FindInitializerField(
        PEReader pe,
        MetadataReader reader,
        TypeDefinitionHandle entry)
    {
        var initializer = reader.GetMethodDefinition(
            (MethodDefinitionHandle)MetadataTokens.EntityHandle(
                FindMethod(reader, entry, ".cctor")));
        var bytes = pe.GetMethodBody(initializer.RelativeVirtualAddress).GetILBytes()
            ?? throw new InvalidDataException("random CIL static constructor has no body");
        var seedField = FindField(reader, entry, "SeedData");
        var storeOpcode = unchecked((byte)System.Reflection.Emit.OpCodes.Stsfld.Value);
        var tokenOpcode = unchecked((byte)System.Reflection.Emit.OpCodes.Ldtoken.Value);
        for (var store = 0; store <= bytes.Length - 5; store++)
        {
            if (bytes[store] != storeOpcode ||
                System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(
                    bytes.AsSpan(store + 1, sizeof(int))) != seedField)
            {
                continue;
            }
            for (var token = store - 5; token >= 0; token--)
            {
                if (bytes[token] == tokenOpcode)
                {
                    return System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(
                        bytes.AsSpan(token + 1, sizeof(int)));
                }
            }
        }
        throw new InvalidDataException("random CIL SeedData initializer token was not found");
    }

    private static TypeDefinitionHandle FindType(MetadataReader reader, string name)
    {
        foreach (var handle in reader.TypeDefinitions)
        {
            if (reader.GetString(reader.GetTypeDefinition(handle).Name) == name)
            {
                return handle;
            }
        }
        throw new InvalidDataException($"random CIL metadata type '{name}' was not found");
    }

    private static int FindTypeReference(
        MetadataReader reader,
        string @namespace,
        string name)
    {
        foreach (var handle in reader.TypeReferences)
        {
            var reference = reader.GetTypeReference(handle);
            if (reader.GetString(reference.Namespace) == @namespace &&
                reader.GetString(reference.Name) == name)
            {
                return MetadataTokens.GetToken(handle);
            }
        }
        throw new InvalidDataException(
            $"random CIL metadata type '{@namespace}.{name}' was not found");
    }

    private static int FindMethod(
        MetadataReader reader,
        TypeDefinitionHandle typeHandle,
        string name)
    {
        foreach (var handle in reader.GetTypeDefinition(typeHandle).GetMethods())
        {
            if (reader.GetString(reader.GetMethodDefinition(handle).Name) == name)
            {
                return MetadataTokens.GetToken(handle);
            }
        }
        throw new InvalidDataException($"random CIL metadata method '{name}' was not found");
    }

    private static int FindField(
        MetadataReader reader,
        TypeDefinitionHandle typeHandle,
        string name)
    {
        foreach (var handle in reader.GetTypeDefinition(typeHandle).GetFields())
        {
            if (reader.GetString(reader.GetFieldDefinition(handle).Name) == name)
            {
                return MetadataTokens.GetToken(handle);
            }
        }
        throw new InvalidDataException($"random CIL metadata field '{name}' was not found");
    }
}
