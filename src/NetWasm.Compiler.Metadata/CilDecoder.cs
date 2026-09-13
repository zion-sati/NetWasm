using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal interface ICilDecoder
{
    CilMethodBody Decode(ManagedAssembly source, MethodDefinitionModel method);
    CilMethodBody Decode(ManagedAssembly source, MethodInstanceModel method);
}

internal sealed class CilDecoder(
    IMetadataMethodBodyBlockReader methodBodies,
    ISymbolFormatter symbols,
    IMetadataMethodReferenceResolver methodReferences,
    IMetadataTypeEntityResolver typeEntities,
    IMetadataFieldReferenceResolver fieldReferences,
    IMetadataTypeSignatureResolver typeSignatures,
    IMetadataCallSiteSignatureResolver callSiteSignatures,
    IMetadataStackTypeResolver stackTypes,
    ICilSwitchLowerer switches) : ICilDecoder
{
    private static readonly Dictionary<byte, OpCode> OneByteOpCodes;
    private static readonly Dictionary<byte, OpCode> TwoByteOpCodes;
    private static readonly Dictionary<ExceptionRegionKind, CilExceptionRegionKind>
        ExceptionRegionKinds = new Dictionary<ExceptionRegionKind, CilExceptionRegionKind>
        {
            [ExceptionRegionKind.Catch] = CilExceptionRegionKind.Catch,
            [ExceptionRegionKind.Filter] = CilExceptionRegionKind.Filter,
            [ExceptionRegionKind.Finally] = CilExceptionRegionKind.Finally,
            [ExceptionRegionKind.Fault] = CilExceptionRegionKind.Fault,
        };
    private readonly IMetadataMethodBodyBlockReader _methodBodies = methodBodies;
    private readonly ISymbolFormatter _symbols = symbols;
    private readonly IMetadataMethodReferenceResolver _methodReferences = methodReferences;
    private readonly IMetadataTypeEntityResolver _typeEntities = typeEntities;
    private readonly IMetadataFieldReferenceResolver _fieldReferences = fieldReferences;
    private readonly IMetadataTypeSignatureResolver _typeSignatures = typeSignatures;
    private readonly IMetadataCallSiteSignatureResolver _callSiteSignatures = callSiteSignatures;
    private readonly IMetadataStackTypeResolver _stackTypes = stackTypes;

    static CilDecoder()
    {
        var oneByte = new Dictionary<byte, OpCode>();
        var twoByte = new Dictionary<byte, OpCode>();
        foreach (var field in typeof(OpCodes).GetFields(
                     BindingFlags.Public | BindingFlags.Static))
        {
            var opCode = (OpCode)field.GetValue(null)!;
            var value = unchecked((ushort)opCode.Value);
            if (value < 0x100)
            {
                oneByte.Add((byte)value, opCode);
            }
            else if ((value & 0xff00) == 0xfe00)
            {
                twoByte.Add((byte)value, opCode);
            }
        }
        OneByteOpCodes = oneByte;
        TwoByteOpCodes = twoByte;
    }

    public CilMethodBody Decode(ManagedAssembly assembly, MethodDefinitionModel method)
    {
        var instance = _methodReferences.Resolve(assembly.Metadata,
            method.Key.MetadataToken,
            _symbols.Format(method),
            0);
        return Decode(assembly, instance);
    }

    public CilMethodBody Decode(ManagedAssembly assembly, MethodInstanceModel methodInstance)
    {
        var method = methodInstance.Definition;
        var body = _methodBodies.Read(assembly.PortableExecutableReader, method);
        var displayName = _symbols.Format(method);
        var genericContext = new CliGenericContext(
            methodInstance.DeclaringType.Shape == CliTypeShape.GenericInstantiation
                ? methodInstance.DeclaringType.TypeArguments
                : [],
            methodInstance.MethodArguments);
        var bytes = body.GetILBytes()!;
        var instructions = ImmutableArray.CreateBuilder<CilInstruction>();
        var position = 0;
        while (position < bytes.Length)
        {
            var offset = position;
            var opCode = ReadOpCode(bytes, ref position, displayName, offset);
            instructions.Add(ReadInstruction(
                assembly,
                genericContext,
                displayName,
                bytes,
                ref position,
                offset,
                opCode));
        }

        var decodedLocalSignatureTypes = body.LocalSignature.IsNil
            ? []
            : assembly.Reader.GetStandaloneSignature(body.LocalSignature)
                .DecodeLocalSignature(
                    new SignatureTypeProvider(
                        assembly.Identity,
                        assembly.Reader,
                        assembly.Metadata.AssemblyIdentityAliases),
                    genericContext);
        var localSignatureTypes = decodedLocalSignatureTypes
            .Select(type => _stackTypes.Resolve(type, genericContext))
            .ToImmutableArray();
        var locals = localSignatureTypes
            .Select(type => type.StackKind)
            .ToImmutableArray();
        var exceptionRegions = body.ExceptionRegions.Select(region =>
            DecodeExceptionRegion(assembly, region)).ToImmutableArray();
        var lowered = switches.Lower(
            instructions.ToImmutable(),
            exceptionRegions);
        return new CilMethodBody(
            method,
            checked(body.MaxStack + lowered.AdditionalMaxStack),
            locals,
            lowered.Instructions)
        {
            MethodInstance = methodInstance,
            LocalSignatureTypes = localSignatureTypes,
            ExceptionRegions = lowered.ExceptionRegions,
        };
    }

    private CilInstruction ReadInstruction(
        ManagedAssembly assembly,
        CliGenericContext genericContext,
        string displayName,
        byte[] bytes,
        ref int position,
        int offset,
        OpCode opCode)
    {
        var name = opCode.Name!;
        CilOperation operation;
        CilOperand operand;
        switch (name)
        {
            case "nop": operation = CilOperation.Nop; operand = new CilOperand.None(); break;
            case "ldarg.0": operation = CilOperation.LoadArgument; operand = new CilOperand.Index(0); break;
            case "ldarg.1": operation = CilOperation.LoadArgument; operand = new CilOperand.Index(1); break;
            case "ldarg.2": operation = CilOperation.LoadArgument; operand = new CilOperand.Index(2); break;
            case "ldarg.3": operation = CilOperation.LoadArgument; operand = new CilOperand.Index(3); break;
            case "ldarg.s": operation = CilOperation.LoadArgument; operand = new CilOperand.Index(ReadByte(bytes, ref position, displayName, offset)); break;
            case "ldarg": operation = CilOperation.LoadArgument; operand = new CilOperand.Index(ReadUInt16(bytes, ref position, displayName, offset)); break;
            case "ldarga.s": operation = CilOperation.LoadArgumentAddress; operand = new CilOperand.Index(ReadByte(bytes, ref position, displayName, offset)); break;
            case "ldarga": operation = CilOperation.LoadArgumentAddress; operand = new CilOperand.Index(ReadUInt16(bytes, ref position, displayName, offset)); break;
            case "starg.s": operation = CilOperation.StoreArgument; operand = new CilOperand.Index(ReadByte(bytes, ref position, displayName, offset)); break;
            case "starg": operation = CilOperation.StoreArgument; operand = new CilOperand.Index(ReadUInt16(bytes, ref position, displayName, offset)); break;
            case "ldloc.0": operation = CilOperation.LoadLocal; operand = new CilOperand.Index(0); break;
            case "ldloc.1": operation = CilOperation.LoadLocal; operand = new CilOperand.Index(1); break;
            case "ldloc.2": operation = CilOperation.LoadLocal; operand = new CilOperand.Index(2); break;
            case "ldloc.3": operation = CilOperation.LoadLocal; operand = new CilOperand.Index(3); break;
            case "ldloc.s": operation = CilOperation.LoadLocal; operand = new CilOperand.Index(ReadByte(bytes, ref position, displayName, offset)); break;
            case "ldloc": operation = CilOperation.LoadLocal; operand = new CilOperand.Index(ReadUInt16(bytes, ref position, displayName, offset)); break;
            case "ldloca.s": operation = CilOperation.LoadLocalAddress; operand = new CilOperand.Index(ReadByte(bytes, ref position, displayName, offset)); break;
            case "ldloca": operation = CilOperation.LoadLocalAddress; operand = new CilOperand.Index(ReadUInt16(bytes, ref position, displayName, offset)); break;
            case "stloc.0": operation = CilOperation.StoreLocal; operand = new CilOperand.Index(0); break;
            case "stloc.1": operation = CilOperation.StoreLocal; operand = new CilOperand.Index(1); break;
            case "stloc.2": operation = CilOperation.StoreLocal; operand = new CilOperand.Index(2); break;
            case "stloc.3": operation = CilOperation.StoreLocal; operand = new CilOperand.Index(3); break;
            case "stloc.s": operation = CilOperation.StoreLocal; operand = new CilOperand.Index(ReadByte(bytes, ref position, displayName, offset)); break;
            case "stloc": operation = CilOperation.StoreLocal; operand = new CilOperand.Index(ReadUInt16(bytes, ref position, displayName, offset)); break;
            case "ldc.i4.m1": operation = CilOperation.LoadInt32; operand = new CilOperand.ConstantI4(-1); break;
            case "ldc.i4.0": operation = CilOperation.LoadInt32; operand = new CilOperand.ConstantI4(0); break;
            case "ldc.i4.1": operation = CilOperation.LoadInt32; operand = new CilOperand.ConstantI4(1); break;
            case "ldc.i4.2": operation = CilOperation.LoadInt32; operand = new CilOperand.ConstantI4(2); break;
            case "ldc.i4.3": operation = CilOperation.LoadInt32; operand = new CilOperand.ConstantI4(3); break;
            case "ldc.i4.4": operation = CilOperation.LoadInt32; operand = new CilOperand.ConstantI4(4); break;
            case "ldc.i4.5": operation = CilOperation.LoadInt32; operand = new CilOperand.ConstantI4(5); break;
            case "ldc.i4.6": operation = CilOperation.LoadInt32; operand = new CilOperand.ConstantI4(6); break;
            case "ldc.i4.7": operation = CilOperation.LoadInt32; operand = new CilOperand.ConstantI4(7); break;
            case "ldc.i4.8": operation = CilOperation.LoadInt32; operand = new CilOperand.ConstantI4(8); break;
            case "ldc.i4.s": operation = CilOperation.LoadInt32; operand = new CilOperand.ConstantI4(ReadSByte(bytes, ref position, displayName, offset)); break;
            case "ldc.i4": operation = CilOperation.LoadInt32; operand = new CilOperand.ConstantI4(ReadInt32(bytes, ref position, displayName, offset)); break;
            case "ldc.i8": operation = CilOperation.LoadInt64; operand = new CilOperand.ConstantI8(ReadInt64(bytes, ref position, displayName, offset)); break;
            case "ldc.r4": operation = CilOperation.LoadFloat32; operand = new CilOperand.ConstantF4(BitConverter.Int32BitsToSingle(ReadInt32(bytes, ref position, displayName, offset))); break;
            case "ldc.r8": operation = CilOperation.LoadFloat64; operand = new CilOperand.ConstantF8(BitConverter.Int64BitsToDouble(ReadInt64(bytes, ref position, displayName, offset))); break;
            case "ldnull": operation = CilOperation.LoadNull; operand = new CilOperand.None(); break;
            case "dup": operation = CilOperation.Duplicate; operand = new CilOperand.None(); break;
            case "pop": operation = CilOperation.Pop; operand = new CilOperand.None(); break;
            case "ldstr":
                {
                    operation = CilOperation.LoadString;
                    var token = ReadInt32(bytes, ref position, displayName, offset);
                    operand = new CilOperand.UserString(assembly.Reader.GetUserString(
                        MetadataTokens.UserStringHandle(token & 0x00ffffff)));
                    break;
                }
            case "ldtoken":
                (operation, operand) = ReadRuntimeHandle(
                    assembly,
                    genericContext,
                    displayName,
                    bytes,
                    ref position,
                    offset);
                break;
            case "ldfld": operation = CilOperation.LoadField; operand = ReadField(assembly, genericContext, displayName, bytes, ref position, offset); break;
            case "ldflda": operation = CilOperation.LoadFieldAddress; operand = ReadField(assembly, genericContext, displayName, bytes, ref position, offset); break;
            case "stfld": operation = CilOperation.StoreField; operand = ReadField(assembly, genericContext, displayName, bytes, ref position, offset); break;
            case "ldsfld": operation = CilOperation.LoadStaticField; operand = ReadField(assembly, genericContext, displayName, bytes, ref position, offset); break;
            case "ldsflda": operation = CilOperation.LoadStaticFieldAddress; operand = ReadField(assembly, genericContext, displayName, bytes, ref position, offset); break;
            case "stsfld": operation = CilOperation.StoreStaticField; operand = ReadField(assembly, genericContext, displayName, bytes, ref position, offset); break;
            case "ldobj": operation = CilOperation.LoadObject; operand = ReadSignatureType(assembly, genericContext, displayName, bytes, ref position, offset); break;
            case "stobj": operation = CilOperation.StoreObject; operand = ReadSignatureType(assembly, genericContext, displayName, bytes, ref position, offset); break;
            case "ldind.i1": operation = CilOperation.LoadObject; operand = PrimitiveArrayElement("i1", CliValueKind.I4); break;
            case "ldind.u1": operation = CilOperation.LoadObject; operand = PrimitiveArrayElement("u1", CliValueKind.I4); break;
            case "ldind.i2": operation = CilOperation.LoadObject; operand = PrimitiveArrayElement("i2", CliValueKind.I4); break;
            case "ldind.u2": operation = CilOperation.LoadObject; operand = PrimitiveArrayElement("u2", CliValueKind.I4); break;
            case "ldind.i4": operation = CilOperation.LoadObject; operand = PrimitiveArrayElement("i4", CliValueKind.I4); break;
            case "ldind.u4": operation = CilOperation.LoadObject; operand = PrimitiveArrayElement("u4", CliValueKind.I4); break;
            case "ldind.i8": operation = CilOperation.LoadObject; operand = PrimitiveArrayElement("i8", CliValueKind.I8); break;
            case "ldind.i": operation = CilOperation.LoadObject; operand = PrimitiveArrayElement("nativeint", CliValueKind.NativeInt); break;
            case "ldind.r4": operation = CilOperation.LoadObject; operand = PrimitiveArrayElement("f4", CliValueKind.F4); break;
            case "ldind.r8": operation = CilOperation.LoadObject; operand = PrimitiveArrayElement("f8", CliValueKind.F8); break;
            case "ldind.ref": operation = CilOperation.LoadObject; operand = new CilOperand.TypeIdentity(CliTypeIdentity.FromStackKind(CliValueKind.ManagedReference)); break;
            case "stind.i1": operation = CilOperation.StoreObject; operand = PrimitiveArrayElement("i1", CliValueKind.I4); break;
            case "stind.i2": operation = CilOperation.StoreObject; operand = PrimitiveArrayElement("i2", CliValueKind.I4); break;
            case "stind.i4": operation = CilOperation.StoreObject; operand = PrimitiveArrayElement("i4", CliValueKind.I4); break;
            case "stind.i8": operation = CilOperation.StoreObject; operand = PrimitiveArrayElement("i8", CliValueKind.I8); break;
            case "stind.i": operation = CilOperation.StoreObject; operand = PrimitiveArrayElement("nativeint", CliValueKind.NativeInt); break;
            case "stind.r4": operation = CilOperation.StoreObject; operand = PrimitiveArrayElement("f4", CliValueKind.F4); break;
            case "stind.r8": operation = CilOperation.StoreObject; operand = PrimitiveArrayElement("f8", CliValueKind.F8); break;
            case "stind.ref": operation = CilOperation.StoreObject; operand = new CilOperand.TypeIdentity(CliTypeIdentity.FromStackKind(CliValueKind.ManagedReference)); break;
            case "cpobj": operation = CilOperation.CopyObject; operand = ReadSignatureType(assembly, genericContext, displayName, bytes, ref position, offset); break;
            case "initobj": operation = CilOperation.InitializeObject; operand = ReadSignatureType(assembly, genericContext, displayName, bytes, ref position, offset); break;
            case "sizeof": operation = CilOperation.SizeOf; operand = ReadSignatureType(assembly, genericContext, displayName, bytes, ref position, offset); break;
            case "localloc": operation = CilOperation.LocalAllocate; operand = new CilOperand.None(); break;
            case "cpblk": operation = CilOperation.CopyBlock; operand = new CilOperand.None(); break;
            case "initblk": operation = CilOperation.InitializeBlock; operand = new CilOperand.None(); break;
            case "add": operation = CilOperation.Add; operand = new CilOperand.None(); break;
            case "sub": operation = CilOperation.Subtract; operand = new CilOperand.None(); break;
            case "mul": operation = CilOperation.Multiply; operand = new CilOperand.None(); break;
            case "and": operation = CilOperation.BitwiseAnd; operand = new CilOperand.None(); break;
            case "or": operation = CilOperation.BitwiseOr; operand = new CilOperand.None(); break;
            case "xor": operation = CilOperation.BitwiseXor; operand = new CilOperand.None(); break;
            case "shl": operation = CilOperation.ShiftLeft; operand = new CilOperand.None(); break;
            case "shr": operation = CilOperation.ShiftRightSigned; operand = new CilOperand.None(); break;
            case "shr.un": operation = CilOperation.ShiftRightUnsigned; operand = new CilOperand.None(); break;
            case "neg": operation = CilOperation.Negate; operand = new CilOperand.None(); break;
            case "not": operation = CilOperation.OnesComplement; operand = new CilOperand.None(); break;
            case "add.ovf": operation = CilOperation.AddChecked; operand = new CilOperand.None(); break;
            case "add.ovf.un": operation = CilOperation.AddCheckedUnsigned; operand = new CilOperand.None(); break;
            case "sub.ovf": operation = CilOperation.SubtractChecked; operand = new CilOperand.None(); break;
            case "sub.ovf.un": operation = CilOperation.SubtractCheckedUnsigned; operand = new CilOperand.None(); break;
            case "mul.ovf": operation = CilOperation.MultiplyChecked; operand = new CilOperand.None(); break;
            case "mul.ovf.un": operation = CilOperation.MultiplyCheckedUnsigned; operand = new CilOperand.None(); break;
            case "div": operation = CilOperation.Divide; operand = new CilOperand.None(); break;
            case "div.un": operation = CilOperation.DivideUnsigned; operand = new CilOperand.None(); break;
            case "rem": operation = CilOperation.Remainder; operand = new CilOperand.None(); break;
            case "rem.un": operation = CilOperation.RemainderUnsigned; operand = new CilOperand.None(); break;
            case "conv.i4": operation = CilOperation.ConvertInt32; operand = new CilOperand.None(); break;
            case "conv.u4": operation = CilOperation.ConvertInt32Unsigned; operand = new CilOperand.None(); break;
            case "conv.i8": operation = CilOperation.ConvertInt64; operand = new CilOperand.None(); break;
            case "conv.u8": operation = CilOperation.ConvertInt64Unsigned; operand = new CilOperand.None(); break;
            case "conv.i": operation = CilOperation.ConvertNativeInt; operand = new CilOperand.None(); break;
            case "conv.u": operation = CilOperation.ConvertNativeUInt; operand = new CilOperand.None(); break;
            case "conv.r4": operation = CilOperation.ConvertFloat32; operand = new CilOperand.None(); break;
            case "conv.r8": operation = CilOperation.ConvertFloat64; operand = new CilOperand.None(); break;
            case "conv.r.un": operation = CilOperation.ConvertFloatUnsigned; operand = new CilOperand.None(); break;
            case "ckfinite": operation = CilOperation.CheckFinite; operand = new CilOperand.None(); break;
            case "conv.i1": operation = CilOperation.ConvertNumeric; operand = Conversion(8, false); break;
            case "conv.u1": operation = CilOperation.ConvertNumeric; operand = Conversion(8, true); break;
            case "conv.i2": operation = CilOperation.ConvertNumeric; operand = Conversion(16, false); break;
            case "conv.u2": operation = CilOperation.ConvertNumeric; operand = Conversion(16, true); break;
            case "conv.ovf.i1": operation = CilOperation.ConvertNumeric; operand = Conversion(8, false, true); break;
            case "conv.ovf.u1": operation = CilOperation.ConvertNumeric; operand = Conversion(8, true, true); break;
            case "conv.ovf.i2": operation = CilOperation.ConvertNumeric; operand = Conversion(16, false, true); break;
            case "conv.ovf.u2": operation = CilOperation.ConvertNumeric; operand = Conversion(16, true, true); break;
            case "conv.ovf.i4": operation = CilOperation.ConvertNumeric; operand = Conversion(32, false, true); break;
            case "conv.ovf.u4": operation = CilOperation.ConvertNumeric; operand = Conversion(32, true, true); break;
            case "conv.ovf.i8": operation = CilOperation.ConvertNumeric; operand = Conversion(64, false, true); break;
            case "conv.ovf.u8": operation = CilOperation.ConvertNumeric; operand = Conversion(64, true, true); break;
            case "conv.ovf.i": operation = CilOperation.ConvertNumeric; operand = Conversion(0, false, true, native: true); break;
            case "conv.ovf.u": operation = CilOperation.ConvertNumeric; operand = Conversion(0, true, true, native: true); break;
            case "conv.ovf.i1.un": operation = CilOperation.ConvertNumeric; operand = Conversion(8, false, true, true); break;
            case "conv.ovf.u1.un": operation = CilOperation.ConvertNumeric; operand = Conversion(8, true, true, true); break;
            case "conv.ovf.i2.un": operation = CilOperation.ConvertNumeric; operand = Conversion(16, false, true, true); break;
            case "conv.ovf.u2.un": operation = CilOperation.ConvertNumeric; operand = Conversion(16, true, true, true); break;
            case "conv.ovf.i4.un": operation = CilOperation.ConvertNumeric; operand = Conversion(32, false, true, true); break;
            case "conv.ovf.u4.un": operation = CilOperation.ConvertNumeric; operand = Conversion(32, true, true, true); break;
            case "conv.ovf.i8.un": operation = CilOperation.ConvertNumeric; operand = Conversion(64, false, true, true); break;
            case "conv.ovf.u8.un": operation = CilOperation.ConvertNumeric; operand = Conversion(64, true, true, true); break;
            case "conv.ovf.i.un": operation = CilOperation.ConvertNumeric; operand = Conversion(0, false, true, true, true); break;
            case "conv.ovf.u.un": operation = CilOperation.ConvertNumeric; operand = Conversion(0, true, true, true, true); break;
            case "ceq": operation = CilOperation.CompareEqual; operand = new CilOperand.None(); break;
            case "cgt": operation = CilOperation.CompareGreaterThanSigned; operand = new CilOperand.None(); break;
            case "cgt.un": operation = CilOperation.CompareGreaterThanUnsigned; operand = new CilOperand.None(); break;
            case "clt": operation = CilOperation.CompareLessThanSigned; operand = new CilOperand.None(); break;
            case "clt.un": operation = CilOperation.CompareLessThanUnsigned; operand = new CilOperand.None(); break;
            case "br": operation = CilOperation.Branch; operand = ReadBranch(bytes, ref position, displayName, offset, shortForm: false); break;
            case "br.s": operation = CilOperation.Branch; operand = ReadBranch(bytes, ref position, displayName, offset, shortForm: true); break;
            case "brtrue": operation = CilOperation.BranchIfTrue; operand = ReadBranch(bytes, ref position, displayName, offset, shortForm: false); break;
            case "brtrue.s": operation = CilOperation.BranchIfTrue; operand = ReadBranch(bytes, ref position, displayName, offset, shortForm: true); break;
            case "brfalse": operation = CilOperation.BranchIfFalse; operand = ReadBranch(bytes, ref position, displayName, offset, shortForm: false); break;
            case "brfalse.s": operation = CilOperation.BranchIfFalse; operand = ReadBranch(bytes, ref position, displayName, offset, shortForm: true); break;
            case "bne.un": operation = CilOperation.BranchIfNotEqual; operand = ReadBranch(bytes, ref position, displayName, offset, shortForm: false); break;
            case "bne.un.s": operation = CilOperation.BranchIfNotEqual; operand = ReadBranch(bytes, ref position, displayName, offset, shortForm: true); break;
            case "beq": operation = CilOperation.BranchIfEqual; operand = ReadBranch(bytes, ref position, displayName, offset, shortForm: false); break;
            case "beq.s": operation = CilOperation.BranchIfEqual; operand = ReadBranch(bytes, ref position, displayName, offset, shortForm: true); break;
            case "bgt": operation = CilOperation.BranchIfGreaterThanSigned; operand = ReadBranch(bytes, ref position, displayName, offset, shortForm: false); break;
            case "bgt.s": operation = CilOperation.BranchIfGreaterThanSigned; operand = ReadBranch(bytes, ref position, displayName, offset, shortForm: true); break;
            case "bgt.un": operation = CilOperation.BranchIfGreaterThanUnsigned; operand = ReadBranch(bytes, ref position, displayName, offset, shortForm: false); break;
            case "bgt.un.s": operation = CilOperation.BranchIfGreaterThanUnsigned; operand = ReadBranch(bytes, ref position, displayName, offset, shortForm: true); break;
            case "bge": operation = CilOperation.BranchIfGreaterThanOrEqualSigned; operand = ReadBranch(bytes, ref position, displayName, offset, shortForm: false); break;
            case "bge.s": operation = CilOperation.BranchIfGreaterThanOrEqualSigned; operand = ReadBranch(bytes, ref position, displayName, offset, shortForm: true); break;
            case "bge.un": operation = CilOperation.BranchIfGreaterThanOrEqualUnsigned; operand = ReadBranch(bytes, ref position, displayName, offset, shortForm: false); break;
            case "bge.un.s": operation = CilOperation.BranchIfGreaterThanOrEqualUnsigned; operand = ReadBranch(bytes, ref position, displayName, offset, shortForm: true); break;
            case "blt": operation = CilOperation.BranchIfLessThanSigned; operand = ReadBranch(bytes, ref position, displayName, offset, shortForm: false); break;
            case "blt.s": operation = CilOperation.BranchIfLessThanSigned; operand = ReadBranch(bytes, ref position, displayName, offset, shortForm: true); break;
            case "blt.un": operation = CilOperation.BranchIfLessThanUnsigned; operand = ReadBranch(bytes, ref position, displayName, offset, shortForm: false); break;
            case "blt.un.s": operation = CilOperation.BranchIfLessThanUnsigned; operand = ReadBranch(bytes, ref position, displayName, offset, shortForm: true); break;
            case "ble": operation = CilOperation.BranchIfLessThanOrEqualSigned; operand = ReadBranch(bytes, ref position, displayName, offset, shortForm: false); break;
            case "ble.s": operation = CilOperation.BranchIfLessThanOrEqualSigned; operand = ReadBranch(bytes, ref position, displayName, offset, shortForm: true); break;
            case "ble.un": operation = CilOperation.BranchIfLessThanOrEqualUnsigned; operand = ReadBranch(bytes, ref position, displayName, offset, shortForm: false); break;
            case "ble.un.s": operation = CilOperation.BranchIfLessThanOrEqualUnsigned; operand = ReadBranch(bytes, ref position, displayName, offset, shortForm: true); break;
            case "switch":
                operation = CilOperation.Switch;
                operand = ReadSwitch(bytes, ref position, displayName, offset);
                break;
            case "ldftn": operation = CilOperation.LoadFunction; operand = ReadMethod(assembly, genericContext, displayName, bytes, ref position, offset); break;
            case "ldvirtftn": operation = CilOperation.LoadVirtualFunction; operand = ReadMethod(assembly, genericContext, displayName, bytes, ref position, offset); break;
            case "call": operation = CilOperation.Call; operand = ReadMethod(assembly, genericContext, displayName, bytes, ref position, offset); break;
            case "callvirt": operation = CilOperation.CallVirtual; operand = ReadMethod(assembly, genericContext, displayName, bytes, ref position, offset); break;
            case "calli":
                operation = CilOperation.CallIndirect;
                operand = new CilOperand.CallSite(
                    _callSiteSignatures.Resolve(
                        assembly.Metadata,
                        ReadInt32(bytes, ref position, displayName, offset),
                        genericContext));
                break;
            case "newobj": operation = CilOperation.NewObject; operand = ReadMethod(assembly, genericContext, displayName, bytes, ref position, offset); break;
            case "box": operation = CilOperation.Box; operand = ReadSignatureType(assembly, genericContext, displayName, bytes, ref position, offset); break;
            case "unbox": operation = CilOperation.Unbox; operand = ReadSignatureType(assembly, genericContext, displayName, bytes, ref position, offset); break;
            case "unbox.any": operation = CilOperation.UnboxAny; operand = ReadSignatureType(assembly, genericContext, displayName, bytes, ref position, offset); break;
            case "constrained.": operation = CilOperation.Constrained; operand = ReadSignatureType(assembly, genericContext, displayName, bytes, ref position, offset); break;
            case "volatile.": operation = CilOperation.Volatile; operand = new CilOperand.None(); break;
            case "readonly.": operation = CilOperation.Readonly; operand = new CilOperand.None(); break;
            case "unaligned.": operation = CilOperation.Unaligned; operand = new CilOperand.Index(ReadByte(bytes, ref position, displayName, offset)); break;
            case "break": operation = CilOperation.Break; operand = new CilOperand.None(); break;
            case "newarr": operation = CilOperation.NewArray; operand = ReadSignatureType(assembly, genericContext, displayName, bytes, ref position, offset); break;
            case "ldlen": operation = CilOperation.LoadArrayLength; operand = new CilOperand.None(); break;
            case "ldelem.ref": operation = CilOperation.LoadArrayElementReference; operand = new CilOperand.None(); break;
            case "stelem.ref": operation = CilOperation.StoreArrayElementReference; operand = new CilOperand.None(); break;
            case "ldelema": operation = CilOperation.LoadArrayElementAddress; operand = ReadSignatureType(assembly, genericContext, displayName, bytes, ref position, offset); break;
            case "ldelem": operation = CilOperation.LoadArrayElement; operand = ReadSignatureType(assembly, genericContext, displayName, bytes, ref position, offset); break;
            case "stelem": operation = CilOperation.StoreArrayElement; operand = ReadSignatureType(assembly, genericContext, displayName, bytes, ref position, offset); break;
            case "ldelem.i1": operation = CilOperation.LoadArrayElement; operand = PrimitiveArrayElement("i1", CliValueKind.I4); break;
            case "ldelem.u1": operation = CilOperation.LoadArrayElement; operand = PrimitiveArrayElement("u1", CliValueKind.I4); break;
            case "ldelem.i2": operation = CilOperation.LoadArrayElement; operand = PrimitiveArrayElement("i2", CliValueKind.I4); break;
            case "ldelem.u2": operation = CilOperation.LoadArrayElement; operand = PrimitiveArrayElement("u2", CliValueKind.I4); break;
            case "ldelem.i4": operation = CilOperation.LoadArrayElement; operand = PrimitiveArrayElement("i4", CliValueKind.I4); break;
            case "ldelem.u4": operation = CilOperation.LoadArrayElement; operand = PrimitiveArrayElement("u4", CliValueKind.I4); break;
            case "ldelem.i8": operation = CilOperation.LoadArrayElement; operand = PrimitiveArrayElement("i8", CliValueKind.I8); break;
            case "ldelem.i": operation = CilOperation.LoadArrayElement; operand = PrimitiveArrayElement("nativeint", CliValueKind.NativeInt); break;
            case "ldelem.r4": operation = CilOperation.LoadArrayElement; operand = PrimitiveArrayElement("f4", CliValueKind.F4); break;
            case "ldelem.r8": operation = CilOperation.LoadArrayElement; operand = PrimitiveArrayElement("f8", CliValueKind.F8); break;
            case "stelem.i1": operation = CilOperation.StoreArrayElement; operand = PrimitiveArrayElement("i1", CliValueKind.I4); break;
            case "stelem.i2": operation = CilOperation.StoreArrayElement; operand = PrimitiveArrayElement("i2", CliValueKind.I4); break;
            case "stelem.i4": operation = CilOperation.StoreArrayElement; operand = PrimitiveArrayElement("i4", CliValueKind.I4); break;
            case "stelem.i8": operation = CilOperation.StoreArrayElement; operand = PrimitiveArrayElement("i8", CliValueKind.I8); break;
            case "stelem.i": operation = CilOperation.StoreArrayElement; operand = PrimitiveArrayElement("nativeint", CliValueKind.NativeInt); break;
            case "stelem.r4": operation = CilOperation.StoreArrayElement; operand = PrimitiveArrayElement("f4", CliValueKind.F4); break;
            case "stelem.r8": operation = CilOperation.StoreArrayElement; operand = PrimitiveArrayElement("f8", CliValueKind.F8); break;
            case "castclass": operation = CilOperation.CastClass; operand = ReadType(assembly, genericContext, displayName, bytes, ref position, offset); break;
            case "isinst": operation = CilOperation.IsInstance; operand = ReadType(assembly, genericContext, displayName, bytes, ref position, offset); break;
            case "throw": operation = CilOperation.Throw; operand = new CilOperand.None(); break;
            case "rethrow": operation = CilOperation.Rethrow; operand = new CilOperand.None(); break;
            case "leave": operation = CilOperation.Leave; operand = ReadBranch(bytes, ref position, displayName, offset, shortForm: false); break;
            case "leave.s": operation = CilOperation.Leave; operand = ReadBranch(bytes, ref position, displayName, offset, shortForm: true); break;
            case "endfinally": operation = CilOperation.EndFinally; operand = new CilOperand.None(); break;
            case "endfilter": operation = CilOperation.EndFilter; operand = new CilOperand.None(); break;
            case "ret": operation = CilOperation.Return; operand = new CilOperand.None(); break;
            default:
                throw new CompilerException(
                    new CompilerDiagnostic(
                        DiagnosticCode.UnsupportedCil,
                        $"unsupported CIL opcode '{name}'",
                        displayName,
                        offset));
        }
        return new CilInstruction(offset, position, operation, operand);

        static CilOperand.NumericConversion Conversion(
            int bits,
            bool destinationUnsigned,
            bool @checked = false,
            bool sourceUnsigned = false,
            bool native = false) => new(
                bits, destinationUnsigned, @checked, sourceUnsigned, native);
    }

    private CilExceptionRegion DecodeExceptionRegion(
        ManagedAssembly assembly,
        ExceptionRegion region)
    {
        var kind = ExceptionRegionKinds[region.Kind];
        EntityKey? catchType = region.CatchType.IsNil
            ? null
            : _typeEntities.Resolve(assembly.Metadata, region.CatchType);
        return new CilExceptionRegion(
            kind,
            region.TryOffset,
            region.TryLength,
            region.HandlerOffset,
            region.HandlerLength,
            catchType,
            region.FilterOffset < 0 ? null : region.FilterOffset);
    }

    private CilOperand.MethodInstance ReadMethod(
        ManagedAssembly assembly,
        CliGenericContext genericContext,
        string method,
        byte[] bytes,
        ref int position,
        int offset)
    {
        var token = ReadInt32(bytes, ref position, method, offset);
        var instance = _methodReferences.Resolve(assembly.Metadata, token, method, offset, genericContext);
        return new CilOperand.MethodInstance(instance);
    }

    private CilOperand ReadField(
        ManagedAssembly assembly,
        CliGenericContext genericContext,
        string method,
        byte[] bytes,
        ref int position,
        int offset)
    {
        var token = ReadInt32(bytes, ref position, method, offset);
        var instance = _fieldReferences.Resolve(assembly.Metadata, token, method, offset, genericContext);
        return instance.IsConstructed ||
               instance.FieldType.StackKind != instance.Definition.FieldType
            ? new CilOperand.FieldInstance(instance)
            : new CilOperand.Entity(instance.Definition.Key);
    }

    private CilOperand.TypeIdentity ReadType(
        ManagedAssembly assembly,
        CliGenericContext genericContext,
        string method,
        byte[] bytes,
        ref int position,
        int offset)
    {
        var token = ReadInt32(bytes, ref position, method, offset);
        var handle = MetadataTokens.EntityHandle(token);
        if (handle.Kind is not (
            HandleKind.TypeDefinition or
            HandleKind.TypeReference or
            HandleKind.TypeSpecification))
        {
            throw new CompilerException(new CompilerDiagnostic(
                DiagnosticCode.UnsupportedMetadata,
                $"type token 0x{token:x8} is not a type definition, reference, or specification",
                method,
                offset));
        }
        return new CilOperand.TypeIdentity(
            _typeSignatures.Resolve(assembly.Metadata, token, genericContext));
    }

    private CilOperand.TypeIdentity ReadSignatureType(
        ManagedAssembly assembly,
        CliGenericContext genericContext,
        string method,
        byte[] bytes,
        ref int position,
        int offset)
    {
        var token = ReadInt32(bytes, ref position, method, offset);
        return new CilOperand.TypeIdentity(
            _typeSignatures.Resolve(assembly.Metadata, token, genericContext));
    }

    private static CilOperand.BranchTarget ReadBranch(
        byte[] bytes,
        ref int position,
        string method,
        int offset,
        bool shortForm)
    {
        var delta = shortForm
            ? ReadSByte(bytes, ref position, method, offset)
            : ReadInt32(bytes, ref position, method, offset);
        return new CilOperand.BranchTarget(position + delta);
    }

    private static CilOperand.SwitchTargets ReadSwitch(
        byte[] bytes,
        ref int position,
        string method,
        int offset)
    {
        var count = ReadInt32(bytes, ref position, method, offset);
        if (count < 0 || count > (bytes.Length - position) / sizeof(int))
        {
            throw Invalid(method, offset, "switch target table is invalid");
        }
        var deltas = ImmutableArray.CreateBuilder<int>(count);
        for (var index = 0; index < count; index++)
        {
            deltas.Add(ReadInt32(bytes, ref position, method, offset));
        }
        var targetBase = position;
        return new CilOperand.SwitchTargets(
            [.. deltas.Select(delta => targetBase + delta)]);
    }

    private (CilOperation Operation, CilOperand Operand) ReadRuntimeHandle(
        ManagedAssembly assembly,
        CliGenericContext genericContext,
        string method,
        byte[] bytes,
        ref int position,
        int offset)
    {
        var token = ReadInt32(bytes, ref position, method, offset);
        var handle = MetadataTokens.EntityHandle(token);
        if (handle.Kind == HandleKind.FieldDefinition)
        {
            return (
                CilOperation.LoadFieldToken,
                new CilOperand.Entity(new EntityKey(assembly.Identity, token)));
        }
        return (
            CilOperation.LoadTypeToken,
            new CilOperand.TypeIdentity(_typeSignatures.Resolve(assembly.Metadata,
                token,
                genericContext)));
    }

    private static CilOperand.TypeIdentity PrimitiveArrayElement(string name, CliValueKind kind) =>
        new(CliTypeIdentity.Primitive(name, kind));

    private static OpCode ReadOpCode(
        byte[] bytes,
        ref int position,
        string method,
        int offset)
    {
        var first = ReadByte(bytes, ref position, method, offset);
        if (first != 0xfe)
        {
            return OneByteOpCodes.TryGetValue(first, out var opCode)
                ? opCode
                : throw Invalid(method, offset, $"invalid CIL opcode 0x{first:x2}");
        }
        var second = ReadByte(bytes, ref position, method, offset);
        return TwoByteOpCodes.TryGetValue(second, out var twoByte)
            ? twoByte
            : throw Invalid(method, offset, $"invalid CIL opcode 0xfe{second:x2}");
    }

    private static byte ReadByte(byte[] bytes, ref int position, string method, int offset)
    {
        EnsureAvailable(bytes, position, sizeof(byte), method, offset);
        return bytes[position++];
    }

    private static sbyte ReadSByte(byte[] bytes, ref int position, string method, int offset) =>
        unchecked((sbyte)ReadByte(bytes, ref position, method, offset));

    private static ushort ReadUInt16(byte[] bytes, ref int position, string method, int offset)
    {
        EnsureAvailable(bytes, position, sizeof(ushort), method, offset);
        var value = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(position));
        position += sizeof(ushort);
        return value;
    }

    private static int ReadInt32(byte[] bytes, ref int position, string method, int offset)
    {
        EnsureAvailable(bytes, position, sizeof(int), method, offset);
        var value = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(position));
        position += sizeof(int);
        return value;
    }

    private static long ReadInt64(byte[] bytes, ref int position, string method, int offset)
    {
        EnsureAvailable(bytes, position, sizeof(long), method, offset);
        var value = BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(position));
        position += sizeof(long);
        return value;
    }

    private static void EnsureAvailable(
        byte[] bytes,
        int position,
        int count,
        string method,
        int offset)
    {
        if (position > bytes.Length - count)
        {
            throw Invalid(method, offset, "truncated CIL operand");
        }
    }

    private static CompilerException Invalid(string method, int offset, string message) =>
        new(new CompilerDiagnostic(DiagnosticCode.InvalidCil, message, method, offset));
}
