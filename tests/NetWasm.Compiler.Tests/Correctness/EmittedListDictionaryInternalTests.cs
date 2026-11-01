using System.Collections;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class EmittedListDictionaryInternalTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void RuntimeVisibleListDictionaryMatchesDesktopAcrossCompilerProfilesAndTargets()
    {
        var fixture = new CorpusFixture(
            "ListDictionaryInternalCompatibilityCorpus",
            "NetWasm.Correctness.ListDictionaryInternalCorpus",
            string.Empty,
            [0, 1, 2, 3, 4, 5, 6])
        {
            OracleMode = OracleMode.SameIl,
            SameSourceReason = null,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
            CaptureCompilerDiagnostics = true,
            ReferenceAssemblyAliases = ImmutableDictionary<string, string>.Empty.Add(
                "System.Private.CoreLib",
                "NetWasm.CoreLib"),
            ExpectedReturnValues = ImmutableDictionary<int, int>.Empty
                .Add(0, 1200)
                .Add(1, 602040)
                .Add(2, 1)
                .Add(3, 1)
                .Add(4, 1)
                .Add(5, 111)
                .Add(6, 330),
        };

        runner.Run(fixture, new ListDictionaryInternalFixtureBuilder());
    }
}

// Fixture authoring only. ListDictionaryInternal is runtime-visible for binary
// compatibility but intentionally absent from the desktop reference assembly,
// so this contract must be authored as programmatic CIL rather than C# source.
internal sealed class ListDictionaryInternalFixtureBuilder : IEmittedAssemblyBuilder
{
    private static readonly Type DictionaryType = typeof(object).Assembly.GetType(
        "System.Collections.ListDictionaryInternal",
        throwOnError: true)!;

    private static readonly ConstructorInfo Constructor =
        DictionaryType.GetConstructor(Type.EmptyTypes)!;

    private static readonly MethodInfo Add =
        typeof(IDictionary).GetMethod(nameof(IDictionary.Add))!;

    private static readonly MethodInfo Remove =
        typeof(IDictionary).GetMethod(nameof(IDictionary.Remove))!;

    private static readonly MethodInfo Contains =
        typeof(IDictionary).GetMethod(nameof(IDictionary.Contains))!;

    private static readonly MethodInfo GetItem =
        typeof(IDictionary).GetProperty("Item")!.GetMethod!;

    private static readonly MethodInfo SetItem =
        typeof(IDictionary).GetProperty("Item")!.SetMethod!;

    private static readonly MethodInfo GetEnumerator =
        typeof(IDictionary).GetMethod(nameof(IDictionary.GetEnumerator))!;

    private static readonly MethodInfo GetCount =
        typeof(ICollection).GetProperty(nameof(ICollection.Count))!.GetMethod!;

    private static readonly MethodInfo CopyTo =
        typeof(ICollection).GetMethod(nameof(ICollection.CopyTo))!;

    private static readonly MethodInfo MoveNext =
        typeof(IEnumerator).GetMethod(nameof(IEnumerator.MoveNext))!;

    private static readonly MethodInfo Reset =
        typeof(IEnumerator).GetMethod(nameof(IEnumerator.Reset))!;

    private static readonly MethodInfo GetKey =
        typeof(IDictionaryEnumerator).GetProperty(nameof(IDictionaryEnumerator.Key))!.GetMethod!;

    private static readonly MethodInfo GetValue =
        typeof(IDictionaryEnumerator).GetProperty(nameof(IDictionaryEnumerator.Value))!.GetMethod!;

    private static readonly MethodInfo GetEntry =
        typeof(IDictionaryEnumerator).GetProperty(nameof(IDictionaryEnumerator.Entry))!.GetMethod!;

    private static readonly MethodInfo GetEntryKey =
        typeof(DictionaryEntry).GetProperty(nameof(DictionaryEntry.Key))!.GetMethod!;

    private static readonly MethodInfo GetEntryValue =
        typeof(DictionaryEntry).GetProperty(nameof(DictionaryEntry.Value))!.GetMethod!;

    public ImmutableArray<byte> Build()
    {
        var assembly = new PersistedAssemblyBuilder(
            new AssemblyName("ListDictionaryInternalCompatibilityCorpus"),
            typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("ListDictionaryInternalCompatibilityCorpus.dll");
        var type = module.DefineType(
            "NetWasm.Correctness.ListDictionaryInternalCorpus.EntryPoint",
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);

        MethodBuilder[] cases =
        [
            DefineBasics(type),
            DefineEnumeration(type),
            DefineDuplicateAddInvalidation(type),
            DefineMissingRemoveInvalidation(type),
            DefineIndexerUpdateInvalidation(type),
            DefineEnumeratorState(type),
            DefineCopies(type),
        ];
        DefineDispatcher(type, cases);
        DefineTrace(type);
        type.CreateType();

        using var stream = new MemoryStream();
        assembly.Save(stream);
        return [.. stream.ToArray()];
    }

    private static MethodBuilder DefineBasics(TypeBuilder type)
    {
        var method = DefineCase(type, "Basics");
        var code = method.GetILGenerator();
        var dictionary = code.DeclareLocal(typeof(IDictionary));
        var failed = code.DefineLabel();

        EmitNewDictionary(code, dictionary);
        EmitAdd(code, dictionary, 1, 10);
        EmitSet(code, dictionary, 2, 20);
        EmitSet(code, dictionary, 1, 100);
        EmitRemove(code, dictionary, 2);

        code.Emit(OpCodes.Ldloc, dictionary);
        code.Emit(OpCodes.Callvirt, GetCount);
        code.Emit(OpCodes.Ldc_I4_1);
        code.Emit(OpCodes.Bne_Un, failed);

        code.Emit(OpCodes.Ldloc, dictionary);
        EmitBoxedInt32(code, 1);
        code.Emit(OpCodes.Callvirt, Contains);
        code.Emit(OpCodes.Brfalse, failed);

        code.Emit(OpCodes.Ldloc, dictionary);
        EmitBoxedInt32(code, 1);
        code.Emit(OpCodes.Callvirt, GetItem);
        code.Emit(OpCodes.Unbox_Any, typeof(int));
        code.Emit(OpCodes.Ldc_I4, 100);
        code.Emit(OpCodes.Bne_Un, failed);

        code.Emit(OpCodes.Ldc_I4, 1200);
        code.Emit(OpCodes.Ret);
        code.MarkLabel(failed);
        code.Emit(OpCodes.Ldc_I4_M1);
        code.Emit(OpCodes.Ret);
        return method;
    }

    private static MethodBuilder DefineEnumeration(TypeBuilder type)
    {
        var method = DefineCase(type, "Enumeration");
        var code = method.GetILGenerator();
        var dictionary = code.DeclareLocal(typeof(IDictionary));
        var enumerator = code.DeclareLocal(typeof(IDictionaryEnumerator));
        var score = code.DeclareLocal(typeof(int));
        var test = code.DefineLabel();
        var body = code.DefineLabel();

        EmitNewDictionary(code, dictionary);
        EmitAdd(code, dictionary, 3, 30);
        EmitAdd(code, dictionary, 1, 10);
        EmitAdd(code, dictionary, 2, 20);
        code.Emit(OpCodes.Ldloc, dictionary);
        code.Emit(OpCodes.Callvirt, GetEnumerator);
        code.Emit(OpCodes.Stloc, enumerator);
        code.Emit(OpCodes.Br, test);

        code.MarkLabel(body);
        code.Emit(OpCodes.Ldloc, score);
        code.Emit(OpCodes.Ldc_I4_S, 100);
        code.Emit(OpCodes.Mul);
        code.Emit(OpCodes.Ldloc, enumerator);
        code.Emit(OpCodes.Callvirt, GetKey);
        code.Emit(OpCodes.Unbox_Any, typeof(int));
        code.Emit(OpCodes.Ldc_I4_S, 10);
        code.Emit(OpCodes.Mul);
        code.Emit(OpCodes.Add);
        code.Emit(OpCodes.Ldloc, enumerator);
        code.Emit(OpCodes.Callvirt, GetValue);
        code.Emit(OpCodes.Unbox_Any, typeof(int));
        code.Emit(OpCodes.Add);
        code.Emit(OpCodes.Stloc, score);

        code.MarkLabel(test);
        code.Emit(OpCodes.Ldloc, enumerator);
        code.Emit(OpCodes.Callvirt, MoveNext);
        code.Emit(OpCodes.Brtrue, body);
        code.Emit(OpCodes.Ldloc, score);
        code.Emit(OpCodes.Ret);
        return method;
    }

    private static MethodBuilder DefineDuplicateAddInvalidation(TypeBuilder type)
    {
        var method = DefineCase(type, "DuplicateAddInvalidation");
        var code = method.GetILGenerator();
        var dictionary = code.DeclareLocal(typeof(IDictionary));
        var enumerator = code.DeclareLocal(typeof(IDictionaryEnumerator));
        var result = code.DeclareLocal(typeof(int));

        EmitNewDictionary(code, dictionary);
        EmitAdd(code, dictionary, 1, 10);
        EmitGetEnumerator(code, dictionary, enumerator);

        code.BeginExceptionBlock();
        EmitAdd(code, dictionary, 1, 20);
        code.Emit(OpCodes.Ldc_I4_M1);
        code.Emit(OpCodes.Stloc, result);
        code.BeginCatchBlock(typeof(ArgumentException));
        code.Emit(OpCodes.Pop);
        code.Emit(OpCodes.Ldc_I4_0);
        code.Emit(OpCodes.Stloc, result);
        code.EndExceptionBlock();

        var returnResult = code.DefineLabel();
        code.Emit(OpCodes.Ldloc, result);
        code.Emit(OpCodes.Brtrue, returnResult);
        EmitInvalidationProbe(code, enumerator, MoveNext, result);
        code.MarkLabel(returnResult);
        code.Emit(OpCodes.Ldloc, result);
        code.Emit(OpCodes.Ret);
        return method;
    }

    private static MethodBuilder DefineMissingRemoveInvalidation(TypeBuilder type)
    {
        var method = DefineCase(type, "MissingRemoveInvalidation");
        var code = method.GetILGenerator();
        var dictionary = code.DeclareLocal(typeof(IDictionary));
        var enumerator = code.DeclareLocal(typeof(IDictionaryEnumerator));
        var result = code.DeclareLocal(typeof(int));

        EmitNewDictionary(code, dictionary);
        EmitAdd(code, dictionary, 1, 10);
        EmitGetEnumerator(code, dictionary, enumerator);
        EmitRemove(code, dictionary, 2);
        EmitInvalidationProbe(code, enumerator, MoveNext, result);
        code.Emit(OpCodes.Ldloc, result);
        code.Emit(OpCodes.Ret);
        return method;
    }

    private static MethodBuilder DefineIndexerUpdateInvalidation(TypeBuilder type)
    {
        var method = DefineCase(type, "IndexerUpdateInvalidation");
        var code = method.GetILGenerator();
        var dictionary = code.DeclareLocal(typeof(IDictionary));
        var enumerator = code.DeclareLocal(typeof(IDictionaryEnumerator));
        var result = code.DeclareLocal(typeof(int));

        EmitNewDictionary(code, dictionary);
        EmitAdd(code, dictionary, 1, 10);
        EmitGetEnumerator(code, dictionary, enumerator);
        EmitSet(code, dictionary, 1, 20);
        EmitInvalidationProbe(code, enumerator, Reset, result);
        code.Emit(OpCodes.Ldloc, result);
        code.Emit(OpCodes.Ret);
        return method;
    }

    private static MethodBuilder DefineEnumeratorState(TypeBuilder type)
    {
        var method = DefineCase(type, "EnumeratorState");
        var code = method.GetILGenerator();
        var dictionary = code.DeclareLocal(typeof(IDictionary));
        var enumerator = code.DeclareLocal(typeof(IDictionaryEnumerator));
        var score = code.DeclareLocal(typeof(int));

        EmitNewDictionary(code, dictionary);
        EmitAdd(code, dictionary, 1, 10);
        EmitGetEnumerator(code, dictionary, enumerator);
        EmitInvalidCurrentProbe(code, enumerator, score, 1);

        code.Emit(OpCodes.Ldloc, enumerator);
        code.Emit(OpCodes.Callvirt, MoveNext);
        code.Emit(OpCodes.Pop);
        code.Emit(OpCodes.Ldloc, enumerator);
        code.Emit(OpCodes.Callvirt, GetEntry);
        code.Emit(OpCodes.Pop);

        code.Emit(OpCodes.Ldloc, enumerator);
        code.Emit(OpCodes.Callvirt, MoveNext);
        code.Emit(OpCodes.Pop);
        EmitInvalidCurrentProbe(code, enumerator, score, 10);

        code.Emit(OpCodes.Ldloc, enumerator);
        code.Emit(OpCodes.Callvirt, Reset);
        EmitInvalidCurrentProbe(code, enumerator, score, 100);
        code.Emit(OpCodes.Ldloc, score);
        code.Emit(OpCodes.Ret);
        return method;
    }

    private static MethodBuilder DefineCopies(TypeBuilder type)
    {
        var method = DefineCase(type, "Copies");
        var code = method.GetILGenerator();
        var dictionary = code.DeclareLocal(typeof(IDictionary));
        var objects = code.DeclareLocal(typeof(object[]));
        var entries = code.DeclareLocal(typeof(DictionaryEntry[]));
        var entry = code.DeclareLocal(typeof(DictionaryEntry));
        var score = code.DeclareLocal(typeof(int));

        EmitNewDictionary(code, dictionary);
        EmitAdd(code, dictionary, 1, 10);
        EmitAdd(code, dictionary, 2, 20);

        code.Emit(OpCodes.Ldc_I4_3);
        code.Emit(OpCodes.Newarr, typeof(object));
        code.Emit(OpCodes.Stloc, objects);
        code.Emit(OpCodes.Ldloc, dictionary);
        code.Emit(OpCodes.Ldloc, objects);
        code.Emit(OpCodes.Ldc_I4_1);
        code.Emit(OpCodes.Callvirt, CopyTo);
        code.Emit(OpCodes.Ldloc, objects);
        code.Emit(OpCodes.Ldc_I4_1);
        code.Emit(OpCodes.Ldelem_Ref);
        code.Emit(OpCodes.Unbox_Any, typeof(DictionaryEntry));
        code.Emit(OpCodes.Stloc, entry);
        EmitEntryScore(code, entry, score);

        code.Emit(OpCodes.Ldc_I4_2);
        code.Emit(OpCodes.Newarr, typeof(DictionaryEntry));
        code.Emit(OpCodes.Stloc, entries);
        code.Emit(OpCodes.Ldloc, dictionary);
        code.Emit(OpCodes.Ldloc, entries);
        code.Emit(OpCodes.Ldc_I4_0);
        code.Emit(OpCodes.Callvirt, CopyTo);
        code.Emit(OpCodes.Ldloc, entries);
        code.Emit(OpCodes.Ldc_I4_1);
        code.Emit(OpCodes.Ldelem, typeof(DictionaryEntry));
        code.Emit(OpCodes.Stloc, entry);
        EmitEntryScore(code, entry, score);

        code.Emit(OpCodes.Ldloc, score);
        code.Emit(OpCodes.Ret);
        return method;
    }

    private static void EmitEntryScore(
        ILGenerator code,
        LocalBuilder entry,
        LocalBuilder score)
    {
        code.Emit(OpCodes.Ldloc, score);
        code.Emit(OpCodes.Ldloca, entry);
        code.Emit(OpCodes.Call, GetEntryKey);
        code.Emit(OpCodes.Unbox_Any, typeof(int));
        code.Emit(OpCodes.Ldc_I4_S, 100);
        code.Emit(OpCodes.Mul);
        code.Emit(OpCodes.Add);
        code.Emit(OpCodes.Ldloca, entry);
        code.Emit(OpCodes.Call, GetEntryValue);
        code.Emit(OpCodes.Unbox_Any, typeof(int));
        code.Emit(OpCodes.Add);
        code.Emit(OpCodes.Stloc, score);
    }

    private static void EmitInvalidationProbe(
        ILGenerator code,
        LocalBuilder enumerator,
        MethodInfo operation,
        LocalBuilder result)
    {
        code.BeginExceptionBlock();
        code.Emit(OpCodes.Ldloc, enumerator);
        code.Emit(OpCodes.Callvirt, operation);
        if (operation.ReturnType != typeof(void))
        {
            code.Emit(OpCodes.Pop);
        }
        code.Emit(OpCodes.Ldc_I4_M1);
        code.Emit(OpCodes.Stloc, result);
        code.BeginCatchBlock(typeof(InvalidOperationException));
        code.Emit(OpCodes.Pop);
        code.Emit(OpCodes.Ldc_I4_1);
        code.Emit(OpCodes.Stloc, result);
        code.EndExceptionBlock();
    }

    private static void EmitInvalidCurrentProbe(
        ILGenerator code,
        LocalBuilder enumerator,
        LocalBuilder score,
        int increment)
    {
        code.BeginExceptionBlock();
        code.Emit(OpCodes.Ldloc, enumerator);
        code.Emit(OpCodes.Callvirt, GetEntry);
        code.Emit(OpCodes.Pop);
        code.BeginCatchBlock(typeof(InvalidOperationException));
        code.Emit(OpCodes.Pop);
        code.Emit(OpCodes.Ldloc, score);
        code.Emit(OpCodes.Ldc_I4, increment);
        code.Emit(OpCodes.Add);
        code.Emit(OpCodes.Stloc, score);
        code.EndExceptionBlock();
    }

    private static void EmitNewDictionary(ILGenerator code, LocalBuilder dictionary)
    {
        code.Emit(OpCodes.Newobj, Constructor);
        code.Emit(OpCodes.Stloc, dictionary);
    }

    private static void EmitAdd(ILGenerator code, LocalBuilder dictionary, int key, int value)
    {
        code.Emit(OpCodes.Ldloc, dictionary);
        EmitBoxedInt32(code, key);
        EmitBoxedInt32(code, value);
        code.Emit(OpCodes.Callvirt, Add);
    }

    private static void EmitSet(ILGenerator code, LocalBuilder dictionary, int key, int value)
    {
        code.Emit(OpCodes.Ldloc, dictionary);
        EmitBoxedInt32(code, key);
        EmitBoxedInt32(code, value);
        code.Emit(OpCodes.Callvirt, SetItem);
    }

    private static void EmitRemove(ILGenerator code, LocalBuilder dictionary, int key)
    {
        code.Emit(OpCodes.Ldloc, dictionary);
        EmitBoxedInt32(code, key);
        code.Emit(OpCodes.Callvirt, Remove);
    }

    private static void EmitGetEnumerator(
        ILGenerator code,
        LocalBuilder dictionary,
        LocalBuilder enumerator)
    {
        code.Emit(OpCodes.Ldloc, dictionary);
        code.Emit(OpCodes.Callvirt, GetEnumerator);
        code.Emit(OpCodes.Stloc, enumerator);
    }

    private static void EmitBoxedInt32(ILGenerator code, int value)
    {
        code.Emit(OpCodes.Ldc_I4, value);
        code.Emit(OpCodes.Box, typeof(int));
    }

    private static MethodBuilder DefineCase(TypeBuilder type, string name) =>
        type.DefineMethod(
            name,
            MethodAttributes.Private | MethodAttributes.Static,
            typeof(int),
            Type.EmptyTypes);

    private static void DefineDispatcher(TypeBuilder type, MethodBuilder[] cases)
    {
        var method = type.DefineMethod(
            "Run",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(int),
            [typeof(int)]);
        var code = method.GetILGenerator();
        var labels = cases.Select(_ => code.DefineLabel()).ToArray();
        code.Emit(OpCodes.Ldarg_0);
        code.Emit(OpCodes.Switch, labels);
        code.Emit(OpCodes.Ldc_I4_M1);
        code.Emit(OpCodes.Ret);
        for (var index = 0; index < cases.Length; index++)
        {
            code.MarkLabel(labels[index]);
            code.Emit(OpCodes.Call, cases[index]);
            code.Emit(OpCodes.Ret);
        }
    }

    private static void DefineTrace(TypeBuilder type)
    {
        var method = type.DefineMethod(
            "Trace",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(int),
            Type.EmptyTypes);
        var code = method.GetILGenerator();
        code.Emit(OpCodes.Ldc_I4_0);
        code.Emit(OpCodes.Ret);
    }
}
