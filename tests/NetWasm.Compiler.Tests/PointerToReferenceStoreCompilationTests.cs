using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// IL may store a value of one pointer-sized kind where another is declared: an
// unmanaged pointer into a ref field (upstream Span<T>(void*, int) does this,
// and once inlined its span's fields are separate locals), or a managed address
// through stind.i. On Wasm64 the evaluation stack keeps native ints and managed
// addresses in different i64 locals, so each store must read the value from the
// local of the kind on the stack, not of the field, and a call's argument reaches its callee
// whichever pointer-sized kind the parameter declares.
public sealed class PointerToReferenceStoreCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.Oz)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.Oz)]
    public void PointersStoredIntoReferenceFieldsKeepTheirAddress(
        WasmTarget target,
        FinalWasmOptimization optimization)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileUnsafeSource("PointerToReferenceFields_" + target + optimization, """
            public struct Payload
            {
                public int Value;
            }

            public static unsafe class EntryPoint
            {
                public static int Run(int input)
                {
                    var payload = new Payload { Value = 20 };
                    var span = new System.Span<Payload>(&payload, 1);
                    var readOnly = new System.ReadOnlySpan<Payload>(&payload, 1);
                    span[0].Value += 3;
                    return span[0].Value + readOnly[0].Value - 23 + input;
                }
            }
            """, []);

        Assert.Equal(42, Execute(assets, assembly, target, optimization, 19));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.Oz)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.Oz)]
    public void ManagedAddressesStoredAsNativeIntegersKeepTheirAddress(
        WasmTarget target,
        FinalWasmOptimization optimization)
    {
        using var assets = TestAssets.Create();
        var assembly = Path.Combine(assets.Directory, "ManagedAddressStores_" + target + optimization + ".dll");
        File.WriteAllBytes(assembly, BuildManagedAddressStore());

        Assert.Equal(42, Execute(assets, assembly, target, optimization, 19,
            ImmutableDictionary<string, string>.Empty.Add("System.Private.CoreLib", "NetWasm.CoreLib")));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.Oz)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.Oz)]
    public void PointersPassedAsReferencesReachTheCallee(
        WasmTarget target,
        FinalWasmOptimization optimization)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileUnsafeSource("PointerArguments_" + target + optimization, """
            using System.Runtime.Intrinsics;

            public static unsafe class EntryPoint
            {
                public static int Run(int input)
                {
                    var source = new byte[16];
                    var destination = new byte[16];
                    for (var index = 0; index < source.Length; index++) source[index] = (byte)(index + 1);
                    fixed (byte* from = source)
                    fixed (byte* to = destination)
                    {
                        Vector128.StoreUnsafe(Vector128.LoadUnsafe(ref *from) + Vector128.Create((byte)1), ref *to);
                    }
                    var sum = 0;
                    foreach (var value in destination) sum += value;
                    return sum - 110 + input;
                }
            }
            """, []);

        Assert.Equal(42, Execute(assets, assembly, target, optimization, 0));
    }

    // A byref local that `ref *pointer` initializes addresses memory the collector does not manage,
    // so it needs no root anchor when an allocation in its lifetime can collect.
    [Theory]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.Oz)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.Oz)]
    public void PointersHeldInReferenceLocalsSurviveCollections(
        WasmTarget target,
        FinalWasmOptimization optimization)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileUnsafeSource("PointerReferenceLocals_" + target + optimization, """
            using System;
            using System.Runtime.CompilerServices;

            public static unsafe class EntryPoint
            {
                public static int Run(int input)
                {
                    var buffer = stackalloc byte[16];
                    ref byte first = ref *buffer;
                    first = 20;
                    var arrays = Allocate(input + 3);
                    GC.Collect();
                    first += 19;
                    return first + arrays.Length;
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static object[] Allocate(int count) => new object[count];
            }
            """, []);

        Assert.Equal(42, Execute(assets, assembly, target, optimization, 0));
    }

    // Run(input): int value = 23; nint[] slots = new nint[1];
    // slots[0] = (stind.i) &value; return *(int*)slots[0] + input;
    private static byte[] BuildManagedAddressStore()
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("ManagedAddressStores"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("ManagedAddressStores.dll");
        var type = module.DefineType("EntryPoint",
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        var run = type.DefineMethod("Run", MethodAttributes.Public | MethodAttributes.Static,
            typeof(int), [typeof(int)]);
        var code = run.GetILGenerator();
        var value = code.DeclareLocal(typeof(int));
        var slots = code.DeclareLocal(typeof(nint[]));
        code.Emit(OpCodes.Ldc_I4, 23);
        code.Emit(OpCodes.Stloc, value);
        code.Emit(OpCodes.Ldc_I4_1);
        code.Emit(OpCodes.Newarr, typeof(nint));
        code.Emit(OpCodes.Stloc, slots);
        code.Emit(OpCodes.Ldloc, slots);
        code.Emit(OpCodes.Ldc_I4_0);
        code.Emit(OpCodes.Ldelema, typeof(nint));
        code.Emit(OpCodes.Ldloca, value);
        code.Emit(OpCodes.Stind_I);
        code.Emit(OpCodes.Ldloc, slots);
        code.Emit(OpCodes.Ldc_I4_0);
        code.Emit(OpCodes.Ldelem_I);
        code.Emit(OpCodes.Ldind_I4);
        code.Emit(OpCodes.Ldarg_0);
        code.Emit(OpCodes.Add);
        code.Emit(OpCodes.Ret);
        type.CreateType();
        using var stream = new MemoryStream();
        assembly.Save(stream);
        return stream.ToArray();
    }

    private static int Execute(
        TestAssets assets,
        string assembly,
        WasmTarget target,
        FinalWasmOptimization optimization,
        int input,
        ImmutableDictionary<string, string>? aliases = null)
    {
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], target,
            ReferenceAssemblyAliases: aliases,
            ProjectDirectory: assets.Directory, Optimization: optimization));
        CompilerTestSupport.ValidateWithNode(compilation.ApplicationModule, assets.Directory);
        return CompilerTestSupport.ExecuteWithStandardWasiNode(
            compilation.ApplicationModule, assets.Directory, input, target, compilation.StaticDataEnd);
    }
}
