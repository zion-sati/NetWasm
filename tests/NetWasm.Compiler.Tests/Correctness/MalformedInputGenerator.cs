using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

namespace NetWasm.Compiler.Tests.Correctness;

internal sealed class MalformedInputGenerator : IMalformedInputGenerator
{
    public ImmutableArray<MalformedInputMutation> Generate(
        string validAssembly,
        string outputDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(validAssembly);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        Directory.CreateDirectory(outputDirectory);
        var original = File.ReadAllBytes(validAssembly);
        using var stream = new MemoryStream(original, writable: false);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        var method = FindMethod(reader, "EntryPoint", "Run");
        var definition = reader.GetMethodDefinition(method);
        var body = pe.GetMethodBody(definition.RelativeVirtualAddress);
        var bodyOffset = RvaToFileOffset(pe, definition.RelativeVirtualAddress);
        var headerSize = MethodHeaderSize(original.AsSpan(bodyOffset));
        var ilOffset = checked(bodyOffset + headerSize);
        var ilLength = body.GetILBytes()?.Length ??
                       throw new InvalidDataException("baseline Run body has no CIL");
        if (ilLength < 2)
        {
            throw new InvalidDataException("baseline Run body is too small to mutate");
        }
        var normalized = NormalizeMethod(original, ilOffset, ilLength);
        File.WriteAllBytes(Path.Combine(outputDirectory, "valid-baseline.dll"), normalized);
        var metadataOffset = FindSequence(original, "BSJB"u8);
        return
        [
            Write(
                normalized,
                outputDirectory,
                "unsupported-opcode",
                "unknown-opcode",
                ilOffset,
                [0xff]),
            Write(
                normalized,
                outputDirectory,
                "truncated-branch-operand",
                "truncated-operand",
                ilOffset + ilLength - 1,
                [OpCodeByte(System.Reflection.Emit.OpCodes.Br)]),
            Write(
                normalized,
                outputDirectory,
                "out-of-range-branch",
                "invalid-branch-target",
                ilOffset,
                [OpCodeByte(System.Reflection.Emit.OpCodes.Br_S), 0x7f]),
            Write(
                normalized,
                outputDirectory,
                "invalid-argument-index",
                "invalid-argument-index",
                ilOffset,
                [OpCodeByte(System.Reflection.Emit.OpCodes.Ldarg_S), byte.MaxValue]),
            Write(
                normalized,
                outputDirectory,
                "invalid-local-index",
                "invalid-local-index",
                ilOffset,
                [OpCodeByte(System.Reflection.Emit.OpCodes.Ldloc_S), byte.MaxValue]),
            Write(
                normalized,
                outputDirectory,
                "evaluation-stack-underflow",
                "evaluation-stack-underflow",
                ilOffset,
                [OpCodeByte(System.Reflection.Emit.OpCodes.Pop)]),
            Write(
                normalized,
                outputDirectory,
                "invalid-return-stack",
                "invalid-return-stack",
                ilOffset,
                [OpCodeByte(System.Reflection.Emit.OpCodes.Ret)]),
            Write(
                normalized,
                outputDirectory,
                "evaluation-stack-type-mismatch",
                "evaluation-stack-type-mismatch",
                ilOffset,
                [
                    OpCodeByte(System.Reflection.Emit.OpCodes.Ldnull),
                    OpCodeByte(System.Reflection.Emit.OpCodes.Ldc_I4_0),
                    OpCodeByte(System.Reflection.Emit.OpCodes.Add),
                    OpCodeByte(System.Reflection.Emit.OpCodes.Pop),
                ]),
            Write(
                normalized,
                outputDirectory,
                "evaluation-stack-merge-mismatch",
                "evaluation-stack-merge-mismatch",
                ilOffset,
                [
                    OpCodeByte(System.Reflection.Emit.OpCodes.Ldc_I4_0),
                    OpCodeByte(System.Reflection.Emit.OpCodes.Brtrue_S),
                    0x01,
                    OpCodeByte(System.Reflection.Emit.OpCodes.Ldc_I4_1),
                    OpCodeByte(System.Reflection.Emit.OpCodes.Ret),
                ]),
            Write(
                normalized,
                outputDirectory,
                "invalid-method-token",
                "invalid-metadata-token",
                ilOffset,
                [
                    OpCodeByte(System.Reflection.Emit.OpCodes.Call),
                    0xff, 0xff, 0xff, 0xff,
                ]),
            Write(
                normalized,
                outputDirectory,
                "truncated-prefix",
                "malformed-prefix",
                ilOffset + ilLength - 1,
                [OpCodeByte(System.Reflection.Emit.OpCodes.Prefix1)]),
            Write(
                normalized,
                outputDirectory,
                "invalid-metadata-signature",
                "invalid-metadata-root",
                metadataOffset,
                [0x00]),
        ];
    }

    public ImmutableArray<MalformedInputMutation> GenerateExceptionHandling(
        string validAssembly,
        string outputDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(validAssembly);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        Directory.CreateDirectory(outputDirectory);
        var original = File.ReadAllBytes(validAssembly);
        using var stream = new MemoryStream(original, writable: false);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        var method = FindMethod(reader, "EntryPoint", "Run");
        var definition = reader.GetMethodDefinition(method);
        var body = pe.GetMethodBody(definition.RelativeVirtualAddress);
        var bodyOffset = RvaToFileOffset(pe, definition.RelativeVirtualAddress);
        var headerSize = MethodHeaderSize(original.AsSpan(bodyOffset));
        var ilOffset = checked(bodyOffset + headerSize);
        var filter = body.ExceptionRegions.Single(region =>
            region.Kind == ExceptionRegionKind.Filter);
        var @finally = body.ExceptionRegions.Single(region =>
            region.Kind == ExceptionRegionKind.Finally);
        var filterEnd = FindOpCode(
            original,
            checked(ilOffset + filter.FilterOffset),
            checked(filter.HandlerOffset - filter.FilterOffset),
            System.Reflection.Emit.OpCodes.Endfilter);
        var finallyEnd = FindOpCode(
            original,
            checked(ilOffset + @finally.HandlerOffset),
            @finally.HandlerLength,
            System.Reflection.Emit.OpCodes.Endfinally);
        return
        [
            Write(
                original,
                outputDirectory,
                "branch-into-filter",
                "illegal-branch-into-filter-region",
                checked(ilOffset + filter.TryOffset),
                Branch(
                    System.Reflection.Emit.OpCodes.Br,
                    filter.TryOffset,
                    filter.FilterOffset)),
            Write(
                original,
                outputDirectory,
                "branch-into-handler",
                "illegal-branch-into-handler-region",
                checked(ilOffset + filter.TryOffset),
                Branch(
                    System.Reflection.Emit.OpCodes.Br,
                    filter.TryOffset,
                    filter.HandlerOffset)),
            Write(
                original,
                outputDirectory,
                "branch-out-of-protected-region",
                "illegal-branch-out-of-protected-region",
                checked(ilOffset + filter.TryOffset),
                Branch(
                    System.Reflection.Emit.OpCodes.Br,
                    filter.TryOffset,
                    checked(@finally.HandlerOffset + @finally.HandlerLength))),
            Write(
                original,
                outputDirectory,
                "endfilter-outside-filter",
                "endfilter-outside-filter-region",
                checked(ilOffset + filter.TryOffset),
                OpCodeBytes(System.Reflection.Emit.OpCodes.Endfilter)),
            Write(
                original,
                outputDirectory,
                "endfinally-outside-finally",
                "endfinally-outside-finally-region",
                checked(ilOffset + filter.TryOffset),
                OpCodeBytes(System.Reflection.Emit.OpCodes.Endfinally)),
            Write(
                original,
                outputDirectory,
                "return-from-finally",
                "illegal-return-from-finally-region",
                checked(ilOffset + @finally.HandlerOffset),
                OpCodeBytes(System.Reflection.Emit.OpCodes.Ret)),
            Write(
                original,
                outputDirectory,
                "leave-from-finally",
                "illegal-leave-from-finally-region",
                checked(ilOffset + @finally.HandlerOffset),
                Branch(
                    System.Reflection.Emit.OpCodes.Leave,
                    @finally.HandlerOffset,
                    checked(@finally.HandlerOffset + @finally.HandlerLength))),
            Write(
                original,
                outputDirectory,
                "missing-endfilter",
                "filter-region-missing-endfilter",
                filterEnd,
                [0x00, 0x00]),
            Write(
                original,
                outputDirectory,
                "missing-endfinally",
                "finally-region-missing-endfinally",
                finallyEnd,
                [0x00]),
        ];
    }

    private static byte[] NormalizeMethod(byte[] original, int ilOffset, int ilLength)
    {
        var normalized = original.ToArray();
        var body = normalized.AsSpan(ilOffset, ilLength);
        body.Fill(OpCodeByte(System.Reflection.Emit.OpCodes.Nop));
        body[^2] = OpCodeByte(System.Reflection.Emit.OpCodes.Ldc_I4_0);
        body[^1] = OpCodeByte(System.Reflection.Emit.OpCodes.Ret);
        return normalized;
    }

    private static MalformedInputMutation Write(
        byte[] original,
        string directory,
        string name,
        string invariant,
        int offset,
        ReadOnlySpan<byte> replacement)
    {
        if (offset < 0 || offset > original.Length - replacement.Length)
        {
            throw new InvalidDataException($"mutation '{name}' is outside the assembly");
        }
        var mutated = original.ToArray();
        var originalBytes = mutated.AsSpan(offset, replacement.Length).ToArray();
        replacement.CopyTo(mutated.AsSpan(offset));
        var path = Path.Combine(directory, name + ".dll");
        File.WriteAllBytes(path, mutated);
        return new(
            name,
            invariant,
            path,
            Convert.ToHexStringLower(SHA256.HashData(mutated)),
            offset,
            Convert.ToHexStringLower(originalBytes),
            Convert.ToHexStringLower(replacement));
    }

    private static byte OpCodeByte(System.Reflection.Emit.OpCode opCode)
    {
        if (opCode.Size != 1)
        {
            throw new InvalidOperationException(
                $"opcode {opCode.Name} is not a one-byte CIL opcode");
        }
        return unchecked((byte)opCode.Value);
    }

    private static byte[] OpCodeBytes(System.Reflection.Emit.OpCode opCode)
    {
        var value = unchecked((ushort)opCode.Value);
        return opCode.Size switch
        {
            1 => [(byte)value],
            2 => [(byte)(value >> 8), (byte)value],
            _ => throw new InvalidOperationException(
                $"opcode {opCode.Name} has invalid size {opCode.Size}"),
        };
    }

    private static byte[] Branch(
        System.Reflection.Emit.OpCode opCode,
        int instructionOffset,
        int targetOffset)
    {
        if (opCode.OperandType != System.Reflection.Emit.OperandType.InlineBrTarget ||
            opCode.Size != 1)
        {
            throw new InvalidOperationException(
                $"opcode {opCode.Name} is not a long branch");
        }
        var result = new byte[sizeof(byte) + sizeof(int)];
        result[0] = OpCodeByte(opCode);
        BinaryPrimitives.WriteInt32LittleEndian(
            result.AsSpan(1),
            checked(targetOffset - instructionOffset - result.Length));
        return result;
    }

    private static int FindOpCode(
        byte[] image,
        int offset,
        int length,
        System.Reflection.Emit.OpCode opCode)
    {
        var bytes = OpCodeBytes(opCode);
        var region = image.AsSpan(offset, length);
        var relative = region.IndexOf(bytes);
        if (relative < 0)
        {
            throw new InvalidDataException(
                $"opcode {opCode.Name} is absent from the expected EH region");
        }
        return checked(offset + relative);
    }

    private static MethodDefinitionHandle FindMethod(
        MetadataReader reader,
        string typeName,
        string methodName)
    {
        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(typeHandle);
            if (reader.GetString(type.Name) != typeName)
            {
                continue;
            }
            foreach (var methodHandle in type.GetMethods())
            {
                if (reader.GetString(reader.GetMethodDefinition(methodHandle).Name) ==
                    methodName)
                {
                    return methodHandle;
                }
            }
        }
        throw new InvalidDataException($"missing mutation target {typeName}::{methodName}");
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

    private static int MethodHeaderSize(ReadOnlySpan<byte> header) =>
        (header[0] & 3) switch
        {
            2 => 1,
            3 => (BinaryPrimitives.ReadUInt16LittleEndian(header) >> 12) * 4,
            _ => throw new InvalidDataException("unsupported method header format"),
        };

    private static int FindSequence(byte[] source, ReadOnlySpan<byte> sequence)
    {
        for (var offset = 0; offset <= source.Length - sequence.Length; offset++)
        {
            if (source.AsSpan(offset, sequence.Length).SequenceEqual(sequence))
            {
                return offset;
            }
        }
        throw new InvalidDataException("metadata signature was not found");
    }
}
