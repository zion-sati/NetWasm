// From the compiler harness's ReflectionPrimitivesCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.Reflection.Emit;
namespace ReflectionPrimitivesFixture;

public static class EntryPoint
{
    static bool Is(OpCode code, short value, int size, string name, OperandType operand, FlowControl flow, OpCodeType kind,
        StackBehaviour pop, StackBehaviour push, int delta) =>
        code.Value == value && code.Size == size && code.Name == name && code.ToString() == name &&
        code.OperandType == operand && code.FlowControl == flow && code.OpCodeType == kind &&
        code.StackBehaviourPop == pop && code.StackBehaviourPush == push && code.EvaluationStackDelta == delta;

    public static int Run(int input)
    {
        // One-byte instructions across operand kinds, flow control and stack behaviour.
        if (!Is(OpCodes.Nop, 0x00, 1, "nop", OperandType.InlineNone, FlowControl.Next, OpCodeType.Primitive, StackBehaviour.Pop0, StackBehaviour.Push0, 0)) return 1;
        if (!Is(OpCodes.Ldarg_0, 0x02, 1, "ldarg.0", OperandType.InlineNone, FlowControl.Next, OpCodeType.Macro, StackBehaviour.Pop0, StackBehaviour.Push1, 1)) return 2;
        if (!Is(OpCodes.Ldc_I4_M1, 0x15, 1, "ldc.i4.m1", OperandType.InlineNone, FlowControl.Next, OpCodeType.Macro, StackBehaviour.Pop0, StackBehaviour.Pushi, 1)) return 3;
        if (!Is(OpCodes.Br_S, 0x2b, 1, "br.s", OperandType.ShortInlineBrTarget, FlowControl.Branch, OpCodeType.Macro, StackBehaviour.Pop0, StackBehaviour.Push0, 0)) return 4;
        if (!Is(OpCodes.Brtrue, 0x3a, 1, "brtrue", OperandType.InlineBrTarget, FlowControl.Cond_Branch, OpCodeType.Primitive, StackBehaviour.Popi, StackBehaviour.Push0, -1)) return 5;
        if (!Is(OpCodes.Call, 0x28, 1, "call", OperandType.InlineMethod, FlowControl.Call, OpCodeType.Primitive, StackBehaviour.Varpop, StackBehaviour.Varpush, 0)) return 6;
        if (!Is(OpCodes.Ret, 0x2a, 1, "ret", OperandType.InlineNone, FlowControl.Return, OpCodeType.Primitive, StackBehaviour.Varpop, StackBehaviour.Push0, 0)) return 7;
        if (!Is(OpCodes.Throw, 0x7a, 1, "throw", OperandType.InlineNone, FlowControl.Throw, OpCodeType.Objmodel, StackBehaviour.Popref, StackBehaviour.Push0, -1)) return 8;
        if (!Is(OpCodes.Switch, 0x45, 1, "switch", OperandType.InlineSwitch, FlowControl.Cond_Branch, OpCodeType.Primitive, StackBehaviour.Popi, StackBehaviour.Push0, -1)) return 9;
        if (!Is(OpCodes.Ldstr, 0x72, 1, "ldstr", OperandType.InlineString, FlowControl.Next, OpCodeType.Objmodel, StackBehaviour.Pop0, StackBehaviour.Pushref, 1)) return 10;
        if (!Is(OpCodes.Stelem_Ref, 0xa2, 1, "stelem.ref", OperandType.InlineNone, FlowControl.Next, OpCodeType.Objmodel, StackBehaviour.Popref_popi_popref, StackBehaviour.Push0, -3)) return 11;
        if (!Is(OpCodes.Conv_Ovf_I1_Un, unchecked((short)0x82), 1, "conv.ovf.i1.un", OperandType.InlineNone, FlowControl.Next, OpCodeType.Primitive, StackBehaviour.Pop1, StackBehaviour.Pushi, 0)) return 12;
        if (!Is(OpCodes.Ldc_R8, 0x23, 1, "ldc.r8", OperandType.InlineR, FlowControl.Next, OpCodeType.Primitive, StackBehaviour.Pop0, StackBehaviour.Pushr8, 1)) return 13;

        // Two-byte instructions and prefixes.
        if (!Is(OpCodes.Ceq, unchecked((short)0xfe01), 2, "ceq", OperandType.InlineNone, FlowControl.Next, OpCodeType.Primitive, StackBehaviour.Pop1_pop1, StackBehaviour.Pushi, -1)) return 14;
        if (!Is(OpCodes.Ldarg, unchecked((short)0xfe09), 2, "ldarg", OperandType.InlineVar, FlowControl.Next, OpCodeType.Primitive, StackBehaviour.Pop0, StackBehaviour.Push1, 1)) return 15;
        if (!Is(OpCodes.Tailcall, unchecked((short)0xfe14), 2, "tail.", OperandType.InlineNone, FlowControl.Meta, OpCodeType.Prefix, StackBehaviour.Pop0, StackBehaviour.Push0, 0)) return 16;
        if (!Is(OpCodes.Readonly, unchecked((short)0xfe1e), 2, "readonly.", OperandType.InlineNone, FlowControl.Meta, OpCodeType.Prefix, StackBehaviour.Pop0, StackBehaviour.Push0, 0)) return 17;
        if (OpCodes.Unaligned.Name != "unaligned." || OpCodes.Volatile.Name != "volatile." || OpCodes.Constrained.Name != "constrained." || OpCodes.Sizeof.Name != "sizeof") return 18;

        // Single-byte operands.
        if (!OpCodes.TakesSingleByteArgument(OpCodes.Br_S) || !OpCodes.TakesSingleByteArgument(OpCodes.Ldc_I4_S) || !OpCodes.TakesSingleByteArgument(OpCodes.Ldloc_S)) return 19;
        if (OpCodes.TakesSingleByteArgument(OpCodes.Br) || OpCodes.TakesSingleByteArgument(OpCodes.Ldc_I4) || OpCodes.TakesSingleByteArgument(OpCodes.Nop)) return 20;

        // Equality, hashing and the default instruction.
        OpCode nop = OpCodes.Nop, copy = OpCodes.Nop;
        if (nop != copy || !nop.Equals(copy) || !nop.Equals((object)copy) || nop == OpCodes.Break || nop.Equals("nop") || OpCodes.Ceq.GetHashCode() != OpCodes.Ceq.Value) return 21;
        OpCode none = default;
        if (none.Size != 0 || none.Name != null || none.ToString() != null || none != OpCodes.Nop) return 22;

        // Enum values match upstream's.
        if ((int)FlowControl.Branch != 0 || (int)FlowControl.Meta != 4 || (int)FlowControl.Throw != 8 || (int)OpCodeType.Macro != 1 || (int)OpCodeType.Primitive != 5) return 23;
        if ((int)OperandType.InlineNone != 5 || (int)OperandType.ShortInlineVar != 18 || (int)StackBehaviour.Pop0 != 0 || (int)StackBehaviour.Varpush != 27) return 24;

        // Labels compare by id.
        Label first = default, second = default;
        if (first.Id != 0 || first != second || !first.Equals((object)second) || first.GetHashCode() != 0 || first.Equals(null)) return 25;
        return input + 1;
    }
}
