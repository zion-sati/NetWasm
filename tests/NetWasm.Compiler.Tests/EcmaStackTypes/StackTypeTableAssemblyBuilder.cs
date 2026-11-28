// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Reflection;
using System.Reflection.Emit;

namespace NetWasm.Compiler.Tests.EcmaStackTypes;

// The operand kinds of ECMA-335 Partition III 1.5's tables: int32, int64, native int, F (as float32 or float64, both F
// on the stack), & (a managed address) and O (an object reference).
internal enum StackKind
{
    Int32,
    Int64,
    NativeInt,
    Float32,
    Float64,
    Address,
    Object,
}

// Which inputs a case is compared at. A case whose result depends on the size of a native int for a negative operand
// (an unsigned division, shift or conversion of one) is pointer-size dependent: desktop is 64-bit, so wasm32 compares
// it only where its operands are not negative; one that depends on it at every input (an int64 narrowed to a native
// int) is compared on 64-bit only. A negative float converted to an unsigned integer without an overflow check is
// unspecified everywhere, so that case takes only operands that are not negative.
internal enum InputDomain
{
    All,
    PointerSizeDependent,
    SixtyFourBitOnly,
    NotNegative,
}

// One row of a table: an instruction applied to operands of the given kinds.
internal sealed record StackTypeCase(
    string Family,
    OpCode Operation,
    StackKind[] Operands,
    StackKind Result,
    InputDomain Domain = InputDomain.All)
{
    public string Name => $"{Operation.Name}({string.Join(",", Operands)})";
}

// An assembly whose EntryPoint.Run computes each case's result as an int (a summary that is the same on 32- and 64-bit
// platforms where the case is not pointer-size dependent): Run(n) for n >= 0 is case n / Inputs.Length at input
// n % Inputs.Length with runtime operands; Run(-1) hashes every case at every input, runtime and constant operands;
// Run(-2) hashes the inputs every platform agrees on. Constant operands are each input's values as literals, so the
// optimizers fold them.
internal static class StackTypeTableAssemblyBuilder
{
    public static readonly int[] Inputs = [0, 5, 9, -7];

    // Inputs whose operands are all not negative.
    private static bool Portable(int input) => input - 3 >= 0;

    public static byte[] Build(IReadOnlyList<StackTypeCase> cases, string name)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName(name), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule(name + ".dll");
        var entry = module.DefineType("EntryPoint", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        var data = entry.DefineField("Data", typeof(int[]), FieldAttributes.Public | FieldAttributes.Static);
        var box = entry.DefineField("Box", typeof(object), FieldAttributes.Public | FieldAttributes.Static);
        var initializer = entry.DefineTypeInitializer().GetILGenerator();
        initializer.Emit(OpCodes.Ldc_I4, 16);
        initializer.Emit(OpCodes.Newarr, typeof(int));
        initializer.Emit(OpCodes.Stsfld, data);
        for (var index = 0; index < 16; index++)
        {
            initializer.Emit(OpCodes.Ldsfld, data);
            initializer.Emit(OpCodes.Ldc_I4, index);
            initializer.Emit(OpCodes.Ldc_I4, 100 + 7 * index);
            initializer.Emit(OpCodes.Stelem_I4);
        }
        initializer.Emit(OpCodes.Newobj, typeof(object).GetConstructor(Type.EmptyTypes)!);
        initializer.Emit(OpCodes.Stsfld, box);
        initializer.Emit(OpCodes.Ret);

        var fields = new Fields(data, box);
        var runtime = new MethodBuilder[cases.Count];
        var all = new List<(MethodBuilder Method, int? Input)>();
        var portable = new List<(MethodBuilder Method, int? Input)>();
        for (var index = 0; index < cases.Count; index++)
        {
            runtime[index] = DefineCase(entry, $"Case{index}", cases[index], fields, constant: null);
            var domain = cases[index].Domain;
            foreach (var input in Inputs.Where(input => domain != InputDomain.NotNegative || Portable(input)))
            {
                all.Add((runtime[index], input));
                if (domain == InputDomain.All || domain != InputDomain.SixtyFourBitOnly && Portable(input))
                {
                    portable.Add((runtime[index], input));
                }
            }
            foreach (var input in new[] { 5, -7 }.Where(input => domain != InputDomain.NotNegative || Portable(input)))
            {
                var folded = DefineCase(entry, $"Case{index}Constant{(input < 0 ? "Negative" : "")}", cases[index], fields, input);
                all.Add((folded, null));
                if (domain == InputDomain.All || domain != InputDomain.SixtyFourBitOnly && Portable(input))
                {
                    portable.Add((folded, null));
                }
            }
        }

        var hashAll = DefineHash(entry, "HashAll", all);
        var hashPortable = DefineHash(entry, "HashPortable", portable);
        var run = entry.DefineMethod("Run", MethodAttributes.Public | MethodAttributes.Static, typeof(int), [typeof(int)]);
        var code = run.GetILGenerator();
        var selected = code.DefineLabel();
        var notAll = code.DefineLabel();
        code.Emit(OpCodes.Ldarg_0);
        code.Emit(OpCodes.Ldc_I4_M1);
        code.Emit(OpCodes.Bne_Un, notAll);
        code.Emit(OpCodes.Call, hashAll);
        code.Emit(OpCodes.Ret);
        code.MarkLabel(notAll);
        code.Emit(OpCodes.Ldarg_0);
        code.Emit(OpCodes.Ldc_I4_0);
        code.Emit(OpCodes.Bge, selected);
        code.Emit(OpCodes.Call, hashPortable);
        code.Emit(OpCodes.Ret);
        code.MarkLabel(selected);
        var labels = cases.Select(_ => code.DefineLabel()).ToArray();
        code.Emit(OpCodes.Ldarg_0);
        code.Emit(OpCodes.Ldc_I4, Inputs.Length);
        code.Emit(OpCodes.Rem);
        var inputLocal = code.DeclareLocal(typeof(int));
        code.Emit(OpCodes.Stloc, inputLocal);
        code.Emit(OpCodes.Ldarg_0);
        code.Emit(OpCodes.Ldc_I4, Inputs.Length);
        code.Emit(OpCodes.Div);
        code.Emit(OpCodes.Switch, labels);
        code.Emit(OpCodes.Ldc_I4, int.MinValue);
        code.Emit(OpCodes.Ret);
        for (var index = 0; index < cases.Count; index++)
        {
            code.MarkLabel(labels[index]);
            EmitInput(code, inputLocal);
            code.Emit(OpCodes.Call, runtime[index]);
            code.Emit(OpCodes.Ret);
        }

        entry.CreateType();
        using var stream = new MemoryStream();
        assembly.Save(stream);
        return stream.ToArray();
    }

    private sealed record Fields(FieldBuilder Data, FieldBuilder Box);

    // Inputs[index], from the index in the local.
    private static void EmitInput(ILGenerator code, LocalBuilder index)
    {
        var done = code.DefineLabel();
        var labels = Inputs.Select(_ => code.DefineLabel()).ToArray();
        code.Emit(OpCodes.Ldloc, index);
        code.Emit(OpCodes.Switch, labels);
        code.Emit(OpCodes.Ldc_I4_0);
        code.Emit(OpCodes.Br, done);
        for (var input = 0; input < Inputs.Length; input++)
        {
            code.MarkLabel(labels[input]);
            code.Emit(OpCodes.Ldc_I4, Inputs[input]);
            code.Emit(OpCodes.Br, done);
        }
        code.MarkLabel(done);
    }

    // A method hashing each listed case's result: at its input when it takes one, as is when its operands are literals.
    private static MethodBuilder DefineHash(TypeBuilder entry, string name, List<(MethodBuilder Method, int? Input)> calls)
    {
        var hash = entry.DefineMethod(name, MethodAttributes.Private | MethodAttributes.Static, typeof(int), Type.EmptyTypes);
        var code = hash.GetILGenerator();
        code.Emit(OpCodes.Ldc_I4, 17);
        foreach (var (method, input) in calls)
        {
            code.Emit(OpCodes.Ldc_I4, 31);
            code.Emit(OpCodes.Mul);
            if (input is { } value) code.Emit(OpCodes.Ldc_I4, value);
            code.Emit(OpCodes.Call, method);
            code.Emit(OpCodes.Add);
        }
        code.Emit(OpCodes.Ret);
        return hash;
    }

    // static int name(int x) (or name() with constant operands): the case's operation on its operands, as an int.
    private static MethodBuilder DefineCase(TypeBuilder entry, string name, StackTypeCase @case, Fields fields, int? constant)
    {
        var method = entry.DefineMethod(name, MethodAttributes.Public | MethodAttributes.Static, typeof(int),
            constant is null ? [typeof(int)] : Type.EmptyTypes);
        method.SetImplementationFlags(MethodImplAttributes.NoInlining);
        var code = method.GetILGenerator();
        var result = code.DeclareLocal(typeof(int));
        var overflows = @case.Family == "overflow" || @case.Operation.Name!.StartsWith("conv.ovf", StringComparison.Ordinal);
        if (overflows) code.BeginExceptionBlock();
        for (var operand = 0; operand < @case.Operands.Length; operand++)
        {
            EmitOperand(code, @case, operand, fields, constant);
        }
        if (@case.Family == "switch")
        {
            // An int64 operand becomes a native int with bits above the low 32 where native ints are 64-bit.
            if (@case.Operands[0] == StackKind.Int64) code.Emit(OpCodes.Conv_I);
            var targets = Enumerable.Range(0, 4).Select(_ => code.DefineLabel()).ToArray();
            var done = code.DefineLabel();
            code.Emit(OpCodes.Switch, targets);
            code.Emit(OpCodes.Ldc_I4, 99);
            code.Emit(OpCodes.Br, done);
            for (var target = 0; target < targets.Length; target++)
            {
                code.MarkLabel(targets[target]);
                code.Emit(OpCodes.Ldc_I4, 10 * (target + 1));
                code.Emit(OpCodes.Br, done);
            }
            code.MarkLabel(done);
        }
        else if (@case.Family == "branch")
        {
            var taken = code.DefineLabel();
            var done = code.DefineLabel();
            code.Emit(@case.Operation, taken);
            code.Emit(OpCodes.Ldc_I4_0);
            code.Emit(OpCodes.Br, done);
            code.MarkLabel(taken);
            code.Emit(OpCodes.Ldc_I4_1);
            code.MarkLabel(done);
        }
        else
        {
            code.Emit(@case.Operation);
        }
        if (@case.Family == "convert" && @case.Operands[0] == StackKind.Address)
        {
            // An address converted to an integer is where it lies: as an offset from the array's first element,
            // converted the same way, it is the same on every platform.
            code.Emit(OpCodes.Ldsfld, fields.Data);
            code.Emit(OpCodes.Ldc_I4_0);
            code.Emit(OpCodes.Ldelema, typeof(int));
            code.Emit(@case.Operation);
            code.Emit(OpCodes.Sub);
        }
        Summarize(code, @case.Result);
        code.Emit(OpCodes.Stloc, result);
        if (overflows)
        {
            code.BeginCatchBlock(typeof(OverflowException));
            code.Emit(OpCodes.Pop);
            code.Emit(OpCodes.Ldc_I4, -77777);
            code.Emit(OpCodes.Stloc, result);
            code.EndExceptionBlock();
        }
        code.Emit(OpCodes.Ldloc, result);
        code.Emit(OpCodes.Ret);
        return method;
    }

    // Operand A (index 0) is x - 3, negative for small inputs; B (index 1) is (x & 7) + 1, from 1 to 8, so it divides
    // and shifts. An address is an element of Data: A at x & 3, B at (x & 3) + 4, or (as the subtrahend of an address)
    // at (x & 3) + 8. An integer beside an address is a byte offset, (x & 3) * 4; a native int compared with an address
    // is Data[5]'s address. An object is Box, or null for an even A.
    private static void EmitOperand(ILGenerator code, StackTypeCase @case, int operand, Fields fields, int? constant)
    {
        var kind = @case.Operands[operand];
        var besideAddress = @case.Operands.Length == 2 && @case.Operands[1 - operand] == StackKind.Address;
        void X()
        {
            if (constant is { } value) code.Emit(OpCodes.Ldc_I4, value);
            else code.Emit(OpCodes.Ldarg_0);
        }
        void Integer()
        {
            if (constant is { } value)
            {
                code.Emit(OpCodes.Ldc_I4, besideAddress ? (value & 3) * 4 : operand == 0 ? value - 3 : (value & 7) + 1);
                return;
            }
            X();
            if (besideAddress)
            {
                code.Emit(OpCodes.Ldc_I4_3);
                code.Emit(OpCodes.And);
                code.Emit(OpCodes.Ldc_I4_2);
                code.Emit(OpCodes.Shl);
            }
            else if (operand == 0)
            {
                code.Emit(OpCodes.Ldc_I4_3);
                code.Emit(OpCodes.Sub);
            }
            else
            {
                code.Emit(OpCodes.Ldc_I4_7);
                code.Emit(OpCodes.And);
                code.Emit(OpCodes.Ldc_I4_1);
                code.Emit(OpCodes.Add);
            }
        }
        void Element(int offset)
        {
            code.Emit(OpCodes.Ldsfld, fields.Data);
            X();
            code.Emit(OpCodes.Ldc_I4_3);
            code.Emit(OpCodes.And);
            if (offset != 0)
            {
                code.Emit(OpCodes.Ldc_I4, offset);
                code.Emit(OpCodes.Add);
            }
            code.Emit(OpCodes.Ldelema, typeof(int));
        }
        switch (kind)
        {
            case StackKind.Int32:
                Integer();
                return;
            case StackKind.Int64:
                Integer();
                code.Emit(OpCodes.Conv_I8);
                code.Emit(OpCodes.Ldc_I8, operand == 0 ? 0x1_0000_0001L : 3L);
                code.Emit(OpCodes.Mul);
                return;
            case StackKind.NativeInt when @case.Operands.Length == 2 && @case.Operands[1 - operand] == StackKind.Address &&
                @case.Family is "compare" or "branch":
                code.Emit(OpCodes.Ldsfld, fields.Data);
                code.Emit(OpCodes.Ldc_I4_5);
                code.Emit(OpCodes.Ldelema, typeof(int));
                code.Emit(OpCodes.Conv_I);
                return;
            case StackKind.NativeInt:
                Integer();
                code.Emit(OpCodes.Conv_I);
                return;
            case StackKind.Float32:
                Integer();
                code.Emit(OpCodes.Conv_R4);
                code.Emit(OpCodes.Ldc_R4, operand == 0 ? 0.25f : 0.5f);
                code.Emit(operand == 0 ? OpCodes.Add : OpCodes.Mul);
                return;
            case StackKind.Float64:
                Integer();
                code.Emit(OpCodes.Conv_R8);
                code.Emit(OpCodes.Ldc_R8, operand == 0 ? 0.125 : 0.75);
                code.Emit(operand == 0 ? OpCodes.Add : OpCodes.Mul);
                return;
            case StackKind.Address:
                var subtrahend = operand == 1 && @case.Operands[0] == StackKind.Address && @case.Operation.Name!.StartsWith("sub", StringComparison.Ordinal);
                Element(operand == 0 ? (@case.Operation.Name!.StartsWith("sub", StringComparison.Ordinal) && @case.Operands[1] != StackKind.Address ? 8 : 0)
                    : subtrahend ? 8 : 4);
                return;
            case StackKind.Object:
                if (operand == 1)
                {
                    code.Emit(OpCodes.Ldsfld, fields.Box);
                    return;
                }
                var isNull = code.DefineLabel();
                var done = code.DefineLabel();
                X();
                code.Emit(OpCodes.Ldc_I4_1);
                code.Emit(OpCodes.And);
                code.Emit(OpCodes.Brfalse, isNull);
                code.Emit(OpCodes.Ldsfld, fields.Box);
                code.Emit(OpCodes.Br, done);
                code.MarkLabel(isNull);
                code.Emit(OpCodes.Ldnull);
                code.MarkLabel(done);
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(@case));
        }
    }

    // The result as an int: an int64 folds its halves, a native int sign-extends to int64 first (so a correctly
    // widened value reads the same at either size), a float its bits, an address the element it points to.
    private static void Summarize(ILGenerator code, StackKind kind)
    {
        switch (kind)
        {
            case StackKind.Int32:
                return;
            case StackKind.NativeInt:
                code.Emit(OpCodes.Conv_I8);
                goto case StackKind.Int64;
            case StackKind.Int64:
                {
                    var wide = code.DeclareLocal(typeof(long));
                    code.Emit(OpCodes.Stloc, wide);
                    code.Emit(OpCodes.Ldloc, wide);
                    code.Emit(OpCodes.Conv_I4);
                    code.Emit(OpCodes.Ldloc, wide);
                    code.Emit(OpCodes.Ldc_I4, 32);
                    code.Emit(OpCodes.Shr);
                    code.Emit(OpCodes.Conv_I4);
                    code.Emit(OpCodes.Ldc_I4, 1_000_003);
                    code.Emit(OpCodes.Mul);
                    code.Emit(OpCodes.Xor);
                    return;
                }
            case StackKind.Float32:
            case StackKind.Float64:
                {
                    var real = code.DeclareLocal(typeof(double));
                    code.Emit(OpCodes.Conv_R8);
                    code.Emit(OpCodes.Stloc, real);
                    code.Emit(OpCodes.Ldloca, real);
                    code.Emit(OpCodes.Ldind_I8);
                    goto case StackKind.Int64;
                }
            case StackKind.Address:
                code.Emit(OpCodes.Ldind_I4);
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }
}
