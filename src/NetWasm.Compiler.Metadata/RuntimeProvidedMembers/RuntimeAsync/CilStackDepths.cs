// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers.RuntimeAsync;

// The evaluation stack's depth before each instruction of a body, by offset, from each instruction's pops and pushes, a
// handler starting with the exception (catch, filter) or with nothing (finally, fault), and leave emptying the stack;
// null for a body with an instruction whose effect this does not follow, or with two depths at one instruction.
internal static class CilStackDepths
{
    public static IReadOnlyDictionary<int, int>? Compute(CilMethodBody body)
    {
        var positions = body.Instructions.Select((instruction, index) => (instruction.Offset, index))
            .ToDictionary(item => item.Offset, item => item.index);
        var depths = new Dictionary<int, int>();
        var work = new Stack<int>();
        bool Reach(int offset, int depth)
        {
            if (!positions.ContainsKey(offset)) return false;
            if (depths.TryGetValue(offset, out var known)) return known == depth;
            depths.Add(offset, depth);
            work.Push(offset);
            return true;
        }
        if (body.Instructions.IsEmpty) return depths;
        Reach(body.Instructions[0].Offset, 0);
        foreach (var region in body.ExceptionRegions)
        {
            var exception = region.Kind is CilExceptionRegionKind.Catch or CilExceptionRegionKind.Filter ? 1 : 0;
            if (!Reach(region.HandlerOffset, exception)) return null;
            if (region.FilterOffset is { } filter && !Reach(filter, 1)) return null;
        }
        while (work.Count != 0)
        {
            var offset = work.Pop();
            var instruction = body.Instructions[positions[offset]];
            var before = depths[offset];
            if (Effect(instruction, before) is not var (pops, pushes) || pops > before) return null;
            var after = before - pops + pushes;
            switch (instruction.Operation)
            {
                case CilOperation.Leave:
                    if (!Reach(((CilOperand.BranchTarget)instruction.Operand).Offset, 0)) return null;
                    continue;
                case CilOperation.Branch:
                    if (!Reach(((CilOperand.BranchTarget)instruction.Operand).Offset, after)) return null;
                    continue;
                case CilOperation.Return or CilOperation.Throw or CilOperation.Rethrow or CilOperation.EndFinally or
                    CilOperation.EndFilter:
                    continue;
            }
            switch (instruction.Operand)
            {
                case CilOperand.BranchTarget target when !Reach(target.Offset, after):
                    return null;
                case CilOperand.SwitchTargets targets when targets.Offsets.Any(target => !Reach(target, after)):
                    return null;
            }
            if (!Reach(instruction.NextOffset, after)) return null;
        }
        return depths;
    }

    internal static (int Pops, int Pushes)? Effect(CilInstruction instruction, int depth) => instruction.Operation switch
    {
        CilOperation.Nop or CilOperation.Constrained or CilOperation.Readonly or CilOperation.Volatile or
            CilOperation.Unaligned or CilOperation.Break or CilOperation.Branch or CilOperation.Leave or
            CilOperation.EndFinally or CilOperation.Rethrow => (0, 0),
        CilOperation.Return => (depth, 0),
        CilOperation.LoadArgument or CilOperation.LoadArgumentAddress or CilOperation.LoadLocal or
            CilOperation.LoadLocalAddress or CilOperation.LoadInt32 or CilOperation.LoadInt64 or CilOperation.LoadFloat32 or
            CilOperation.LoadFloat64 or CilOperation.LoadNull or CilOperation.LoadString or CilOperation.LoadStaticField or
            CilOperation.LoadStaticFieldAddress or CilOperation.SizeOf or CilOperation.LoadTypeToken or
            CilOperation.LoadFieldToken or CilOperation.LoadMethodToken or CilOperation.LoadFunction => (0, 1),
        CilOperation.StoreArgument or CilOperation.StoreLocal or CilOperation.Pop or CilOperation.StoreStaticField or
            CilOperation.InitializeObject or CilOperation.BranchIfTrue or CilOperation.BranchIfFalse or CilOperation.Switch or
            CilOperation.Throw or CilOperation.EndFilter => (1, 0),
        CilOperation.Duplicate => (1, 2),
        CilOperation.LoadField or CilOperation.LoadFieldAddress or CilOperation.LoadObject or CilOperation.Box or
            CilOperation.Unbox or CilOperation.UnboxAny or CilOperation.CastClass or CilOperation.IsInstance or
            CilOperation.LoadArrayLength or CilOperation.NewArray or CilOperation.Negate or CilOperation.OnesComplement or
            CilOperation.ConvertInt32 or CilOperation.ConvertInt32Unsigned or CilOperation.ConvertInt64 or
            CilOperation.ConvertInt64Unsigned or CilOperation.ConvertNativeInt or CilOperation.ConvertNativeUInt or
            CilOperation.ConvertFloat32 or CilOperation.ConvertFloat64 or CilOperation.ConvertFloatUnsigned or
            CilOperation.ConvertNumeric or CilOperation.CheckFinite or CilOperation.LoadVirtualFunction or
            CilOperation.LocalAllocate => (1, 1),
        CilOperation.StoreField or CilOperation.StoreObject or CilOperation.CopyObject or CilOperation.BranchIfEqual or
            CilOperation.BranchIfNotEqual or CilOperation.BranchIfGreaterThanSigned or CilOperation.BranchIfGreaterThanUnsigned or
            CilOperation.BranchIfGreaterThanOrEqualSigned or CilOperation.BranchIfGreaterThanOrEqualUnsigned or
            CilOperation.BranchIfLessThanSigned or CilOperation.BranchIfLessThanUnsigned or
            CilOperation.BranchIfLessThanOrEqualSigned or CilOperation.BranchIfLessThanOrEqualUnsigned => (2, 0),
        CilOperation.Add or CilOperation.Subtract or CilOperation.Multiply or CilOperation.Divide or
            CilOperation.DivideUnsigned or CilOperation.Remainder or CilOperation.RemainderUnsigned or
            CilOperation.BitwiseAnd or CilOperation.BitwiseOr or CilOperation.BitwiseXor or CilOperation.ShiftLeft or
            CilOperation.ShiftRightSigned or CilOperation.ShiftRightUnsigned or CilOperation.AddChecked or
            CilOperation.AddCheckedUnsigned or CilOperation.SubtractChecked or CilOperation.SubtractCheckedUnsigned or
            CilOperation.MultiplyChecked or CilOperation.MultiplyCheckedUnsigned or CilOperation.CompareEqual or
            CilOperation.CompareGreaterThanSigned or CilOperation.CompareGreaterThanUnsigned or
            CilOperation.CompareLessThanSigned or CilOperation.CompareLessThanUnsigned or CilOperation.LoadArrayElement or
            CilOperation.LoadArrayElementAddress or CilOperation.LoadArrayElementReference => (2, 1),
        CilOperation.StoreArrayElement or CilOperation.StoreArrayElementReference or CilOperation.CopyBlock or
            CilOperation.InitializeBlock => (3, 0),
        CilOperation.Call or CilOperation.CallVirtual when instruction.Operand is CilOperand.MethodInstance { Value: var callee } =>
            (callee.Signature.ParameterTypes.Length + (callee.Definition.IsStatic ? 0 : 1),
                callee.Signature.ReturnType == CliValueKind.Void ? 0 : 1),
        CilOperation.NewObject when instruction.Operand is CilOperand.MethodInstance { Value: var constructor } =>
            (constructor.Signature.ParameterTypes.Length, 1),
        // The arguments, then the function pointer (as TypedStackValidator counts a calli).
        CilOperation.CallIndirect when instruction.Operand is CilOperand.CallSite { Signature: var signature } =>
            (signature.ParameterTypes.Length + 1, signature.ReturnType == CliValueKind.Void ? 0 : 1),
        _ => null,
    };
}
