using System.Collections.Immutable;
using System.Reflection.Emit;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Correctness;

internal sealed class GeneratedCilSerializer(
    IGeneratedCilValidator validator) : IGeneratedCilSerializer
{
    private static readonly ImmutableDictionary<CilOperation, Encoding> Encodings =
        CreateEncodings();

    public byte[] Serialize(GeneratedCilProgram program)
        => SerializeMethod(program).Cil;

    public SerializedGeneratedMethod SerializeMethod(GeneratedCilProgram program)
    {
        validator.Validate(program);
        var offsets = new Dictionary<int, int>();
        var offset = 0;
        foreach (var block in program.Blocks)
        {
            offsets.Add(block.Id, offset);
            foreach (var instruction in block.Instructions)
            {
                offset = checked(offset + Size(instruction));
            }
        }

        using var stream = new MemoryStream(offset);
        using var writer = new BinaryWriter(stream);
        foreach (var block in program.Blocks)
        {
            foreach (var instruction in block.Instructions)
            {
                var instructionOffset = checked((int)stream.Position);
                Write(writer, instruction, instructionOffset, offsets);
            }
        }
        var cil = stream.ToArray();
        var regions = program.ExceptionRegions.Select(region =>
        {
            var tryOffset = offsets[region.TryStartBlock];
            var tryEnd = offsets[region.TryEndBlock];
            var handlerOffset = offsets[region.HandlerStartBlock];
            var handlerEnd = offsets[region.HandlerEndBlock];
            return new SerializedGeneratedExceptionRegion(
                region.Kind,
                tryOffset,
                checked(tryEnd - tryOffset),
                handlerOffset,
                checked(handlerEnd - handlerOffset),
                region.FilterStartBlock is int filterStart
                    ? offsets[filterStart]
                    : null,
                region.CatchTypeToken);
        }).ToImmutableArray();
        return new(cil, regions);
    }

    private static int Size(GeneratedCilInstruction instruction)
    {
        var encoding = GetEncoding(instruction);
        return checked(encoding.OpCode.Size + encoding.Operand switch
        {
            EncodingOperand.None => 0,
            EncodingOperand.ByteIndex => sizeof(byte),
            EncodingOperand.Int32 or EncodingOperand.Branch or EncodingOperand.Token =>
                sizeof(int),
            EncodingOperand.Int64 or EncodingOperand.Float64 => sizeof(long),
            EncodingOperand.Float32 => sizeof(float),
            EncodingOperand.Switch => sizeof(int) +
                (sizeof(int) * Require<GeneratedCilOperand.BlockTargets>(instruction)
                    .BlockIds.Length),
            _ => throw new InvalidOperationException(
                $"unknown generated operand encoding {encoding.Operand}"),
        });
    }

    private static void Write(
        BinaryWriter writer,
        GeneratedCilInstruction instruction,
        int instructionOffset,
        Dictionary<int, int> blockOffsets)
    {
        var encoding = GetEncoding(instruction);
        WriteOpCode(writer, encoding.OpCode);
        var nextOffset = checked(instructionOffset + Size(instruction));
        switch (encoding.Operand)
        {
            case EncodingOperand.None:
                ValidateImplicitOperand(instruction);
                break;
            case EncodingOperand.Int32:
                writer.Write(Require<GeneratedCilOperand.Int32>(instruction).Value);
                break;
            case EncodingOperand.Int64:
                writer.Write(Require<GeneratedCilOperand.Int64>(instruction).Value);
                break;
            case EncodingOperand.Float32:
                writer.Write(Require<GeneratedCilOperand.Float32>(instruction).Value);
                break;
            case EncodingOperand.Float64:
                writer.Write(Require<GeneratedCilOperand.Float64>(instruction).Value);
                break;
            case EncodingOperand.Token:
                writer.Write(Require<GeneratedCilOperand.MetadataToken>(instruction).Value);
                break;
            case EncodingOperand.ByteIndex:
                writer.Write(checked((byte)Require<GeneratedCilOperand.Index>(instruction)
                    .Value));
                break;
            case EncodingOperand.Branch:
                var target = Require<GeneratedCilOperand.BlockTarget>(instruction).BlockId;
                writer.Write(checked(blockOffsets[target] - nextOffset));
                break;
            case EncodingOperand.Switch:
                var targets = Require<GeneratedCilOperand.BlockTargets>(instruction).BlockIds;
                writer.Write(targets.Length);
                foreach (var blockId in targets)
                {
                    writer.Write(checked(blockOffsets[blockId] - nextOffset));
                }
                break;
            default:
                throw new InvalidOperationException(
                    $"unknown generated operand encoding {encoding.Operand}");
        }
    }

    private static void ValidateImplicitOperand(GeneratedCilInstruction instruction)
    {
        if (instruction.Operation is CilOperation.LoadArgument or
            CilOperation.LoadArgumentAddress or CilOperation.LoadLocal or
            CilOperation.LoadLocalAddress or CilOperation.StoreLocal or
            CilOperation.Unaligned)
        {
            _ = Require<GeneratedCilOperand.Index>(instruction);
            return;
        }
        if (instruction.Operation == CilOperation.ConvertNumeric)
        {
            _ = Require<GeneratedCilOperand.NumericConversion>(instruction);
            return;
        }
        if (instruction.Operation is CilOperation.LoadArrayElement or
            CilOperation.StoreArrayElement)
        {
            _ = Require<GeneratedCilOperand.MetadataToken>(instruction);
            return;
        }
        _ = Require<GeneratedCilOperand.None>(instruction);
    }

    private static void WriteOpCode(BinaryWriter writer, OpCode opCode)
    {
        var value = unchecked((ushort)opCode.Value);
        if (opCode.Size == 1)
        {
            writer.Write((byte)value);
            return;
        }
        writer.Write((byte)(value >> 8));
        writer.Write((byte)value);
    }

    private static Encoding GetEncoding(GeneratedCilInstruction instruction)
    {
        if (instruction.Operation == CilOperation.ConvertNumeric)
        {
            return new(ResolveNumericConversion(
                Require<GeneratedCilOperand.NumericConversion>(instruction)),
                EncodingOperand.None);
        }
        if (instruction.Operation is CilOperation.LoadArrayElement or
            CilOperation.StoreArrayElement)
        {
            var token = Require<GeneratedCilOperand.MetadataToken>(instruction);
            if (token.StackKind != CliValueKind.I4)
            {
                throw new InvalidDataException(
                    $"random typed array operation does not support {token.StackKind}");
            }
            return new(
                instruction.Operation == CilOperation.LoadArrayElement
                    ? OpCodes.Ldelem_I4
                    : OpCodes.Stelem_I4,
                EncodingOperand.None);
        }
        return Encodings.TryGetValue(instruction.Operation, out var encoding)
            ? encoding
            : throw new InvalidDataException(
                $"random CIL serializer has no encoding for {instruction.Operation}");
    }

    private static OpCode ResolveNumericConversion(
        GeneratedCilOperand.NumericConversion conversion)
    {
        if (conversion.Native)
        {
            return conversion.Checked
                ? conversion.DestinationUnsigned
                    ? conversion.SourceUnsigned ? OpCodes.Conv_Ovf_U_Un : OpCodes.Conv_Ovf_U
                    : conversion.SourceUnsigned ? OpCodes.Conv_Ovf_I_Un : OpCodes.Conv_Ovf_I
                : conversion.DestinationUnsigned ? OpCodes.Conv_U : OpCodes.Conv_I;
        }

        return (conversion.BitWidth, conversion.DestinationUnsigned,
            conversion.Checked, conversion.SourceUnsigned) switch
        {
            (8, false, false, _) => OpCodes.Conv_I1,
            (8, true, false, _) => OpCodes.Conv_U1,
            (16, false, false, _) => OpCodes.Conv_I2,
            (16, true, false, _) => OpCodes.Conv_U2,
            (32, false, false, _) => OpCodes.Conv_I4,
            (32, true, false, _) => OpCodes.Conv_U4,
            (64, false, false, _) => OpCodes.Conv_I8,
            (64, true, false, _) => OpCodes.Conv_U8,
            (8, false, true, false) => OpCodes.Conv_Ovf_I1,
            (8, false, true, true) => OpCodes.Conv_Ovf_I1_Un,
            (8, true, true, false) => OpCodes.Conv_Ovf_U1,
            (8, true, true, true) => OpCodes.Conv_Ovf_U1_Un,
            (16, false, true, false) => OpCodes.Conv_Ovf_I2,
            (16, false, true, true) => OpCodes.Conv_Ovf_I2_Un,
            (16, true, true, false) => OpCodes.Conv_Ovf_U2,
            (16, true, true, true) => OpCodes.Conv_Ovf_U2_Un,
            (32, false, true, false) => OpCodes.Conv_Ovf_I4,
            (32, false, true, true) => OpCodes.Conv_Ovf_I4_Un,
            (32, true, true, false) => OpCodes.Conv_Ovf_U4,
            (32, true, true, true) => OpCodes.Conv_Ovf_U4_Un,
            (64, false, true, false) => OpCodes.Conv_Ovf_I8,
            (64, false, true, true) => OpCodes.Conv_Ovf_I8_Un,
            (64, true, true, false) => OpCodes.Conv_Ovf_U8,
            (64, true, true, true) => OpCodes.Conv_Ovf_U8_Un,
            _ => throw new InvalidDataException(
                $"unsupported generated numeric conversion {conversion}"),
        };
    }

    private static T Require<T>(GeneratedCilInstruction instruction)
        where T : GeneratedCilOperand => instruction.Operand as T ??
            throw new InvalidDataException(
                $"{instruction.Operation} has operand {instruction.Operand.GetType().Name}, " +
                $"expected {typeof(T).Name}");

    private static ImmutableDictionary<CilOperation, Encoding> CreateEncodings()
    {
        var result = ImmutableDictionary.CreateBuilder<CilOperation, Encoding>();
        Add(result, CilOperation.Nop, OpCodes.Nop);
        Add(result, CilOperation.Break, OpCodes.Break);
        Add(result, CilOperation.LoadArgument, OpCodes.Ldarg_0);
        Add(result, CilOperation.LoadArgumentAddress, OpCodes.Ldarga_S,
            EncodingOperand.ByteIndex);
        Add(result, CilOperation.StoreArgument, OpCodes.Starg_S,
            EncodingOperand.ByteIndex);
        Add(result, CilOperation.LoadLocal, OpCodes.Ldloc_S,
            EncodingOperand.ByteIndex);
        Add(result, CilOperation.LoadLocalAddress, OpCodes.Ldloca_S,
            EncodingOperand.ByteIndex);
        Add(result, CilOperation.StoreLocal, OpCodes.Stloc_S,
            EncodingOperand.ByteIndex);
        Add(result, CilOperation.LoadInt32, OpCodes.Ldc_I4, EncodingOperand.Int32);
        Add(result, CilOperation.LoadInt64, OpCodes.Ldc_I8, EncodingOperand.Int64);
        Add(result, CilOperation.LoadFloat32, OpCodes.Ldc_R4, EncodingOperand.Float32);
        Add(result, CilOperation.LoadFloat64, OpCodes.Ldc_R8, EncodingOperand.Float64);
        Add(result, CilOperation.LoadNull, OpCodes.Ldnull);
        Add(result, CilOperation.LoadString, OpCodes.Ldstr, EncodingOperand.Token);
        Add(result, CilOperation.Duplicate, OpCodes.Dup);
        Add(result, CilOperation.Pop, OpCodes.Pop);
        Add(result, CilOperation.LoadField, OpCodes.Ldfld, EncodingOperand.Token);
        Add(result, CilOperation.LoadFieldAddress, OpCodes.Ldflda,
            EncodingOperand.Token);
        Add(result, CilOperation.StoreField, OpCodes.Stfld, EncodingOperand.Token);
        Add(result, CilOperation.LoadStaticField, OpCodes.Ldsfld, EncodingOperand.Token);
        Add(result, CilOperation.LoadStaticFieldAddress, OpCodes.Ldsflda,
            EncodingOperand.Token);
        Add(result, CilOperation.StoreStaticField, OpCodes.Stsfld, EncodingOperand.Token);
        Add(result, CilOperation.LoadTypeToken, OpCodes.Ldtoken, EncodingOperand.Token);
        Add(result, CilOperation.LoadFieldToken, OpCodes.Ldtoken, EncodingOperand.Token);
        Add(result, CilOperation.SizeOf, OpCodes.Sizeof, EncodingOperand.Token);
        Add(result, CilOperation.LoadObject, OpCodes.Ldobj, EncodingOperand.Token);
        Add(result, CilOperation.StoreObject, OpCodes.Stobj, EncodingOperand.Token);
        Add(result, CilOperation.CopyObject, OpCodes.Cpobj, EncodingOperand.Token);
        Add(result, CilOperation.InitializeObject, OpCodes.Initobj,
            EncodingOperand.Token);
        Add(result, CilOperation.LocalAllocate, OpCodes.Localloc);
        Add(result, CilOperation.CopyBlock, OpCodes.Cpblk);
        Add(result, CilOperation.InitializeBlock, OpCodes.Initblk);
        Add(result, CilOperation.Volatile, OpCodes.Volatile);
        Add(result, CilOperation.Readonly, OpCodes.Readonly);
        Add(result, CilOperation.Unaligned, OpCodes.Unaligned, EncodingOperand.ByteIndex);
        Add(result, CilOperation.Add, OpCodes.Add);
        Add(result, CilOperation.Subtract, OpCodes.Sub);
        Add(result, CilOperation.Multiply, OpCodes.Mul);
        Add(result, CilOperation.AddChecked, OpCodes.Add_Ovf);
        Add(result, CilOperation.AddCheckedUnsigned, OpCodes.Add_Ovf_Un);
        Add(result, CilOperation.SubtractChecked, OpCodes.Sub_Ovf);
        Add(result, CilOperation.SubtractCheckedUnsigned, OpCodes.Sub_Ovf_Un);
        Add(result, CilOperation.MultiplyChecked, OpCodes.Mul_Ovf);
        Add(result, CilOperation.MultiplyCheckedUnsigned, OpCodes.Mul_Ovf_Un);
        Add(result, CilOperation.BitwiseAnd, OpCodes.And);
        Add(result, CilOperation.BitwiseOr, OpCodes.Or);
        Add(result, CilOperation.BitwiseXor, OpCodes.Xor);
        Add(result, CilOperation.ShiftLeft, OpCodes.Shl);
        Add(result, CilOperation.ShiftRightSigned, OpCodes.Shr);
        Add(result, CilOperation.ShiftRightUnsigned, OpCodes.Shr_Un);
        Add(result, CilOperation.Divide, OpCodes.Div);
        Add(result, CilOperation.DivideUnsigned, OpCodes.Div_Un);
        Add(result, CilOperation.Remainder, OpCodes.Rem);
        Add(result, CilOperation.RemainderUnsigned, OpCodes.Rem_Un);
        Add(result, CilOperation.Negate, OpCodes.Neg);
        Add(result, CilOperation.OnesComplement, OpCodes.Not);
        Add(result, CilOperation.ConvertInt32, OpCodes.Conv_I4);
        Add(result, CilOperation.ConvertInt32Unsigned, OpCodes.Conv_U4);
        Add(result, CilOperation.ConvertInt64, OpCodes.Conv_I8);
        Add(result, CilOperation.ConvertInt64Unsigned, OpCodes.Conv_U8);
        Add(result, CilOperation.ConvertNativeInt, OpCodes.Conv_I);
        Add(result, CilOperation.ConvertNativeUInt, OpCodes.Conv_U);
        Add(result, CilOperation.ConvertFloat32, OpCodes.Conv_R4);
        Add(result, CilOperation.ConvertFloat64, OpCodes.Conv_R8);
        Add(result, CilOperation.ConvertFloatUnsigned, OpCodes.Conv_R_Un);
        Add(result, CilOperation.CheckFinite, OpCodes.Ckfinite);
        Add(result, CilOperation.CompareEqual, OpCodes.Ceq);
        Add(result, CilOperation.CompareGreaterThanSigned, OpCodes.Cgt);
        Add(result, CilOperation.CompareGreaterThanUnsigned, OpCodes.Cgt_Un);
        Add(result, CilOperation.CompareLessThanSigned, OpCodes.Clt);
        Add(result, CilOperation.CompareLessThanUnsigned, OpCodes.Clt_Un);
        Add(result, CilOperation.Branch, OpCodes.Br, EncodingOperand.Branch);
        Add(result, CilOperation.BranchIfTrue, OpCodes.Brtrue, EncodingOperand.Branch);
        Add(result, CilOperation.BranchIfFalse, OpCodes.Brfalse,
            EncodingOperand.Branch);
        Add(result, CilOperation.BranchIfEqual, OpCodes.Beq, EncodingOperand.Branch);
        Add(result, CilOperation.BranchIfNotEqual, OpCodes.Bne_Un,
            EncodingOperand.Branch);
        Add(result, CilOperation.BranchIfGreaterThanSigned, OpCodes.Bgt,
            EncodingOperand.Branch);
        Add(result, CilOperation.BranchIfGreaterThanUnsigned, OpCodes.Bgt_Un,
            EncodingOperand.Branch);
        Add(result, CilOperation.BranchIfGreaterThanOrEqualSigned, OpCodes.Bge,
            EncodingOperand.Branch);
        Add(result, CilOperation.BranchIfGreaterThanOrEqualUnsigned, OpCodes.Bge_Un,
            EncodingOperand.Branch);
        Add(result, CilOperation.BranchIfLessThanSigned, OpCodes.Blt,
            EncodingOperand.Branch);
        Add(result, CilOperation.BranchIfLessThanUnsigned, OpCodes.Blt_Un,
            EncodingOperand.Branch);
        Add(result, CilOperation.BranchIfLessThanOrEqualSigned, OpCodes.Ble,
            EncodingOperand.Branch);
        Add(result, CilOperation.BranchIfLessThanOrEqualUnsigned, OpCodes.Ble_Un,
            EncodingOperand.Branch);
        Add(result, CilOperation.Call, OpCodes.Call, EncodingOperand.Token);
        Add(result, CilOperation.CallVirtual, OpCodes.Callvirt, EncodingOperand.Token);
        Add(result, CilOperation.LoadFunction, OpCodes.Ldftn, EncodingOperand.Token);
        Add(result, CilOperation.LoadVirtualFunction, OpCodes.Ldvirtftn,
            EncodingOperand.Token);
        Add(result, CilOperation.CallIndirect, OpCodes.Calli, EncodingOperand.Token);
        Add(result, CilOperation.Constrained, OpCodes.Constrained,
            EncodingOperand.Token);
        Add(result, CilOperation.NewObject, OpCodes.Newobj, EncodingOperand.Token);
        Add(result, CilOperation.NewArray, OpCodes.Newarr, EncodingOperand.Token);
        Add(result, CilOperation.LoadArrayLength, OpCodes.Ldlen);
        Add(result, CilOperation.LoadArrayElementReference, OpCodes.Ldelem_Ref);
        Add(result, CilOperation.StoreArrayElementReference, OpCodes.Stelem_Ref);
        Add(result, CilOperation.LoadArrayElementAddress, OpCodes.Ldelema,
            EncodingOperand.Token);
        Add(result, CilOperation.Box, OpCodes.Box, EncodingOperand.Token);
        Add(result, CilOperation.Unbox, OpCodes.Unbox, EncodingOperand.Token);
        Add(result, CilOperation.UnboxAny, OpCodes.Unbox_Any, EncodingOperand.Token);
        Add(result, CilOperation.CastClass, OpCodes.Castclass, EncodingOperand.Token);
        Add(result, CilOperation.IsInstance, OpCodes.Isinst, EncodingOperand.Token);
        Add(result, CilOperation.Switch, OpCodes.Switch, EncodingOperand.Switch);
        Add(result, CilOperation.Throw, OpCodes.Throw);
        Add(result, CilOperation.Rethrow, OpCodes.Rethrow);
        Add(result, CilOperation.Leave, OpCodes.Leave, EncodingOperand.Branch);
        Add(result, CilOperation.EndFinally, OpCodes.Endfinally);
        Add(result, CilOperation.EndFilter, OpCodes.Endfilter);
        Add(result, CilOperation.Return, OpCodes.Ret);
        return result.ToImmutable();
    }

    private static void Add(
        IDictionary<CilOperation, Encoding> encodings,
        CilOperation operation,
        OpCode opCode,
        EncodingOperand operand = EncodingOperand.None)
    {
        if (!encodings.TryAdd(operation, new(opCode, operand)))
        {
            throw new InvalidOperationException(
                $"random CIL encoding is duplicated for {operation}");
        }
    }

    private enum EncodingOperand
    {
        None,
        ByteIndex,
        Int32,
        Int64,
        Float32,
        Float64,
        Token,
        Branch,
        Switch,
    }

    private readonly record struct Encoding(
        OpCode OpCode,
        EncodingOperand Operand);
}
