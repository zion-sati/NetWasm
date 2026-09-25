using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection.Emit;
using System.Security.Cryptography;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Correctness;

internal sealed class MethodBodyPatcher : IMethodBodyPatcher
{
    public static void RewriteExceptionRegionKind(
        string assemblyPath,
        string typeName,
        string methodName,
        CilExceptionRegionKind from,
        CilExceptionRegionKind to)
    {
        var image = File.ReadAllBytes(assemblyPath);
        var location = PortableExecutableMethodLocator.Locate(
            image,
            typeName,
            methodName);
        if (location.ExceptionSectionOffset is not int sectionOffset)
        {
            throw new InvalidDataException("method has no exception section");
        }
        var clauseSize = location.HasFatExceptionClauses ? 24 : 12;
        var clauseCount = checked((location.ExceptionSectionSize - 4) / clauseSize);
        var fromFlag = ClauseFlags(from);
        var toFlag = ClauseFlags(to);
        var rewritten = 0;
        for (var index = 0; index < clauseCount; index++)
        {
            var clause = image.AsSpan(
                checked(sectionOffset + 4 + index * clauseSize),
                clauseSize);
            var flag = location.HasFatExceptionClauses
                ? BinaryPrimitives.ReadUInt32LittleEndian(clause)
                : BinaryPrimitives.ReadUInt16LittleEndian(clause);
            if (flag != fromFlag)
            {
                continue;
            }
            if (location.HasFatExceptionClauses)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(clause, toFlag);
            }
            else
            {
                BinaryPrimitives.WriteUInt16LittleEndian(clause, checked((ushort)toFlag));
            }
            rewritten++;
        }
        if (rewritten != 1)
        {
            throw new InvalidDataException(
                $"expected one {from} exception clause but found {rewritten}");
        }
        File.WriteAllBytes(assemblyPath, image);
    }

    public PatchedMethodBody Patch(
        string assemblyPath,
        string typeName,
        string methodName,
        ReadOnlySpan<byte> cil,
        int maxStack)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(methodName);
        if (maxStack is < 0 or > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(maxStack));
        }

        var image = File.ReadAllBytes(assemblyPath);
        var location = PortableExecutableMethodLocator.Locate(
            image,
            typeName,
            methodName);
        if (!location.HasFatHeader)
        {
            throw new InvalidDataException(
                $"generated CIL target {typeName}::{methodName} needs a fat header");
        }
        if (cil.Length > location.IlLength ||
            (cil.Length != location.IlLength && cil.Length > location.IlLength - 2))
        {
            throw new InvalidDataException(
                $"generated CIL needs {cil.Length} bytes but method body capacity is " +
                location.IlLength);
        }

        BinaryPrimitives.WriteUInt16LittleEndian(
            image.AsSpan(location.HeaderOffset + sizeof(ushort), sizeof(ushort)),
            checked((ushort)maxStack));
        var body = image.AsSpan(location.IlOffset, location.IlLength);
        cil.CopyTo(body);
        if (cil.Length < body.Length)
        {
            body[cil.Length..].Fill(OpCodeByte(OpCodes.Nop));
            body[^2] = OpCodeByte(OpCodes.Ldc_I4_0);
            body[^1] = OpCodeByte(OpCodes.Ret);
        }
        File.WriteAllBytes(assemblyPath, image);
        return new(
            assemblyPath,
            Convert.ToHexStringLower(SHA256.HashData(image)),
            location.IlLength,
            cil.Length);
    }

    public PatchedMethodBody Patch(
        string assemblyPath,
        string typeName,
        string methodName,
        SerializedGeneratedMethod method,
        int maxStack)
    {
        ArgumentNullException.ThrowIfNull(method);
        _ = Patch(
            assemblyPath,
            typeName,
            methodName,
            method.Cil,
            maxStack);
        var image = File.ReadAllBytes(assemblyPath);
        var location = PortableExecutableMethodLocator.Locate(
            image,
            typeName,
            methodName);
        WriteExceptionRegions(image, location, method.ExceptionRegions);
        File.WriteAllBytes(assemblyPath, image);
        return new(
            assemblyPath,
            Convert.ToHexStringLower(SHA256.HashData(image)),
            location.IlLength,
            method.Cil.Length);
    }

    private static void WriteExceptionRegions(
        byte[] image,
        PortableExecutableMethodLocation location,
        ImmutableArray<SerializedGeneratedExceptionRegion> regions)
    {
        if (regions.IsEmpty)
        {
            ClearExceptionRegions(image, location);
            return;
        }
        if (location.ExceptionSectionOffset is not int sectionOffset)
        {
            throw new InvalidDataException(
                "generated exception regions need a reserved method-data section");
        }
        var clauseSize = location.HasFatExceptionClauses ? 24 : 12;
        var capacity = checked((location.ExceptionSectionSize - 4) / clauseSize);
        if (capacity < regions.Length)
        {
            throw new InvalidDataException(
                $"generated exception regions need {regions.Length} clauses but " +
                $"the target reserves {capacity}");
        }
        SetExceptionSectionSize(image, location, regions.Length, clauseSize);
        image.AsSpan(
            checked(sectionOffset + 4 + regions.Length * clauseSize),
            checked((capacity - regions.Length) * clauseSize)).Clear();
        for (var index = 0; index < regions.Length; index++)
        {
            var clause = image.AsSpan(
                checked(sectionOffset + 4 + index * clauseSize),
                clauseSize);
            if (location.HasFatExceptionClauses)
            {
                WriteFatClause(clause, regions[index]);
            }
            else
            {
                WriteSmallClause(clause, regions[index]);
            }
        }
    }

    private static void ClearExceptionRegions(
        byte[] image,
        PortableExecutableMethodLocation location)
    {
        const ushort moreSectionsFlag = 0x0008;
        var flags = BinaryPrimitives.ReadUInt16LittleEndian(
            image.AsSpan(location.HeaderOffset, sizeof(ushort)));
        BinaryPrimitives.WriteUInt16LittleEndian(
            image.AsSpan(location.HeaderOffset, sizeof(ushort)),
            (ushort)(flags & ~moreSectionsFlag));
        if (location.ExceptionSectionOffset is int sectionOffset)
        {
            image.AsSpan(sectionOffset, location.ExceptionSectionSize).Clear();
        }
    }

    private static void SetExceptionSectionSize(
        byte[] image,
        PortableExecutableMethodLocation location,
        int regionCount,
        int clauseSize)
    {
        var sectionOffset = location.ExceptionSectionOffset!.Value;
        var sectionSize = checked(4 + regionCount * clauseSize);
        if (location.HasFatExceptionClauses)
        {
            image[sectionOffset + 1] = (byte)sectionSize;
            image[sectionOffset + 2] = (byte)(sectionSize >> 8);
            image[sectionOffset + 3] = (byte)(sectionSize >> 16);
            return;
        }

        image[sectionOffset + 1] = checked((byte)sectionSize);
    }

    private static void WriteFatClause(
        Span<byte> clause,
        SerializedGeneratedExceptionRegion region)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(clause, ClauseFlags(region.Kind));
        BinaryPrimitives.WriteInt32LittleEndian(clause[4..], region.TryOffset);
        BinaryPrimitives.WriteInt32LittleEndian(clause[8..], region.TryLength);
        BinaryPrimitives.WriteInt32LittleEndian(clause[12..], region.HandlerOffset);
        BinaryPrimitives.WriteInt32LittleEndian(clause[16..], region.HandlerLength);
        BinaryPrimitives.WriteInt32LittleEndian(clause[20..], ClauseValue(region));
    }

    private static void WriteSmallClause(
        Span<byte> clause,
        SerializedGeneratedExceptionRegion region)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(
            clause,
            checked((ushort)ClauseFlags(region.Kind)));
        BinaryPrimitives.WriteUInt16LittleEndian(
            clause[2..],
            checked((ushort)region.TryOffset));
        clause[4] = checked((byte)region.TryLength);
        BinaryPrimitives.WriteUInt16LittleEndian(
            clause[5..],
            checked((ushort)region.HandlerOffset));
        clause[7] = checked((byte)region.HandlerLength);
        BinaryPrimitives.WriteInt32LittleEndian(clause[8..], ClauseValue(region));
    }

    private static uint ClauseFlags(CilExceptionRegionKind kind) => kind switch
    {
        CilExceptionRegionKind.Catch => 0,
        CilExceptionRegionKind.Filter => 1,
        CilExceptionRegionKind.Finally => 2,
        CilExceptionRegionKind.Fault => 4,
        _ => throw new InvalidDataException($"unknown generated EH kind {kind}"),
    };

    private static int ClauseValue(SerializedGeneratedExceptionRegion region) =>
        region.Kind switch
        {
            CilExceptionRegionKind.Catch => region.CatchTypeToken ??
                throw new InvalidDataException("generated catch has no type token"),
            CilExceptionRegionKind.Filter => region.FilterOffset ??
                throw new InvalidDataException("generated filter has no offset"),
            _ => 0,
        };

    private static byte OpCodeByte(OpCode opCode)
    {
        if (opCode.Size != 1)
        {
            throw new InvalidOperationException(
                $"opcode {opCode.Name} is not a one-byte CIL opcode");
        }
        return unchecked((byte)opCode.Value);
    }
}
