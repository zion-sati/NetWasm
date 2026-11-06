// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime 474ed963699fc29809015392738a15231aaae2de src/coreclr/tools/Common/TypeSystem/IL/Stubs/UnsafeIntrinsics.cs
// Adaptations: the bodies are NetWasm CIL models, built for the method instance,
// so the type operand is the instance's type argument.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers;

// System.Runtime.CompilerServices.Unsafe's intrinsics have bodies that throw
// PlatformNotSupportedException, or bodies written for runtimes that expand
// the call; as NativeAOT does, the compiler gives each the body it stands for.
internal sealed class UnsafeIntrinsicBodyBuilder : IUnsafeIntrinsicBodyBuilder
{
    public CilMethodBody? Build(MethodInstanceModel method)
    {
        ArgumentNullException.ThrowIfNull(method);
        if (method.DeclaringType.FullName != "System.Runtime.CompilerServices.Unsafe")
        {
            return null;
        }

        return method.Definition.Name switch
        {
            "AsPointer" => Body(method, Load(0), Simple(CilOperation.ConvertNativeUInt)),
            "As" or "AsRef" => Body(method, Load(0)),
            "Add" => Body(method, Load(1), Typed(CilOperation.SizeOf, method), Simple(CilOperation.ConvertNativeInt),
                Simple(CilOperation.Multiply), Load(0), Simple(CilOperation.Add)),
            "AddByteOffset" => Body(method, Load(0), Load(1), Simple(CilOperation.Add)),
            "Copy" => Body(method, Load(0), Load(1), Typed(CilOperation.LoadObject, method), Typed(CilOperation.StoreObject, method)),
            "CopyBlock" => Block(method, CilOperation.CopyBlock, unaligned: false),
            "CopyBlockUnaligned" => Block(method, CilOperation.CopyBlock, unaligned: true),
            "InitBlock" => Block(method, CilOperation.InitializeBlock, unaligned: false),
            "InitBlockUnaligned" => Block(method, CilOperation.InitializeBlock, unaligned: true),
            "Read" => ReadWrite(method, write: false, unaligned: false),
            "Write" => ReadWrite(method, write: true, unaligned: false),
            "ReadUnaligned" => ReadWrite(method, write: false, unaligned: true),
            "WriteUnaligned" => ReadWrite(method, write: true, unaligned: true),
            "AreSame" => Body(method, Load(0), Load(1), Simple(CilOperation.CompareEqual)),
            "IsAddressGreaterThan" => Body(method, Load(0), Load(1), Simple(CilOperation.CompareGreaterThanUnsigned)),
            "IsAddressLessThan" => Body(method, Load(0), Load(1), Simple(CilOperation.CompareLessThanUnsigned)),
            "ByteOffset" => Body(method, Load(1), Load(0), Simple(CilOperation.Subtract)),
            "NullRef" => Body(method, new(CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
                Simple(CilOperation.ConvertNativeUInt)),
            "IsNullRef" => Body(method, Load(0), new(CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
                Simple(CilOperation.ConvertNativeUInt), Simple(CilOperation.CompareEqual)),
            "SkipInit" => Body(method),
            "Subtract" => Body(method, Load(0), Load(1), Typed(CilOperation.SizeOf, method), Simple(CilOperation.ConvertNativeInt),
                Simple(CilOperation.Multiply), Simple(CilOperation.Subtract)),
            "SubtractByteOffset" => Body(method, Load(0), Load(1), Simple(CilOperation.Subtract)),
            "Unbox" => Body(method, Load(0), Typed(CilOperation.Unbox, method)),
            _ => null,
        };
    }

    private static CilMethodBody ReadWrite(MethodInstanceModel method, bool write, bool unaligned)
    {
        List<(CilOperation, CilOperand)> code = [Load(0)];
        if (write) code.Add(Load(1));
        if (unaligned) code.Add(Unaligned());
        code.Add(Typed(write ? CilOperation.StoreObject : CilOperation.LoadObject, method));
        return Body(method, [.. code]);
    }

    private static CilMethodBody Block(MethodInstanceModel method, CilOperation operation, bool unaligned)
    {
        List<(CilOperation, CilOperand)> code = [Load(0), Load(1), Load(2)];
        if (unaligned) code.Add(Unaligned());
        code.Add(Simple(operation));
        return Body(method, [.. code]);
    }

    private static (CilOperation, CilOperand) Load(int argument) =>
        (CilOperation.LoadArgument, new CilOperand.Index(argument));

    private static (CilOperation, CilOperand) Simple(CilOperation operation) => (operation, new CilOperand.None());

    // ILEmitter's EmitUnaligned: a byte alignment.
    private static (CilOperation, CilOperand) Unaligned() => (CilOperation.Unaligned, new CilOperand.Index(1));

    // The intrinsic's first type parameter, which every typed intrinsic names.
    private static (CilOperation, CilOperand) Typed(CilOperation operation, MethodInstanceModel method) =>
        (operation, new CilOperand.TypeIdentity(method.MethodArguments[0]));

    // The instructions, then ret; a stack no deeper than the arguments it loads.
    private static CilMethodBody Body(MethodInstanceModel method, params ReadOnlySpan<(CilOperation Operation, CilOperand Operand)> code)
    {
        var instructions = ImmutableArray.CreateBuilder<CilInstruction>(code.Length + 1);
        foreach (var (operation, operand) in code)
        {
            instructions.Add(new CilInstruction(instructions.Count, instructions.Count + 1, operation, operand));
        }
        instructions.Add(new CilInstruction(instructions.Count, instructions.Count + 1, CilOperation.Return, new CilOperand.None()));
        return new CilMethodBody(method.Definition, 3, [], instructions.MoveToImmutable())
        {
            MethodInstance = method,
        };
    }
}
