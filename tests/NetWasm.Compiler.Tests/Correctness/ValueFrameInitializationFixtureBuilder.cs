// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;

namespace NetWasm.Compiler.Tests.Correctness;

internal sealed class ValueFrameInitializationFixtureBuilder : IEmittedAssemblyBuilder
{
    public ImmutableArray<byte> Build()
    {
        var assembly = new PersistedAssemblyBuilder(
            new AssemblyName("ValueFrameInitializationFixture"),
            typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("ValueFrameInitializationFixture.dll");
        var payloadBuilder = module.DefineType(
            "ValueFrameInitializationFixture.Payload",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.SequentialLayout,
            typeof(ValueType));
        var number = payloadBuilder.DefineField("Number", typeof(long), FieldAttributes.Public);
        var reference = payloadBuilder.DefineField("Reference", typeof(object), FieldAttributes.Public);
        var payload = payloadBuilder.CreateType();

        var entryPoint = module.DefineType(
            "ValueFrameInitializationFixture.EntryPoint",
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        var seed = DefineSeed(entryPoint, payload, number, reference);
        var observe = DefineObserve(entryPoint, payload, number, reference);
        var run = entryPoint.DefineMethod(
            "Run",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(int),
            [typeof(int)]);
        var runCode = run.GetILGenerator();
        runCode.Emit(OpCodes.Call, seed);
        runCode.Emit(OpCodes.Call, observe);
        runCode.Emit(OpCodes.Ret);
        entryPoint.CreateType();

        using var stream = new MemoryStream();
        assembly.Save(stream);
        return [.. stream.ToArray()];
    }

    private static MethodBuilder DefineSeed(
        TypeBuilder entryPoint,
        Type payload,
        FieldInfo number,
        FieldInfo reference)
    {
        var method = entryPoint.DefineMethod(
            "Seed",
            MethodAttributes.Private | MethodAttributes.Static,
            typeof(void),
            Type.EmptyTypes);
        var code = method.GetILGenerator();
        var local = code.DeclareLocal(payload);
        code.Emit(OpCodes.Ldloca, local);
        code.Emit(OpCodes.Ldc_I8, 0x123456789abcdefL);
        code.Emit(OpCodes.Stfld, number);
        code.Emit(OpCodes.Ldloca, local);
        code.Emit(OpCodes.Ldstr, "dirty");
        code.Emit(OpCodes.Stfld, reference);
        code.Emit(OpCodes.Ret);
        return method;
    }

    private static MethodBuilder DefineObserve(
        TypeBuilder entryPoint,
        Type payload,
        FieldInfo number,
        FieldInfo reference)
    {
        var method = entryPoint.DefineMethod(
            "Observe",
            MethodAttributes.Private | MethodAttributes.Static,
            typeof(int),
            Type.EmptyTypes);
        var code = method.GetILGenerator();
        var local = code.DeclareLocal(payload);
        var failed = code.DefineLabel();
        code.Emit(OpCodes.Ldloca, local);
        code.Emit(OpCodes.Ldfld, number);
        code.Emit(OpCodes.Ldc_I8, 0L);
        code.Emit(OpCodes.Bne_Un, failed);
        code.Emit(OpCodes.Ldloca, local);
        code.Emit(OpCodes.Ldfld, reference);
        code.Emit(OpCodes.Brtrue, failed);
        code.Emit(OpCodes.Ldc_I4, 42);
        code.Emit(OpCodes.Ret);
        code.MarkLabel(failed);
        code.Emit(OpCodes.Ldc_I4_M1);
        code.Emit(OpCodes.Ret);
        return method;
    }
}
