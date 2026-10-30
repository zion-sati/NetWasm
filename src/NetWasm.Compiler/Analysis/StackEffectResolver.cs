using System;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

// How instructions move values on the evaluation stack, for rules that follow
// a value from the instruction that pushes it to the one that takes it before
// the body's stacks are validated.
internal interface IStackEffectResolver
{
    // How many values an instruction pops and pushes, or null for one this
    // does not follow.
    (int Pops, int Pushes)? Resolve(CilInstruction instruction);
}

internal sealed class StackEffectResolver(ICalledMethodResolver calledMethods) : IStackEffectResolver
{
    private readonly ICalledMethodResolver _calledMethods = calledMethods ??
        throw new ArgumentNullException(nameof(calledMethods));

    public (int Pops, int Pushes)? Resolve(CilInstruction instruction) => instruction.Operation switch
    {
        CilOperation.Nop or CilOperation.Constrained or CilOperation.Readonly or CilOperation.Volatile or CilOperation.Unaligned => (0, 0),
        CilOperation.LoadArgument or CilOperation.LoadArgumentAddress or CilOperation.LoadLocal or CilOperation.LoadLocalAddress or
            CilOperation.LoadInt32 or CilOperation.LoadInt64 or CilOperation.LoadFloat32 or CilOperation.LoadFloat64 or
            CilOperation.LoadNull or CilOperation.LoadString or CilOperation.LoadStaticField or CilOperation.LoadStaticFieldAddress or
            CilOperation.SizeOf => (0, 1),
        CilOperation.StoreArgument or CilOperation.StoreLocal or CilOperation.Pop or CilOperation.StoreStaticField or
            CilOperation.InitializeObject or CilOperation.BranchIfTrue or CilOperation.BranchIfFalse => (1, 0),
        CilOperation.Duplicate => (1, 2),
        CilOperation.LoadField or CilOperation.LoadFieldAddress or CilOperation.LoadObject or CilOperation.Box or
            CilOperation.Unbox or CilOperation.UnboxAny or CilOperation.CastClass or CilOperation.IsInstance or
            CilOperation.LoadArrayLength or CilOperation.NewArray or CilOperation.Negate or CilOperation.OnesComplement or
            CilOperation.ConvertInt32 or CilOperation.ConvertInt32Unsigned or CilOperation.ConvertInt64 or
            CilOperation.ConvertInt64Unsigned or CilOperation.ConvertNativeInt or CilOperation.ConvertNativeUInt or
            CilOperation.ConvertFloat32 or CilOperation.ConvertFloat64 or CilOperation.ConvertFloatUnsigned or
            CilOperation.ConvertNumeric or CilOperation.CheckFinite => (1, 1),
        CilOperation.StoreField or CilOperation.StoreObject => (2, 0),
        CilOperation.Add or CilOperation.Subtract or CilOperation.Multiply or CilOperation.Divide or CilOperation.DivideUnsigned or
            CilOperation.Remainder or CilOperation.RemainderUnsigned or CilOperation.BitwiseAnd or CilOperation.BitwiseOr or
            CilOperation.BitwiseXor or CilOperation.ShiftLeft or CilOperation.ShiftRightSigned or CilOperation.ShiftRightUnsigned or
            CilOperation.AddChecked or CilOperation.AddCheckedUnsigned or CilOperation.SubtractChecked or
            CilOperation.SubtractCheckedUnsigned or CilOperation.MultiplyChecked or CilOperation.MultiplyCheckedUnsigned or
            CilOperation.CompareEqual or CilOperation.CompareGreaterThanSigned or CilOperation.CompareGreaterThanUnsigned or
            CilOperation.CompareLessThanSigned or CilOperation.CompareLessThanUnsigned or CilOperation.LoadArrayElement or
            CilOperation.LoadArrayElementAddress or CilOperation.LoadArrayElementReference => (2, 1),
        CilOperation.StoreArrayElement or CilOperation.StoreArrayElementReference => (3, 0),
        CilOperation.Call or CilOperation.CallVirtual when _calledMethods.Resolve(instruction) is { } callee =>
            (callee.Signature.ParameterTypes.Length + (callee.Definition.IsStatic ? 0 : 1),
                callee.Signature.ReturnType == CliValueKind.Void ? 0 : 1),
        CilOperation.NewObject when _calledMethods.Resolve(instruction) is { } constructor =>
            (constructor.Signature.ParameterTypes.Length, 1),
        _ => null,
    };
}
