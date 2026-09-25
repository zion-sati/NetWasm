using System.Buffers.Binary;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace NetWasm.Compiler.Tests.Correctness;

internal sealed record PortableExecutableMethodLocation(
    int HeaderOffset,
    int HeaderSize,
    int IlOffset,
    int IlLength,
    bool HasFatHeader,
    int? ExceptionSectionOffset,
    int ExceptionSectionSize,
    bool HasFatExceptionClauses);

internal static class PortableExecutableMethodLocator
{
    public static PortableExecutableMethodLocation Locate(
        byte[] image,
        string fullTypeName,
        string methodName)
    {
        using var stream = new MemoryStream(image, writable: false);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        var method = FindMethod(reader, fullTypeName, methodName);
        var definition = reader.GetMethodDefinition(method);
        var body = pe.GetMethodBody(definition.RelativeVirtualAddress);
        var headerOffset = RvaToFileOffset(pe, definition.RelativeVirtualAddress);
        var header = image.AsSpan(headerOffset);
        var kind = header[0] & 3;
        var fat = kind == 3;
        var headerSize = kind switch
        {
            2 => 1,
            3 => (BinaryPrimitives.ReadUInt16LittleEndian(header) >> 12) * 4,
            _ => throw new InvalidDataException("unsupported method header format"),
        };
        var ilLength = body.GetILBytes()?.Length ??
            throw new InvalidDataException(
                $"method {fullTypeName}::{methodName} has no CIL");
        int? exceptionSectionOffset = null;
        var exceptionSectionSize = 0;
        var hasFatExceptionClauses = false;
        if (fat &&
            (BinaryPrimitives.ReadUInt16LittleEndian(header) & 0x0008) != 0)
        {
            exceptionSectionOffset = Align4(checked(headerOffset + headerSize + ilLength));
            var section = image.AsSpan(exceptionSectionOffset.Value);
            hasFatExceptionClauses = (section[0] & 0x40) != 0;
            exceptionSectionSize = hasFatExceptionClauses
                ? section[1] | section[2] << 8 | section[3] << 16
                : section[1];
        }
        return new(
            headerOffset,
            headerSize,
            checked(headerOffset + headerSize),
            ilLength,
            fat,
            exceptionSectionOffset,
            exceptionSectionSize,
            hasFatExceptionClauses);
    }

    private static MethodDefinitionHandle FindMethod(
        MetadataReader reader,
        string fullTypeName,
        string methodName)
    {
        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(typeHandle);
            var name = reader.GetString(type.Name);
            var @namespace = reader.GetString(type.Namespace);
            var fullName = string.IsNullOrEmpty(@namespace)
                ? name
                : @namespace + "." + name;
            if (!StringComparer.Ordinal.Equals(fullName, fullTypeName))
            {
                continue;
            }
            foreach (var methodHandle in type.GetMethods())
            {
                if (StringComparer.Ordinal.Equals(
                        reader.GetString(reader.GetMethodDefinition(methodHandle).Name),
                        methodName))
                {
                    return methodHandle;
                }
            }
        }
        throw new InvalidDataException(
            $"missing CIL target {fullTypeName}::{methodName}");
    }

    private static int RvaToFileOffset(PEReader pe, int rva)
    {
        foreach (var section in pe.PEHeaders.SectionHeaders)
        {
            var length = Math.Max(section.VirtualSize, section.SizeOfRawData);
            if (rva >= section.VirtualAddress &&
                rva < section.VirtualAddress + length)
            {
                return checked(section.PointerToRawData + rva - section.VirtualAddress);
            }
        }
        throw new InvalidDataException($"RVA 0x{rva:x8} is outside PE sections");
    }

    private static int Align4(int value) => checked((value + 3) & ~3);
}
