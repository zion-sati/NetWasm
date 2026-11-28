// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace NetWasm.Compiler.Tests;

// A program per instruction that dereferences an object reference or a managed address, in which that instruction is
// the only way to raise NullReferenceException: EntryPoint.Run(input) passes a null address or reference (input 0)
// or a valid one to a method that does nothing but the instruction, and returns -1 when the exception is caught and
// 1 when nothing is thrown.
internal static class NullDereferenceAssemblyBuilder
{
    public static readonly string[] Operations = ["ldind", "stind", "ldobj", "stobj", "cpobj", "initobj", "ldflda"];

    public static byte[] Build(string operation)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("NullDereference_" + operation), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("NullDereference_" + operation + ".dll");
        var pair = module.DefineType("Pair", TypeAttributes.Public | TypeAttributes.Sealed |
            TypeAttributes.SequentialLayout, typeof(ValueType));
        pair.DefineField("A", typeof(int), FieldAttributes.Public);
        pair.DefineField("B", typeof(int), FieldAttributes.Public);
        var box = module.DefineType("Box", TypeAttributes.Public | TypeAttributes.Sealed);
        var value = box.DefineField("Value", typeof(int), FieldAttributes.Public);
        var create = box.DefineDefaultConstructor(MethodAttributes.Public);
        var entry = module.DefineType("EntryPoint", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        var sink = entry.DefineField("Sink", pair, FieldAttributes.Public | FieldAttributes.Static);

        var target = operation switch
        {
            "ldind" or "stind" => typeof(int),
            "ldflda" => box,
            _ => pair.MakeByRefType(),
        };
        var parameter = operation is "ldind" or "stind" ? typeof(int).MakeByRefType() : target;
        var dereference = entry.DefineMethod("Dereference", MethodAttributes.Private | MethodAttributes.Static,
            typeof(void), [parameter]);
        dereference.SetImplementationFlags(MethodImplAttributes.NoInlining);
        var code = dereference.GetILGenerator();
        code.Emit(OpCodes.Ldarg_0);
        switch (operation)
        {
            case "ldind":
                code.Emit(OpCodes.Ldind_I4);
                code.Emit(OpCodes.Pop);
                break;
            case "stind":
                code.Emit(OpCodes.Ldc_I4_7);
                code.Emit(OpCodes.Stind_I4);
                break;
            case "ldobj":
                code.Emit(OpCodes.Ldobj, pair);
                code.Emit(OpCodes.Stsfld, sink);
                break;
            case "stobj":
                code.Emit(OpCodes.Ldsfld, sink);
                code.Emit(OpCodes.Stobj, pair);
                break;
            case "cpobj":
                code.Emit(OpCodes.Ldsflda, sink);
                code.Emit(OpCodes.Cpobj, pair);
                break;
            case "initobj":
                code.Emit(OpCodes.Initobj, pair);
                break;
            case "ldflda":
                code.Emit(OpCodes.Ldflda, value);
                code.Emit(OpCodes.Call, typeof(Unsafe).GetMethod(nameof(Unsafe.IsNullRef))!.MakeGenericMethod(typeof(int)));
                code.Emit(OpCodes.Pop);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(operation), operation, null);
        }
        code.Emit(OpCodes.Ret);

        var run = entry.DefineMethod("Run", MethodAttributes.Public | MethodAttributes.Static, typeof(int), [typeof(int)]);
        code = run.GetILGenerator();
        var result = code.DeclareLocal(typeof(int));
        var integer = code.DeclareLocal(typeof(int));
        var valid = code.DeclareLocal(pair);
        var chosen = code.DefineLabel();
        var nonNull = code.DefineLabel();
        code.BeginExceptionBlock();
        code.Emit(OpCodes.Ldarg_0);
        code.Emit(OpCodes.Brtrue, nonNull);
        if (operation == "ldflda")
        {
            code.Emit(OpCodes.Ldnull);
        }
        else
        {
            var element = operation is "ldind" or "stind" ? typeof(int) : (Type)pair;
            code.Emit(OpCodes.Call, typeof(Unsafe).GetMethod(nameof(Unsafe.NullRef))!.MakeGenericMethod(element));
        }
        code.Emit(OpCodes.Br, chosen);
        code.MarkLabel(nonNull);
        switch (operation)
        {
            case "ldflda":
                code.Emit(OpCodes.Newobj, create);
                break;
            case "ldind" or "stind":
                code.Emit(OpCodes.Ldloca, integer);
                break;
            default:
                code.Emit(OpCodes.Ldloca, valid);
                break;
        }
        code.MarkLabel(chosen);
        code.Emit(OpCodes.Call, dereference);
        code.Emit(OpCodes.Ldc_I4_1);
        code.Emit(OpCodes.Stloc, result);
        code.BeginCatchBlock(typeof(NullReferenceException));
        code.Emit(OpCodes.Pop);
        code.Emit(OpCodes.Ldc_I4_M1);
        code.Emit(OpCodes.Stloc, result);
        code.EndExceptionBlock();
        code.Emit(OpCodes.Ldloc, result);
        code.Emit(OpCodes.Ret);

        pair.CreateType();
        box.CreateType();
        entry.CreateType();
        using var stream = new MemoryStream();
        assembly.Save(stream);
        return stream.ToArray();
    }
}
