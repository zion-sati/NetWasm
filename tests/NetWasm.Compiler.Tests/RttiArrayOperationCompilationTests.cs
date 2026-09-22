using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class RttiArrayOperationCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void CompilerExecutesDistinctSzAndRankOneArraySemantics(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = CreateRankOneArrayFixture(assets.Directory);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "RttiRankOneArray.EntryPoint",
            "Run",
            [],
            Target: target,
            ReferenceAssemblyAliases: ImmutableDictionary<string, string>.Empty.Add(
                "System.Private.CoreLib",
                "NetWasm.CoreLib")));

        CompilerTestSupport.ValidateWithNode(
            compilation.ApplicationModule,
            assets.Directory);
        for (var input = 0; input < 5; input++)
        {
            Assert.Equal(
                42,
                CompilerTestSupport.ExecuteWithStandardWasiNode(
                    compilation.ApplicationModule,
                    assets.Directory,
                    input,
                    target,
                    compilation.StaticDataEnd));
        }
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerPreservesChecksAfterArrayConversions(
        bool optimized,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = optimized
            ? assets.CompileOptimizedSource("RttiArrayOperations", Source)
            : assets.CompileSource("RttiArrayOperations", Source);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "RttiArrayOperations.EntryPoint",
            "Run",
            [],
            Target: target));

        CompilerTestSupport.ValidateWithNode(
            compilation.ApplicationModule,
            assets.Directory);
        for (var input = 0; input < 15; input++)
        {
            Assert.Equal(
                42,
                CompilerTestSupport.ExecuteWithStandardWasiNode(
                    compilation.ApplicationModule,
                    assets.Directory,
                    input,
                    target,
                    compilation.StaticDataEnd));
        }
    }

    private const string Source = """
        using System;
        using System.Collections.Generic;

        namespace RttiArrayOperations;

        public sealed class Item { }
        public interface ILeft { }
        public interface IRight { }
        public sealed class Both : ILeft, IRight { }
        public sealed class LeftOnly : ILeft { }

        public static class EntryPoint
        {
            public static int Run(int input) => input switch
            {
                0 => CopyReducedValueArray(),
                1 => CopyReferenceDowncast(),
                2 => RejectUnrelatedZeroLengthCopy(),
                3 => CopyThroughCovariantInterface(),
                4 => StoreThroughCovariantInterface(),
                5 => RejectStoreThroughCovariantInterface(),
                6 => GenericStoreIntoCovariantArray(),
                7 => RejectMutableCovariantAddress(),
                8 => CopyOverlappingRanges(),
                9 => CopyBetweenInterfaceArrays(),
                10 => PartiallyCopyBetweenInterfaceArrays(),
                11 => CopyZeroElementsBetweenInterfaceArrays(),
                12 => GenericCopyUsesCopyExceptionContract(),
                13 => RejectRankMismatchBeforeRangeValidation(),
                14 => RejectNullBeforeRankAndRangeValidation(),
                _ => -1,
            };

            private static int CopyReducedValueArray()
            {
                var source = new[] { -1 };
                var destination = new uint[1];
                Array.Copy(source, destination, 1);
                return destination[0] == uint.MaxValue ? 42 : -2;
            }

            private static int CopyReferenceDowncast()
            {
                var source = new object[] { "ok", new object() };
                var destination = new string[2];
                try
                {
                    Array.Copy(source, destination, source.Length);
                    return -3;
                }
                catch (InvalidCastException)
                {
                    return destination[0] == "ok" && destination[1] is null ? 42 : -4;
                }
            }

            private static int RejectUnrelatedZeroLengthCopy()
            {
                try
                {
                    Array.Copy(Array.Empty<string>(), Array.Empty<Item>(), 0);
                    return -5;
                }
                catch (ArrayTypeMismatchException)
                {
                    return 42;
                }
            }

            private static int CopyThroughCovariantInterface()
            {
                ICollection<object> source = new string[] { "ok" };
                var destination = new object[1];
                source.CopyTo(destination, 0);
                return (string)destination[0] == "ok" ? 42 : -6;
            }

            private static int StoreThroughCovariantInterface()
            {
                IList<object> values = new string[1];
                values[0] = "ok";
                return (string)values[0] == "ok" ? 42 : -7;
            }

            private static int RejectStoreThroughCovariantInterface()
            {
                IList<object> values = new string[1];
                try
                {
                    values[0] = new object();
                    return -8;
                }
                catch (ArrayTypeMismatchException)
                {
                    return 42;
                }
            }

            private static int GenericStoreIntoCovariantArray()
            {
                object[] values = new string[1];
                Set(values, "ok");
                try
                {
                    Set(values, new object());
                    return -9;
                }
                catch (ArrayTypeMismatchException)
                {
                    return (string)values[0] == "ok" ? 42 : -10;
                }
            }

            private static void Set<T>(T[] values, T value) => values[0] = value;

            private static int RejectMutableCovariantAddress()
            {
                object[] values = new string[1];
                try
                {
                    ref var value = ref Address(values);
                    value = "ok";
                    return -11;
                }
                catch (ArrayTypeMismatchException)
                {
                    return 42;
                }
            }

            private static ref object Address(object[] values) => ref values[0];

            private static int CopyOverlappingRanges()
            {
                Array references = new object[] { "a", "b", "c" };
                Array.Copy(references, 0, references, 1, 2);
                var values = (object[])references;
                if ((string)values[0] != "a" || (string)values[1] != "a" ||
                    (string)values[2] != "b") return -12;
                Array.Copy(references, 1, references, 0, 2);
                if ((string)values[0] != "a" || (string)values[1] != "b") return -13;
                Array.Copy(references, 0, references, 0, 3);

                Array numbers = new[] { 1, 2, 3 };
                Array.Copy(numbers, 0, numbers, 1, 2);
                var copied = (int[])numbers;
                return copied[0] == 1 && copied[1] == 1 && copied[2] == 2 ? 42 : -14;
            }

            private static int CopyBetweenInterfaceArrays()
            {
                ILeft[] source = [new Both()];
                IRight[] destination = new IRight[1];
                Array.Copy((Array)source, 0, destination, 0, 1);
                return ReferenceEquals(source[0], destination[0]) ? 42 : -15;
            }

            private static int PartiallyCopyBetweenInterfaceArrays()
            {
                ILeft[] source = [new Both(), new LeftOnly()];
                IRight[] destination = new IRight[2];
                try
                {
                    Array.Copy((Array)source, 0, destination, 0, 2);
                    return -16;
                }
                catch (InvalidCastException)
                {
                    return ReferenceEquals(source[0], destination[0]) &&
                        destination[1] is null ? 42 : -17;
                }
            }

            private static int CopyZeroElementsBetweenInterfaceArrays()
            {
                Array.Copy((Array)Array.Empty<ILeft>(), 0, Array.Empty<IRight>(), 0, 0);
                return 42;
            }

            private static int GenericCopyUsesCopyExceptionContract()
            {
                object[] source = ["ok", new object()];
                string[] destination = new string[2];
                try
                {
                    Array.Copy(source, 0, destination, 0, 2);
                    return -18;
                }
                catch (InvalidCastException)
                {
                    return destination[0] == "ok" && destination[1] is null ? 42 : -19;
                }
            }

            private static int RejectRankMismatchBeforeRangeValidation()
            {
                try
                {
                    Array.Copy(new int[1], -1, new int[1, 1], -1, -1);
                    return -20;
                }
                catch (RankException)
                {
                    return 42;
                }
            }

            private static int RejectNullBeforeRankAndRangeValidation()
            {
                try
                {
                    Array.Copy(null!, -1, new int[1, 1], -1, -1);
                    return -21;
                }
                catch (ArgumentNullException)
                {
                    return 42;
                }
            }
        }
        """;

    private static string CreateRankOneArrayFixture(string directory)
    {
        var assembly = new PersistedAssemblyBuilder(
            new AssemblyName("RttiRankOneArray"),
            typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("RttiRankOneArray.dll");
        var entryPoint = module.DefineType(
            "RttiRankOneArray.EntryPoint",
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        var cases = new[]
        {
            DefineRankOneGetSet(module, entryPoint),
            DefineArrayShapeCasts(module, entryPoint),
            DefineExactMutableAddress(module, entryPoint),
            DefineRejectedMutableAddress(module, entryPoint),
            DefineReadonlyCovariantAddress(module, entryPoint),
        };
        DefineDispatcher(entryPoint, cases);
        entryPoint.CreateType();

        var path = Path.Combine(directory, "RttiRankOneArray.dll");
        assembly.Save(path);
        RewriteRankTwoArraySignaturesAsRankOne(path);
        return path;
    }

    private static MethodBuilder DefineRankOneGetSet(ModuleBuilder module, TypeBuilder type)
    {
        var method = DefineCase(type, "RankOneGetSet");
        var code = method.GetILGenerator();
        var arrayType = typeof(int).MakeArrayType(2);
        var array = code.DeclareLocal(arrayType);

        code.Emit(OpCodes.Ldc_I4_1);
        code.Emit(OpCodes.Newobj, DefineArrayMethod(
            module,
            arrayType,
            ".ctor",
            typeof(void),
            typeof(int)));
        code.Emit(OpCodes.Stloc, array);
        code.Emit(OpCodes.Ldloc, array);
        code.Emit(OpCodes.Ldc_I4_0);
        code.Emit(OpCodes.Ldc_I4, 42);
        code.Emit(OpCodes.Call, DefineArrayMethod(
            module,
            arrayType,
            "Set",
            typeof(void),
            typeof(int),
            typeof(int)));
        code.Emit(OpCodes.Ldloc, array);
        code.Emit(OpCodes.Ldc_I4_0);
        code.Emit(OpCodes.Call, DefineArrayMethod(
            module,
            arrayType,
            "Get",
            typeof(int),
            typeof(int)));
        code.Emit(OpCodes.Ret);
        return method;
    }

    private static MethodBuilder DefineArrayShapeCasts(ModuleBuilder module, TypeBuilder type)
    {
        var method = DefineCase(type, "ArrayShapeCasts");
        var code = method.GetILGenerator();
        var rankOneType = typeof(int).MakeArrayType(2);
        var szType = typeof(int[]);
        var rankOne = code.DeclareLocal(rankOneType);
        var sz = code.DeclareLocal(szType);
        var failed = code.DefineLabel();

        code.Emit(OpCodes.Ldc_I4_1);
        code.Emit(OpCodes.Newobj, DefineArrayMethod(
            module,
            rankOneType,
            ".ctor",
            typeof(void),
            typeof(int)));
        code.Emit(OpCodes.Stloc, rankOne);
        code.Emit(OpCodes.Ldloc, rankOne);
        code.Emit(OpCodes.Isinst, rankOneType);
        code.Emit(OpCodes.Brfalse, failed);
        code.Emit(OpCodes.Ldloc, rankOne);
        code.Emit(OpCodes.Isinst, szType);
        code.Emit(OpCodes.Brtrue, failed);

        code.Emit(OpCodes.Ldc_I4_1);
        code.Emit(OpCodes.Newarr, typeof(int));
        code.Emit(OpCodes.Stloc, sz);
        code.Emit(OpCodes.Ldloc, sz);
        code.Emit(OpCodes.Isinst, szType);
        code.Emit(OpCodes.Brfalse, failed);
        code.Emit(OpCodes.Ldloc, sz);
        code.Emit(OpCodes.Isinst, rankOneType);
        code.Emit(OpCodes.Brfalse, failed);
        code.Emit(OpCodes.Ldc_I4, 42);
        code.Emit(OpCodes.Ret);

        code.MarkLabel(failed);
        code.Emit(OpCodes.Ldc_I4_M1);
        code.Emit(OpCodes.Ret);
        return method;
    }

    private static MethodBuilder DefineExactMutableAddress(ModuleBuilder module, TypeBuilder type)
    {
        var method = DefineCase(type, "ExactMutableAddress");
        var code = method.GetILGenerator();
        var arrayType = typeof(string).MakeArrayType(2);
        var array = code.DeclareLocal(arrayType);
        var succeeded = code.DefineLabel();

        code.Emit(OpCodes.Ldc_I4_1);
        code.Emit(OpCodes.Newobj, DefineArrayMethod(
            module,
            arrayType,
            ".ctor",
            typeof(void),
            typeof(int)));
        code.Emit(OpCodes.Stloc, array);
        code.Emit(OpCodes.Ldloc, array);
        code.Emit(OpCodes.Ldc_I4_0);
        code.Emit(OpCodes.Call, DefineArrayMethod(
            module,
            arrayType,
            "Address",
            typeof(string).MakeByRefType(),
            typeof(int)));
        code.Emit(OpCodes.Ldnull);
        code.Emit(OpCodes.Stind_Ref);
        code.Emit(OpCodes.Ldloc, array);
        code.Emit(OpCodes.Ldc_I4_0);
        code.Emit(OpCodes.Call, DefineArrayMethod(
            module,
            arrayType,
            "Get",
            typeof(string),
            typeof(int)));
        code.Emit(OpCodes.Brfalse, succeeded);
        code.Emit(OpCodes.Ldc_I4_M1);
        code.Emit(OpCodes.Ret);
        code.MarkLabel(succeeded);
        code.Emit(OpCodes.Ldc_I4, 42);
        code.Emit(OpCodes.Ret);
        return method;
    }

    private static MethodBuilder DefineRejectedMutableAddress(ModuleBuilder module, TypeBuilder type)
    {
        var method = DefineCase(type, "RejectedMutableAddress");
        var code = method.GetILGenerator();
        var sourceType = typeof(string).MakeArrayType(2);
        var viewType = typeof(object).MakeArrayType(2);
        var view = code.DeclareLocal(viewType);
        var result = code.DeclareLocal(typeof(int));
        var complete = code.DefineLabel();

        code.BeginExceptionBlock();
        code.Emit(OpCodes.Ldc_I4_1);
        code.Emit(OpCodes.Newobj, DefineArrayMethod(
            module,
            sourceType,
            ".ctor",
            typeof(void),
            typeof(int)));
        code.Emit(OpCodes.Castclass, viewType);
        code.Emit(OpCodes.Stloc, view);
        code.Emit(OpCodes.Ldloc, view);
        code.Emit(OpCodes.Ldc_I4_0);
        code.Emit(OpCodes.Call, DefineArrayMethod(
            module,
            viewType,
            "Address",
            typeof(object).MakeByRefType(),
            typeof(int)));
        code.Emit(OpCodes.Pop);
        code.Emit(OpCodes.Ldc_I4_M1);
        code.Emit(OpCodes.Stloc, result);
        code.Emit(OpCodes.Leave, complete);
        code.BeginCatchBlock(typeof(ArrayTypeMismatchException));
        code.Emit(OpCodes.Pop);
        code.Emit(OpCodes.Ldc_I4, 42);
        code.Emit(OpCodes.Stloc, result);
        code.Emit(OpCodes.Leave, complete);
        code.EndExceptionBlock();
        code.MarkLabel(complete);
        code.Emit(OpCodes.Ldloc, result);
        code.Emit(OpCodes.Ret);
        return method;
    }

    private static MethodBuilder DefineReadonlyCovariantAddress(ModuleBuilder module, TypeBuilder type)
    {
        var method = DefineCase(type, "ReadonlyCovariantAddress");
        var code = method.GetILGenerator();
        var sourceType = typeof(string).MakeArrayType(2);
        var viewType = typeof(object).MakeArrayType(2);
        var view = code.DeclareLocal(viewType);
        var succeeded = code.DefineLabel();

        code.Emit(OpCodes.Ldc_I4_1);
        code.Emit(OpCodes.Newobj, DefineArrayMethod(
            module,
            sourceType,
            ".ctor",
            typeof(void),
            typeof(int)));
        code.Emit(OpCodes.Castclass, viewType);
        code.Emit(OpCodes.Stloc, view);
        code.Emit(OpCodes.Ldloc, view);
        code.Emit(OpCodes.Ldc_I4_0);
        code.Emit(OpCodes.Readonly);
        code.Emit(OpCodes.Call, DefineArrayMethod(
            module,
            viewType,
            "Address",
            typeof(object).MakeByRefType(),
            typeof(int)));
        code.Emit(OpCodes.Ldind_Ref);
        code.Emit(OpCodes.Brfalse, succeeded);
        code.Emit(OpCodes.Ldc_I4_M1);
        code.Emit(OpCodes.Ret);
        code.MarkLabel(succeeded);
        code.Emit(OpCodes.Ldc_I4, 42);
        code.Emit(OpCodes.Ret);
        return method;
    }

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

    private static MethodBuilder DefineCase(TypeBuilder type, string name) =>
        type.DefineMethod(
            name,
            MethodAttributes.Private | MethodAttributes.Static,
            typeof(int),
            Type.EmptyTypes);

    private static MethodInfo DefineArrayMethod(
        ModuleBuilder module,
        Type arrayType,
        string name,
        Type returnType,
        params Type[] parameterTypes) => module.GetArrayMethod(
        arrayType,
        name,
        CallingConventions.HasThis,
        returnType,
        parameterTypes);

    private static void RewriteRankTwoArraySignaturesAsRankOne(string path)
    {
        var image = File.ReadAllBytes(path);
        RewriteArraySignature(image, 0x08);
        RewriteArraySignature(image, 0x0e);
        RewriteArraySignature(image, 0x1c);
        File.WriteAllBytes(path, image);
    }

    private static void RewriteArraySignature(byte[] image, byte elementType)
    {
        ReadOnlySpan<byte> rankTwo = [0x14, elementType, 0x02, 0x00, 0x02, 0x00, 0x00];
        ReadOnlySpan<byte> rankOne = [0x14, elementType, 0x01, 0x01, 0x01, 0x01, 0x00];
        var rewritten = 0;
        for (var offset = 0; offset <= image.Length - rankTwo.Length; offset++)
        {
            if (!image.AsSpan(offset, rankTwo.Length).SequenceEqual(rankTwo))
            {
                continue;
            }

            rankOne.CopyTo(image.AsSpan(offset, rankOne.Length));
            rewritten++;
        }

        if (rewritten == 0)
        {
            throw new InvalidOperationException(
                $"The persisted fixture has no rank-two signature for element type 0x{elementType:x2}.");
        }
    }
}
