using System.Reflection;
using System.Reflection.Emit;

namespace NetWasm.Compiler.Tests;

// An assembly whose methods each store or pass an int32 where a native int is declared
// and the reverse, at every position ECMA-335 III.1.6 allows: locals, arguments,
// instance and static fields, returns, and the arguments of call, callvirt, newobj and
// calli, the last two also with a value pushed on two paths below later arguments. A
// native int carries bits above the low 32 where it is 64-bit, which truncation drops,
// and each widened int32 is negative, so its high half shows the sign extension, which
// a native unsigned int parameter gets too (the runtime's ECMA-335 augments).
internal static class ImplicitCoercionAssemblyBuilder
{
    public static byte[] Build()
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("ImplicitCoercions"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("ImplicitCoercions.dll");
        var box = module.DefineType("Box", TypeAttributes.Public | TypeAttributes.Sealed);
        var integer = box.DefineField("Int", typeof(int), FieldAttributes.Public);
        var native = box.DefineField("Native", typeof(nint), FieldAttributes.Public);
        var empty = box.DefineDefaultConstructor(MethodAttributes.Public);
        var filled = box.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard,
            [typeof(int), typeof(nint)]);
        var code = filled.GetILGenerator();
        code.Emit(OpCodes.Ldarg_0);
        code.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);
        code.Emit(OpCodes.Ldarg_0);
        code.Emit(OpCodes.Ldarg_1);
        code.Emit(OpCodes.Stfld, integer);
        code.Emit(OpCodes.Ldarg_0);
        code.Emit(OpCodes.Ldarg_2);
        code.Emit(OpCodes.Stfld, native);
        code.Emit(OpCodes.Ret);
        var mix = box.DefineMethod("Mix", MethodAttributes.Public | MethodAttributes.Virtual, typeof(int),
            [typeof(int), typeof(nint)]);
        code = mix.GetILGenerator();
        code.Emit(OpCodes.Ldarg_0);
        code.Emit(OpCodes.Ldfld, integer);
        code.Emit(OpCodes.Ldarg_0);
        code.Emit(OpCodes.Ldfld, native);
        Summarize(code, 10);
        code.Emit(OpCodes.Add);
        code.Emit(OpCodes.Ldarg_1);
        code.Emit(OpCodes.Ldc_I4, 1000);
        code.Emit(OpCodes.Mul);
        code.Emit(OpCodes.Add);
        code.Emit(OpCodes.Ldarg_2);
        Summarize(code, 100000);
        code.Emit(OpCodes.Add);
        code.Emit(OpCodes.Ret);
        box.CreateType();

        var type = module.DefineType("EntryPoint", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        var staticInteger = type.DefineField("s_int", typeof(int), FieldAttributes.Private | FieldAttributes.Static);
        var staticNative = type.DefineField("s_native", typeof(nint), FieldAttributes.Private | FieldAttributes.Static);
        var sum = type.DefineMethod("Sum", MethodAttributes.Private | MethodAttributes.Static, typeof(int),
            [typeof(int), typeof(nint), typeof(int)]);
        code = sum.GetILGenerator();
        code.Emit(OpCodes.Ldarg_0);
        code.Emit(OpCodes.Ldarg_1);
        Summarize(code, 10);
        code.Emit(OpCodes.Add);
        code.Emit(OpCodes.Ldarg_2);
        code.Emit(OpCodes.Ldc_I4, 100000);
        code.Emit(OpCodes.Mul);
        code.Emit(OpCodes.Add);
        code.Emit(OpCodes.Ret);

        // Locals: an int32 in a native int local and a native int in an int32 local.
        var locals = Method(type, "Locals", code =>
        {
            var wide = code.DeclareLocal(typeof(nint));
            var narrow = code.DeclareLocal(typeof(int));
            Narrow(code, -1000);
            code.Emit(OpCodes.Stloc, wide);
            Wide(code, 5);
            code.Emit(OpCodes.Stloc, narrow);
            code.Emit(OpCodes.Ldloc, wide);
            Summarize(code, 10);
            code.Emit(OpCodes.Ldloc, narrow);
            code.Emit(OpCodes.Add);
        });

        // Arguments: each argument stores the other's kind.
        var swap = type.DefineMethod("Swap", MethodAttributes.Private | MethodAttributes.Static, typeof(int),
            [typeof(int), typeof(nint)]);
        code = swap.GetILGenerator();
        code.Emit(OpCodes.Ldarg_0);
        code.Emit(OpCodes.Conv_I8);
        code.Emit(OpCodes.Ldc_I8, 1L << 32);
        code.Emit(OpCodes.Add);
        code.Emit(OpCodes.Conv_I);
        code.Emit(OpCodes.Ldarg_0);
        code.Emit(OpCodes.Ldc_I4, -7);
        code.Emit(OpCodes.Add);
        code.Emit(OpCodes.Starg, 1);
        code.Emit(OpCodes.Starg, 0);
        code.Emit(OpCodes.Ldarg_1);
        Summarize(code, 10);
        code.Emit(OpCodes.Ldarg_0);
        code.Emit(OpCodes.Add);
        code.Emit(OpCodes.Ret);
        var arguments = Method(type, "Arguments", code =>
        {
            code.Emit(OpCodes.Ldarg_0);
            code.Emit(OpCodes.Ldc_I4_0);
            code.Emit(OpCodes.Conv_I);
            code.Emit(OpCodes.Call, swap);
        });

        // Fields: instance and static fields of each kind store the other kind.
        var fields = Method(type, "Fields", code =>
        {
            var instance = code.DeclareLocal(box);
            code.Emit(OpCodes.Newobj, empty);
            code.Emit(OpCodes.Stloc, instance);
            code.Emit(OpCodes.Ldloc, instance);
            Wide(code, 3);
            code.Emit(OpCodes.Stfld, integer);
            code.Emit(OpCodes.Ldloc, instance);
            Narrow(code, -9);
            code.Emit(OpCodes.Stfld, native);
            Wide(code, 4);
            code.Emit(OpCodes.Stsfld, staticInteger);
            Narrow(code, -11);
            code.Emit(OpCodes.Stsfld, staticNative);
            code.Emit(OpCodes.Ldloc, instance);
            code.Emit(OpCodes.Ldfld, integer);
            code.Emit(OpCodes.Ldloc, instance);
            code.Emit(OpCodes.Ldfld, native);
            Summarize(code, 10);
            code.Emit(OpCodes.Add);
            code.Emit(OpCodes.Ldsfld, staticInteger);
            code.Emit(OpCodes.Ldc_I4, 1000);
            code.Emit(OpCodes.Mul);
            code.Emit(OpCodes.Add);
            code.Emit(OpCodes.Ldsfld, staticNative);
            Summarize(code, 100000);
            code.Emit(OpCodes.Add);
        });

        // Returns: a native int returned as int32 and the reverse.
        var returnNative = Method(type, "ReturnNative", typeof(nint), code => Narrow(code, -13));
        var returnInteger = Method(type, "ReturnInteger", typeof(int), code => Wide(code, 6));
        var returns = Method(type, "Returns", code =>
        {
            code.Emit(OpCodes.Ldarg_0);
            code.Emit(OpCodes.Call, returnNative);
            Summarize(code, 10);
            code.Emit(OpCodes.Ldarg_0);
            code.Emit(OpCodes.Call, returnInteger);
            code.Emit(OpCodes.Add);
        });

        // An int32 passed for a native unsigned int.
        var unsignedParameter = type.DefineMethod("UnsignedParameter", MethodAttributes.Private | MethodAttributes.Static,
            typeof(int), [typeof(nuint)]);
        code = unsignedParameter.GetILGenerator();
        code.Emit(OpCodes.Ldarg_0);
        Summarize(code, 10);
        code.Emit(OpCodes.Ret);
        var unsigned = Method(type, "Unsigned", code =>
        {
            Narrow(code, -60);
            code.Emit(OpCodes.Call, unsignedParameter);
        });

        // Calls: every argument of the other kind, pushed in the call's block.
        var calls = Method(type, "Calls", code =>
        {
            Wide(code, 1);
            Narrow(code, -20);
            Wide(code, 2);
            code.Emit(OpCodes.Call, sum);
        });

        // A first argument pushed on two paths, below two later ones that are coerced too.
        var mergedCall = Method(type, "MergedCall", code =>
        {
            Merged(code);
            Narrow(code, -21);
            Wide(code, 8);
            code.Emit(OpCodes.Call, sum);
        });

        // A constructor's and a virtual method's arguments.
        var instanceCalls = Method(type, "InstanceCalls", code =>
        {
            Wide(code, 9);
            Narrow(code, -30);
            code.Emit(OpCodes.Newobj, filled);
            Wide(code, 10);
            Narrow(code, -40);
            code.Emit(OpCodes.Callvirt, mix);
        });

        // calli: a merged first argument below the others and the function pointer.
        var indirect = Method(type, "Indirect", code =>
        {
            Merged(code);
            Narrow(code, -50);
            Wide(code, 11);
            code.Emit(OpCodes.Ldftn, sum);
            code.EmitCalli(OpCodes.Calli, CallingConventions.Standard, typeof(int),
                [typeof(int), typeof(nint), typeof(int)], null);
        });

        var run = type.DefineMethod("Run", MethodAttributes.Public | MethodAttributes.Static, typeof(int), [typeof(int)]);
        code = run.GetILGenerator();
        code.Emit(OpCodes.Ldc_I4, 17);
        foreach (var method in new[] { locals, arguments, fields, returns, unsigned, calls, mergedCall, instanceCalls, indirect })
        {
            code.Emit(OpCodes.Ldc_I4, 31);
            code.Emit(OpCodes.Mul);
            code.Emit(OpCodes.Ldarg_0);
            code.Emit(OpCodes.Call, method);
            code.Emit(OpCodes.Add);
        }
        code.Emit(OpCodes.Ret);
        type.CreateType();
        using var stream = new MemoryStream();
        assembly.Save(stream);
        return stream.ToArray();
    }

    private static MethodBuilder Method(TypeBuilder type, string name, Action<ILGenerator> write) =>
        Method(type, name, typeof(int), write);

    private static MethodBuilder Method(TypeBuilder type, string name, Type returnType, Action<ILGenerator> write)
    {
        var method = type.DefineMethod(name, MethodAttributes.Private | MethodAttributes.Static, returnType,
            [typeof(int)]);
        var code = method.GetILGenerator();
        write(code);
        code.Emit(OpCodes.Ret);
        return method;
    }

    // An int32: the input plus an offset.
    private static void Narrow(ILGenerator code, int offset)
    {
        code.Emit(OpCodes.Ldarg_0);
        code.Emit(OpCodes.Ldc_I4, offset);
        code.Emit(OpCodes.Add);
    }

    // A native int whose low 32 bits are the input plus an offset, with 1 above them
    // where native int is 64-bit.
    private static void Wide(ILGenerator code, int offset)
    {
        code.Emit(OpCodes.Ldarg_0);
        code.Emit(OpCodes.Conv_I8);
        code.Emit(OpCodes.Ldc_I8, (1L << 32) + offset);
        code.Emit(OpCodes.Add);
        code.Emit(OpCodes.Conv_I);
    }

    // A wide native int chosen on one of two paths by the input's sign.
    private static void Merged(ILGenerator code)
    {
        var negative = code.DefineLabel();
        var chosen = code.DefineLabel();
        code.Emit(OpCodes.Ldarg_0);
        code.Emit(OpCodes.Ldc_I4_0);
        code.Emit(OpCodes.Blt, negative);
        Wide(code, 12);
        code.Emit(OpCodes.Br, chosen);
        code.MarkLabel(negative);
        Wide(code, 13);
        code.MarkLabel(chosen);
    }

    // The native int on the stack as an int32: its low half plus its high half (0, or
    // -1 for a sign-extended negative int32) times a scale.
    private static void Summarize(ILGenerator code, int scale)
    {
        var value = code.DeclareLocal(typeof(long));
        code.Emit(OpCodes.Conv_I8);
        code.Emit(OpCodes.Stloc, value);
        code.Emit(OpCodes.Ldloc, value);
        code.Emit(OpCodes.Conv_I4);
        code.Emit(OpCodes.Ldloc, value);
        code.Emit(OpCodes.Ldc_I4, 32);
        code.Emit(OpCodes.Shr);
        code.Emit(OpCodes.Conv_I4);
        code.Emit(OpCodes.Ldc_I4, scale);
        code.Emit(OpCodes.Mul);
        code.Emit(OpCodes.Add);
    }
}
