using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using NetWasm.Compiler.Tests.Correctness;

namespace NetWasm.Compiler.Tests;

public sealed class BoundedArrayCompilationTests(CorrectnessTestRunner runner) : EmittedAssemblyTestBase(runner)
{
    [Theory]
    [InlineData("Emitted-Wasm32-Direct-Linked")]
    [InlineData("Emitted-Wasm64-Direct-Linked")]
    [InlineData("Emitted-Wasm32-Optimized-Linked")]
    [InlineData("Emitted-Wasm64-Optimized-Linked")]
    public void BoundedArraysPreserveShapeAddressesReferencesAndFailures(string cell)
    {
        var manifest = CorpusCaseTestData.Get(CorpusCaseTestData.NonZeroBound) with
        {
            CaseId = "rtti.bounded-array-contract",
            Name = "BoundedArrayContract",
            Namespace = "BoundedArrayContract",
            // D03 retains ARRAY identity in NetWasm where CoreCLR normalizes
            // zero-bound rank-one construction to SZARRAY.
            DesktopEntryMethod = "RunDesktop",
            Inputs = [.. Enumerable.Range(0, 28)],
            Expectations = [.. Enumerable.Range(0, 28).Select(input => input switch
            {
                5 => new CorpusCaseExpectation(input, null, "System.OverflowException"),
                6 or 24 or 25 or 27 => new CorpusCaseExpectation(input, null, "System.ArgumentOutOfRangeException"),
                26 => new CorpusCaseExpectation(input, null, "System.ArgumentException"),
                7 => new CorpusCaseExpectation(input, null, "System.OutOfMemoryException"),
                8 or 9 or 10 or 13 or 18 or 19 or 21 => new CorpusCaseExpectation(input, null, "System.IndexOutOfRangeException"),
                20 => new CorpusCaseExpectation(input, null, "System.ArrayTypeMismatchException"),
                12 => new CorpusCaseExpectation(input, null, "System.NullReferenceException"),
                _ => new CorpusCaseExpectation(input, 42, null),
            })],
            ExecutionBackend = CorpusExecutionBackend.Linked,
            RequiredRuntimeCapabilities = OracleRuntimeCapabilities.GarbageCollection,
            MatrixProfile = CorpusMatrixProfile.Extended,
            ExposesLegacyTrace = false,
            TestMethod = typeof(BoundedArrayCompilationTests).FullName + "." +
                nameof(BoundedArraysPreserveShapeAddressesReferencesAndFailures),
        };
        Run(manifest, cell, new FixtureBuilder());
    }

    private sealed class FixtureBuilder : IEmittedAssemblyBuilder
    {
        public ImmutableArray<byte> Build()
        {
            var assembly = new PersistedAssemblyBuilder(new AssemblyName("BoundedArrayContract"),
                typeof(object).Assembly);
            var module = assembly.DefineDynamicModule("BoundedArrayContract.dll");
            var payloadBuilder = module.DefineType("BoundedArrayContract.Payload",
                TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.SequentialLayout,
                typeof(ValueType));
            payloadBuilder.DefineField("Value", typeof(int), FieldAttributes.Public);
            payloadBuilder.DefineField("Reference", typeof(string), FieldAttributes.Public);
            var payload = payloadBuilder.CreateType()!;
            var type = module.DefineType("BoundedArrayContract.EntryPoint",
                TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
            var methods = Enumerable.Range(0, 28).Select(input => DefineCase(module, type, payload, input)).ToArray();
            DefineDispatcher(type, "Run", methods);
            methods[14] = DefineCase(module, type, payload, 14, desktop: true);
            methods[15] = DefineCase(module, type, payload, 15, desktop: true);
            DefineDispatcher(type, "RunDesktop", methods);
            type.CreateType();
            using var stream = new MemoryStream();
            assembly.Save(stream);
            var image = stream.ToArray();
            // PersistedAssemblyBuilder normalizes MakeArrayType(1); retain an
            // explicit ARRAY token by rewriting the equal-sized rank-two shape.
            foreach (var element in new byte[] { 0x0e, 0x1c })
            {
                ReadOnlySpan<byte> original = [0x14, element, 0x02, 0x00, 0x02, 0x00, 0x00];
                ReadOnlySpan<byte> replacement = [0x14, element, 0x01, 0x01, 0x01, 0x01, 0x00];
                var rewritten = 0;
                for (var offset = 0; offset <= image.Length - original.Length; offset++)
                {
                    if (!image.AsSpan(offset, original.Length).SequenceEqual(original)) continue;
                    replacement.CopyTo(image.AsSpan(offset, replacement.Length));
                    rewritten++;
                }
                Assert.True(rewritten > 0);
            }
            return [.. image];
        }

        private static void DefineDispatcher(TypeBuilder type, string name, MethodBuilder[] methods)
        {
            var run = type.DefineMethod(name, MethodAttributes.Public | MethodAttributes.Static,
                typeof(int), [typeof(int)]);
            var code = run.GetILGenerator();
            var labels = methods.Select(_ => code.DefineLabel()).ToArray();
            code.Emit(OpCodes.Ldarg_0);
            code.Emit(OpCodes.Switch, labels);
            code.Emit(OpCodes.Ldc_I4_M1);
            code.Emit(OpCodes.Ret);
            for (var index = 0; index < methods.Length; index++)
            {
                code.MarkLabel(labels[index]);
                code.Emit(OpCodes.Call, methods[index]);
                code.Emit(OpCodes.Ret);
            }
        }

        private static MethodBuilder DefineCase(ModuleBuilder module, TypeBuilder type, Type payload, int input, bool desktop = false)
        {
            var method = type.DefineMethod("Case" + input + (desktop ? "Desktop" : string.Empty),
                MethodAttributes.Public | MethodAttributes.Static, typeof(int), []);
            var code = method.GetILGenerator();
            if (input == 11)
            {
                DefineRankTwo(module, code, payload);
                return method;
            }
            var vector = input == 16;
            var arrayType = vector ? typeof(string[]) : typeof(string).MakeArrayType(2);
            var array = code.DeclareLocal(arrayType);
            var lower = input switch { 1 or 17 or 18 or 19 or >= 22 => -2, 2 or 4 => int.MinValue, 3 or 6 => int.MaxValue, 14 or 15 or 16 => 0, _ => 1 };
            var length = input switch { 4 => 0, 5 => -1, 23 => 3, 6 or 17 or >= 22 => 2, 7 => int.MaxValue, _ => 1 };
            if (input == 12) code.Emit(OpCodes.Ldnull);
            else
            {
                if (input != 15 && !vector) Constant(code, lower);
                Constant(code, length);
                if (vector) code.Emit(OpCodes.Newarr, typeof(string));
                else code.Emit(OpCodes.Newobj, ArrayMethod(module, arrayType, ".ctor", typeof(void), input == 15 ? 1 : 2));
            }
            code.Emit(OpCodes.Stloc, array);
            if (input is >= 17 and <= 19 or >= 22)
            {
                DefineArrayApis(module, code, arrayType, array, input);
                return method;
            }
            if (input is 5 or 6 or 7)
            {
                Constant(code, -1);
                code.Emit(OpCodes.Ret);
                return method;
            }
            var failed = code.DefineLabel();
            if (input == 20)
            {
                code.Emit(OpCodes.Ldloc, array);
                Constant(code, lower);
                code.Emit(OpCodes.Call, ArrayMethod(module, typeof(object).MakeArrayType(2),
                    "Address", typeof(object).MakeByRefType(), 1));
                code.Emit(OpCodes.Pop);
            }
            if (input is 13 or 21)
            {
                Dimension(code, array, "GetLowerBound", input == 13 ? -1 : 1);
                code.Emit(OpCodes.Pop);
            }
            if (input != 12)
            {
                Dimension(code, array, "GetLowerBound", 0);
                Constant(code, lower);
                code.Emit(OpCodes.Bne_Un, failed);
                Dimension(code, array, "GetUpperBound", 0);
                Constant(code, unchecked(lower + length - 1));
                code.Emit(OpCodes.Bne_Un, failed);
                code.Emit(OpCodes.Ldloc, array);
                code.Emit(OpCodes.Isinst, typeof(string[]));
                code.Emit(desktop || vector ? OpCodes.Brfalse : OpCodes.Brtrue, failed);
            }
            if (input != 4)
            {
                code.Emit(OpCodes.Ldloc, array);
                Constant(code, input is 8 or 10 ? lower - 1 : input == 9 ? lower + length : lower);
                if (input == 9)
                {
                    code.Emit(OpCodes.Ldstr, "out-of-range");
                    code.Emit(OpCodes.Call, module.GetArrayMethod(arrayType, "Set", CallingConventions.HasThis,
                        typeof(void), [typeof(int), typeof(string)]));
                }
                else if (input is 8 or 12)
                {
                    code.Emit(OpCodes.Call, ArrayMethod(module, arrayType, "Get", typeof(string), 1));
                    code.Emit(OpCodes.Pop);
                }
                else
                {
                    if (desktop || vector) code.Emit(OpCodes.Ldelema, typeof(string));
                    else code.Emit(OpCodes.Call, ArrayMethod(module, arrayType, "Address", typeof(string).MakeByRefType(), 1));
                    var address = code.DeclareLocal(typeof(string).MakeByRefType());
                    code.Emit(OpCodes.Stloc, address);
                    if (input is not (0 or 3)) code.Emit(OpCodes.Call, typeof(GC).GetMethod("Collect", Type.EmptyTypes)!);
                    code.Emit(OpCodes.Ldloc, address);
                    if (input == 1) code.Emit(OpCodes.Ldstr, "xxx");
                    else
                    {
                        Constant(code, 'x');
                        Constant(code, 3);
                        code.Emit(OpCodes.Newobj, typeof(string).GetConstructor([typeof(char), typeof(int)])!);
                    }
                    code.Emit(OpCodes.Stind_Ref);
                    if (input is not (0 or 2)) code.Emit(OpCodes.Call, typeof(GC).GetMethod("Collect", Type.EmptyTypes)!);
                    code.Emit(OpCodes.Ldloc, array);
                    Constant(code, lower);
                    if (desktop || vector) code.Emit(OpCodes.Ldelem_Ref);
                    else code.Emit(OpCodes.Call, ArrayMethod(module, arrayType, "Get", typeof(string), 1));
                    code.Emit(OpCodes.Ldstr, "xxx");
                    code.Emit(OpCodes.Call, typeof(string).GetMethod("op_Equality", [typeof(string), typeof(string)])!);
                    code.Emit(OpCodes.Brfalse, failed);
                }
            }
            Constant(code, 42);
            code.Emit(OpCodes.Ret);
            code.MarkLabel(failed);
            Constant(code, -1);
            code.Emit(OpCodes.Ret);
            return method;
        }

        private static void DefineArrayApis(ModuleBuilder module, ILGenerator code,
            Type arrayType, LocalBuilder source, int input)
        {
            var clear = typeof(Array).GetMethod("Clear", [typeof(Array), typeof(int), typeof(int)])!;
            if (input is 18 or 19)
            {
                code.Emit(OpCodes.Ldloc, source);
                Constant(code, input == 18 ? -3 : -2);
                Constant(code, input == 18 ? 0 : -1);
                code.Emit(OpCodes.Call, clear);
                Constant(code, -1);
                code.Emit(OpCodes.Ret);
                return;
            }
            var destination = code.DeclareLocal(arrayType);
            var failed = code.DefineLabel();
            code.Emit(OpCodes.Ldloc, source);
            Constant(code, -1);
            code.Emit(OpCodes.Ldstr, "copied");
            code.Emit(OpCodes.Call, module.GetArrayMethod(arrayType, "Set", CallingConventions.HasThis,
                typeof(void), [typeof(int), typeof(string)]));
            Constant(code, 5);
            Constant(code, 2);
            code.Emit(OpCodes.Newobj, ArrayMethod(module, arrayType, ".ctor", typeof(void), 2));
            code.Emit(OpCodes.Stloc, destination);
            if (input >= 22)
            {
                // Indexed copying alone translates public ARRAY indices;
                // include an overlapping self-copy and both range boundaries.
                code.Emit(OpCodes.Ldloc, source);
                Constant(code, input switch { 23 => -2, 24 => -3, _ => -1 });
                code.Emit(OpCodes.Ldloc, input == 23 ? source : destination);
                Constant(code, input switch { 23 => -1, 25 => 4, _ => 5 });
                Constant(code, input switch { 23 or 26 => 2, 27 => -1, _ => 1 });
                code.Emit(OpCodes.Call, typeof(Array).GetMethod("Copy",
                    [typeof(Array), typeof(int), typeof(Array), typeof(int), typeof(int)])!);
                code.Emit(OpCodes.Ldloc, input == 23 ? source : destination);
                Constant(code, input == 23 ? 0 : 5);
                code.Emit(OpCodes.Call, ArrayMethod(module, arrayType, "Get", typeof(string), 1));
                code.Emit(OpCodes.Ldstr, "copied");
                code.Emit(OpCodes.Call, typeof(string).GetMethod("op_Equality", [typeof(string), typeof(string)])!);
                code.Emit(OpCodes.Brfalse, failed);
                Constant(code, 42);
                code.Emit(OpCodes.Ret);
                code.MarkLabel(failed);
                Constant(code, -1);
                code.Emit(OpCodes.Ret);
                return;
            }
            code.Emit(OpCodes.Ldloc, source);
            code.Emit(OpCodes.Ldloc, destination);
            Constant(code, 2);
            code.Emit(OpCodes.Call, typeof(Array).GetMethod("Copy", [typeof(Array), typeof(Array), typeof(int)])!);
            code.Emit(OpCodes.Ldloc, destination);
            Constant(code, 6);
            code.Emit(OpCodes.Call, ArrayMethod(module, arrayType, "Get", typeof(string), 1));
            code.Emit(OpCodes.Ldstr, "copied");
            code.Emit(OpCodes.Call, typeof(string).GetMethod("op_Equality", [typeof(string), typeof(string)])!);
            code.Emit(OpCodes.Brfalse, failed);
            code.Emit(OpCodes.Ldloc, destination);
            Constant(code, 6);
            Constant(code, 1);
            code.Emit(OpCodes.Call, clear);
            code.Emit(OpCodes.Ldloc, destination);
            Constant(code, 6);
            code.Emit(OpCodes.Call, ArrayMethod(module, arrayType, "Get", typeof(string), 1));
            code.Emit(OpCodes.Brtrue, failed);
            Constant(code, 42);
            code.Emit(OpCodes.Ret);
            code.MarkLabel(failed);
            Constant(code, -1);
            code.Emit(OpCodes.Ret);
        }

        private static void DefineRankTwo(ModuleBuilder module, ILGenerator code, Type payload)
        {
            var arrayType = payload.MakeArrayType(2);
            var array = code.DeclareLocal(arrayType);
            var clone = code.DeclareLocal(arrayType);
            var address = code.DeclareLocal(payload.MakeByRefType());
            var value = code.DeclareLocal(payload);
            var failed = code.DefineLabel();
            foreach (var dimension in new[] { -2, 2, 5, 3 }) Constant(code, dimension);
            code.Emit(OpCodes.Newobj, ArrayMethod(module, arrayType, ".ctor", typeof(void), 4));
            code.Emit(OpCodes.Stloc, array);
            code.Emit(OpCodes.Ldloc, array);
            Constant(code, -1);
            Constant(code, 7);
            code.Emit(OpCodes.Call, ArrayMethod(module, arrayType, "Address", payload.MakeByRefType(), 2));
            code.Emit(OpCodes.Stloc, address);
            code.Emit(OpCodes.Call, typeof(GC).GetMethod("Collect", Type.EmptyTypes)!);
            code.Emit(OpCodes.Ldloc, address);
            Constant(code, 73);
            code.Emit(OpCodes.Stfld, payload.GetField("Value")!);
            code.Emit(OpCodes.Ldloc, address);
            Constant(code, 'y');
            Constant(code, 3);
            code.Emit(OpCodes.Newobj, typeof(string).GetConstructor([typeof(char), typeof(int)])!);
            code.Emit(OpCodes.Stfld, payload.GetField("Reference")!);
            code.Emit(OpCodes.Ldloc, array);
            code.Emit(OpCodes.Callvirt, typeof(Array).GetMethod("Clone")!);
            code.Emit(OpCodes.Castclass, arrayType);
            code.Emit(OpCodes.Stloc, clone);
            code.Emit(OpCodes.Call, typeof(GC).GetMethod("Collect", Type.EmptyTypes)!);
            Dimension(code, clone, "GetLowerBound", 0);
            Constant(code, -2);
            code.Emit(OpCodes.Bne_Un, failed);
            Dimension(code, clone, "GetLowerBound", 1);
            Constant(code, 5);
            code.Emit(OpCodes.Bne_Un, failed);
            code.Emit(OpCodes.Ldloc, clone);
            Constant(code, -1);
            Constant(code, 7);
            code.Emit(OpCodes.Call, ArrayMethod(module, arrayType, "Get", payload, 2));
            code.Emit(OpCodes.Stloc, value);
            code.Emit(OpCodes.Call, typeof(GC).GetMethod("Collect", Type.EmptyTypes)!);
            code.Emit(OpCodes.Ldloca, value);
            code.Emit(OpCodes.Ldfld, payload.GetField("Value")!);
            Constant(code, 73);
            code.Emit(OpCodes.Bne_Un, failed);
            code.Emit(OpCodes.Ldloca, value);
            code.Emit(OpCodes.Ldfld, payload.GetField("Reference")!);
            code.Emit(OpCodes.Ldstr, "yyy");
            code.Emit(OpCodes.Call, typeof(string).GetMethod("op_Equality", [typeof(string), typeof(string)])!);
            code.Emit(OpCodes.Brfalse, failed);
            Constant(code, 42);
            code.Emit(OpCodes.Ret);
            code.MarkLabel(failed);
            Constant(code, -1);
            code.Emit(OpCodes.Ret);
        }

        private static void Dimension(ILGenerator code, LocalBuilder array, string name, int dimension)
        {
            code.Emit(OpCodes.Ldloc, array);
            Constant(code, dimension);
            code.Emit(OpCodes.Callvirt, typeof(Array).GetMethod(name, [typeof(int)])!);
        }

        private static MethodInfo ArrayMethod(ModuleBuilder module, Type array, string name, Type result, int count) =>
            module.GetArrayMethod(array, name, CallingConventions.HasThis, result,
                [.. Enumerable.Repeat(typeof(int), count)]);

        private static void Constant(ILGenerator code, int value) => code.Emit(OpCodes.Ldc_I4, value);
    }
}
