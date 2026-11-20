// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;

namespace NetWasm.Compiler.Tests.Correctness;

// ldfld, ldflda and stfld naming static fields, which C# never emits and Avalonia's XAML compiler does: on a null
// object and on a real one, behind volatile., on an object a call with a side effect produced, on a static whose
// type's .cctor must run first, on a reference-typed static and on a generic type's static. Run returns 42 when
// every check holds, or 100 plus the number of the first that failed.
internal sealed class StaticFieldInstanceAccessFixtureBuilder : IEmittedAssemblyBuilder
{
    public ImmutableArray<byte> Build()
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("StaticFieldInstanceAccessFixture"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("StaticFieldInstanceAccessFixture.dll");

        var holderBuilder = module.DefineType("StaticFieldInstanceAccessFixture.Holder", TypeAttributes.Public, typeof(object));
        var number = holderBuilder.DefineField("Number", typeof(int), FieldAttributes.Public | FieldAttributes.Static);
        var text = holderBuilder.DefineField("Text", typeof(string), FieldAttributes.Public | FieldAttributes.Static);
        var calls = holderBuilder.DefineField("Calls", typeof(int), FieldAttributes.Public | FieldAttributes.Static);
        var lazyRan = holderBuilder.DefineField("LazyRan", typeof(int), FieldAttributes.Public | FieldAttributes.Static);
        var holderConstructor = holderBuilder.DefineDefaultConstructor(MethodAttributes.Public);
        var holderInitializer = holderBuilder.DefineTypeInitializer().GetILGenerator();
        holderInitializer.Emit(OpCodes.Ldc_I4_5);
        holderInitializer.Emit(OpCodes.Stsfld, number);
        holderInitializer.Emit(OpCodes.Ldstr, "first");
        holderInitializer.Emit(OpCodes.Stsfld, text);
        holderInitializer.Emit(OpCodes.Ret);
        // Make() counts its calls and returns null, so a dropped object still has its side effect.
        var make = holderBuilder.DefineMethod("Make", MethodAttributes.Public | MethodAttributes.Static, holderBuilder, Type.EmptyTypes);
        var makeCode = make.GetILGenerator();
        makeCode.Emit(OpCodes.Ldsfld, calls);
        makeCode.Emit(OpCodes.Ldc_I4_1);
        makeCode.Emit(OpCodes.Add);
        makeCode.Emit(OpCodes.Stsfld, calls);
        makeCode.Emit(OpCodes.Ldnull);
        makeCode.Emit(OpCodes.Ret);
        var holder = holderBuilder.CreateType();

        // A precise .cctor that runs only when Lazy's static is first touched, recording in Holder that it ran.
        var lazyBuilder = module.DefineType("StaticFieldInstanceAccessFixture.Lazy", TypeAttributes.Public, typeof(object));
        var lazyValue = lazyBuilder.DefineField("Value", typeof(int), FieldAttributes.Public | FieldAttributes.Static);
        var lazyInitializer = lazyBuilder.DefineTypeInitializer().GetILGenerator();
        lazyInitializer.Emit(OpCodes.Ldc_I4_7);
        lazyInitializer.Emit(OpCodes.Stsfld, lazyValue);
        lazyInitializer.Emit(OpCodes.Ldc_I4_1);
        lazyInitializer.Emit(OpCodes.Stsfld, lazyRan);
        lazyInitializer.Emit(OpCodes.Ret);
        var lazy = lazyBuilder.CreateType();

        var genericBuilder = module.DefineType("StaticFieldInstanceAccessFixture.Box`1", TypeAttributes.Public, typeof(object));
        genericBuilder.DefineGenericParameters("T");
        var genericField = genericBuilder.DefineField("Count", typeof(int), FieldAttributes.Public | FieldAttributes.Static);
        var generic = genericBuilder.CreateType();
        var closedField = TypeBuilder.GetField(generic.MakeGenericType(typeof(long)), genericField);

        var entryPoint = module.DefineType("StaticFieldInstanceAccessFixture.EntryPoint",
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        var run = entryPoint.DefineMethod("Run", MethodAttributes.Public | MethodAttributes.Static, typeof(int), [typeof(int)]);
        var code = run.GetILGenerator();
        var failed = code.DeclareLocal(typeof(int));
        var fail = code.DefineLabel();
        var check = 0;
        void Expect(Action<ILGenerator> value, int expected)
        {
            check++;
            code.Emit(OpCodes.Ldc_I4, 100 + check);
            code.Emit(OpCodes.Stloc, failed);
            value(code);
            code.Emit(OpCodes.Ldc_I4, expected);
            code.Emit(OpCodes.Bne_Un, fail);
        }
        // 1: ldfld of a static on null reads it, running Holder's .cctor first.
        Expect(il => { il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Ldfld, number); }, 5);
        // 2: and on a real object.
        Expect(il => { il.Emit(OpCodes.Newobj, holderConstructor); il.Emit(OpCodes.Ldfld, number); }, 5);
        // 3: stfld on null writes the static.
        Expect(il =>
        {
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ldc_I4, 9);
            il.Emit(OpCodes.Stfld, number);
            il.Emit(OpCodes.Ldsfld, number);
        }, 9);
        // 4: ldflda on null addresses the static.
        Expect(il =>
        {
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ldflda, number);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4, 11);
            il.Emit(OpCodes.Stind_I4);
            il.Emit(OpCodes.Ldind_I4);
        }, 11);
        // 5: behind volatile.
        Expect(il => { il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Volatile); il.Emit(OpCodes.Ldfld, number); }, 11);
        Expect(il =>
        {
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ldc_I4, 13);
            il.Emit(OpCodes.Volatile);
            il.Emit(OpCodes.Stfld, number);
            il.Emit(OpCodes.Ldsfld, number);
        }, 13);
        // 7: the object's producer still runs, once per access.
        Expect(il =>
        {
            il.Emit(OpCodes.Call, make);
            il.Emit(OpCodes.Ldfld, number);
            il.Emit(OpCodes.Call, make);
            il.Emit(OpCodes.Ldc_I4, 2);
            il.Emit(OpCodes.Stfld, number);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldsfld, calls);
        }, 2);
        // 9: Lazy's .cctor has not run, then runs at the ldfld.
        Expect(il => { il.Emit(OpCodes.Ldsfld, lazyRan); }, 0);
        Expect(il => { il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Ldfld, lazyValue); }, 7);
        Expect(il => { il.Emit(OpCodes.Ldsfld, lazyRan); }, 1);
        // 11: a reference-typed static.
        Expect(il =>
        {
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ldstr, "second");
            il.Emit(OpCodes.Stfld, text);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ldfld, text);
            il.Emit(OpCodes.Callvirt, typeof(string).GetProperty(nameof(string.Length))!.GetMethod!);
        }, 6);
        // 12: a generic type's static, through a field reference on the instantiation.
        Expect(il =>
        {
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ldc_I4, 21);
            il.Emit(OpCodes.Stfld, closedField);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ldfld, closedField);
        }, 21);
        code.Emit(OpCodes.Ldc_I4, 42);
        code.Emit(OpCodes.Ret);
        code.MarkLabel(fail);
        code.Emit(OpCodes.Ldloc, failed);
        code.Emit(OpCodes.Ret);
        _ = holder;
        _ = lazy;
        entryPoint.CreateType();

        using var stream = new MemoryStream();
        assembly.Save(stream);
        return [.. stream.ToArray()];
    }
}
