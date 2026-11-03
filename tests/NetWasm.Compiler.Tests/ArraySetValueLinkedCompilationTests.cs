using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using NetWasm.Compiler.Tests.Correctness;
using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Compiler.Tests;

public sealed class ArraySetValueLinkedCompilationTests(CorrectnessTestRunner runner)
{
    [Theory]
    [InlineData("Emitted-Wasm32-Direct-Linked")]
    [InlineData("Emitted-Wasm64-Direct-Linked")]
    public void NonzeroBoundsUsePublicAssignmentWithNativeRuntime(string cell)
    {
        var fixture = new CorpusFixture("ArrayAssignmentBounds", "ArrayAssignmentBounds", "", [0, 1, 2, 3, 4])
        {
            CaseId = "array.assignment-bounds",
            ExpectedReturnValues = new Dictionary<int, int> { [0] = 42, [3] = 42, [4] = 42 }.ToImmutableDictionary(),
            ExpectedExceptionTypes = new Dictionary<int, string>
            {
                [1] = "System.IndexOutOfRangeException",
                [2] = "System.IndexOutOfRangeException",
            }.ToImmutableDictionary(),
            RequiredRuntimeCapabilities = OracleRuntimeCapabilities.GarbageCollection,
            Matrix = new(CorpusMatrixProfile.Extended, cell, CorpusExecutionBackend.Linked),
            ExposesLegacyTrace = false,
            ReferenceAssemblyAliases = ImmutableDictionary<string, string>.Empty
                .Add("System.Private.CoreLib", "NetWasm.CoreLib"),
            OracleMode = OracleMode.SameIl,
            SameSourceReason = null,
            Optimization = FinalWasmOptimization.None,
        };
        runner.Run(fixture, new Builder());
    }

    private sealed class Builder : IEmittedAssemblyBuilder
    {
        public ImmutableArray<byte> Build()
        {
            var assembly = new PersistedAssemblyBuilder(new AssemblyName("ArrayAssignmentBounds"), typeof(object).Assembly);
            var module = assembly.DefineDynamicModule("ArrayAssignmentBounds.dll");
            var type = module.DefineType("ArrayAssignmentBounds.EntryPoint", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
            var run = type.DefineMethod("Run", MethodAttributes.Public | MethodAttributes.Static, typeof(int), [typeof(int)]);
            var il = run.GetILGenerator();
            var arrayType = typeof(int[,]);
            var array = il.DeclareLocal(arrayType);
            var labels = Enumerable.Range(0, 5).Select(_ => il.DefineLabel()).ToArray();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Switch, labels);
            il.Emit(OpCodes.Ldc_I4_M1); il.Emit(OpCodes.Ret);
            for (var scenario = 0; scenario < labels.Length; scenario++)
            {
                il.MarkLabel(labels[scenario]);
                foreach (var value in new[] { -2, 2, 5, 3 }) il.Emit(OpCodes.Ldc_I4, value);
                il.Emit(OpCodes.Newobj, module.GetArrayMethod(arrayType, ".ctor", CallingConventions.HasThis,
                    typeof(void), [typeof(int), typeof(int), typeof(int), typeof(int)]));
                il.Emit(OpCodes.Stloc, array);
                il.Emit(OpCodes.Ldloc, array);
                il.Emit(OpCodes.Ldc_I4, 42); il.Emit(OpCodes.Box, typeof(int));
                il.Emit(OpCodes.Ldc_I4, scenario == 1 ? -3 : -1);
                if (scenario == 4) il.Emit(OpCodes.Conv_I8);
                il.Emit(OpCodes.Ldc_I4, scenario == 2 ? 8 : 7);
                if (scenario == 4) il.Emit(OpCodes.Conv_I8);
                var indexType = scenario == 4 ? typeof(long) : typeof(int);
                il.Emit(OpCodes.Call, typeof(Array).GetMethod("SetValue", [typeof(object), indexType, indexType])!);
                if (scenario == 3)
                {
                    il.Emit(OpCodes.Ldloc, array); il.Emit(OpCodes.Ldnull);
                    il.Emit(OpCodes.Ldc_I4, -1); il.Emit(OpCodes.Ldc_I4, 7);
                    il.Emit(OpCodes.Call, typeof(Array).GetMethod("SetValue", [typeof(object), typeof(int), typeof(int)])!);
                }
                il.Emit(OpCodes.Call, typeof(GC).GetMethod("Collect", Type.EmptyTypes)!);
                il.Emit(OpCodes.Ldloc, array); il.Emit(OpCodes.Ldc_I4, -1); il.Emit(OpCodes.Ldc_I4, 7);
                il.Emit(OpCodes.Call, module.GetArrayMethod(arrayType, "Get", CallingConventions.HasThis, typeof(int), [typeof(int), typeof(int)]));
                if (scenario == 3) { il.Emit(OpCodes.Ldc_I4, 42); il.Emit(OpCodes.Add); }
                il.Emit(OpCodes.Ret);
            }
            type.CreateType();
            using var stream = new MemoryStream();
            assembly.Save(stream);
            return [.. stream.ToArray()];
        }
    }
}
