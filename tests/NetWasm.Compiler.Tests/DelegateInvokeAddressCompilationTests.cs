using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// A delegate's Invoke is the runtime's, without a body. C# converts one delegate to another
// with ldftn Invoke and calls Invoke with callvirt; IL may also call it directly or take its
// address with ldvirtftn (issue 223). Each runs the delegate, its invocation list included,
// as desktop .NET does, for Func and for a delegate type the program declares.
public sealed class DelegateInvokeAddressCompilationTests
{
    // Each input's case, in the order Run's switch takes them.
    private static readonly string[] Cases =
        ["CallFunc", "AddressOfFunc", "CallTransform", "AddressOfTransform", "CallList", "AddressOfList"];

    [Theory]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.Oz)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.O3)]
    public void DirectCallsAndVirtualAddressesOfInvokeRunTheDelegate(WasmTarget target, FinalWasmOptimization optimization)
    {
        using var assets = TestAssets.Create();
        var image = Build();
        var assembly = Path.Combine(assets.Directory, $"DelegateInvokeAddresses{target}{optimization}.dll");
        File.WriteAllBytes(assembly, image);
        var desktop = Assembly.Load(image).GetType("DelegateInvokeAddresses.EntryPoint")!.GetMethod("Run")!;

        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "DelegateInvokeAddresses.EntryPoint", "Run", [], target,
            ReferenceAssemblyAliases: CoreLibAlias, ProjectDirectory: assets.Directory, Optimization: optimization));
        CompilerTestSupport.ValidateWithNode(compilation.ApplicationModule, assets.Directory);

        // Twice(21) is 42; a list of Count then Twice returns Twice's result and counts once.
        int[] expected = [42, 42, 42, 42, 4201, 4201];
        for (var input = 0; input < Cases.Length; input++)
        {
            Assert.Equal(expected[input], (int)desktop.Invoke(null, [input])!);
            Assert.True(expected[input] == CompilerTestSupport.ExecuteWithStandardWasiNode(
                compilation.ApplicationModule, assets.Directory, input, target, compilation.StaticDataEnd), Cases[input]);
        }
    }

    private static readonly ImmutableDictionary<string, string> CoreLibAlias =
        ImmutableDictionary<string, string>.Empty.Add("System.Private.CoreLib", "NetWasm.CoreLib");

    private static byte[] Build()
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("DelegateInvokeAddresses"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("DelegateInvokeAddresses.dll");
        var transform = module.DefineType("DelegateInvokeAddresses.Transform",
            TypeAttributes.Public | TypeAttributes.Sealed, typeof(MulticastDelegate));
        var transformConstructor = transform.DefineConstructor(
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.RTSpecialName,
            CallingConventions.Standard, [typeof(object), typeof(nint)]);
        transformConstructor.SetImplementationFlags(MethodImplAttributes.Runtime | MethodImplAttributes.Managed);
        var transformInvoke = transform.DefineMethod("Invoke",
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Virtual,
            typeof(int), [typeof(int)]);
        transformInvoke.SetImplementationFlags(MethodImplAttributes.Runtime | MethodImplAttributes.Managed);

        var entry = module.DefineType("DelegateInvokeAddresses.EntryPoint",
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        var calls = entry.DefineField("s_calls", typeof(int), FieldAttributes.Private | FieldAttributes.Static);
        var twice = entry.DefineMethod("Twice", MethodAttributes.Public | MethodAttributes.Static, typeof(int), [typeof(int)]);
        var code = twice.GetILGenerator();
        code.Emit(OpCodes.Ldarg_0);
        code.Emit(OpCodes.Ldc_I4_2);
        code.Emit(OpCodes.Mul);
        code.Emit(OpCodes.Ret);
        var count = entry.DefineMethod("Count", MethodAttributes.Public | MethodAttributes.Static, typeof(int), [typeof(int)]);
        code = count.GetILGenerator();
        code.Emit(OpCodes.Ldsfld, calls);
        code.Emit(OpCodes.Ldc_I4_1);
        code.Emit(OpCodes.Add);
        code.Emit(OpCodes.Stsfld, calls);
        code.Emit(OpCodes.Ldarg_0);
        code.Emit(OpCodes.Ldc_I4_1);
        code.Emit(OpCodes.Add);
        code.Emit(OpCodes.Ret);

        var func = typeof(Func<int, int>);
        var funcConstructor = func.GetConstructor([typeof(object), typeof(nint)])!;
        var funcInvoke = func.GetMethod("Invoke")!;
        var combine = typeof(Delegate).GetMethod(nameof(Delegate.Combine), [typeof(Delegate), typeof(Delegate)])!;

        var run = entry.DefineMethod("Run", MethodAttributes.Public | MethodAttributes.Static, typeof(int), [typeof(int)]);
        code = run.GetILGenerator();
        var labels = Cases.Select(_ => code.DefineLabel()).ToArray();
        code.Emit(OpCodes.Ldarg_0);
        code.Emit(OpCodes.Switch, labels);
        code.Emit(OpCodes.Ldc_I4_M1);
        code.Emit(OpCodes.Ret);
        for (var index = 0; index < Cases.Length; index++)
        {
            code.MarkLabel(labels[index]);
            var (constructor, invoke) = Cases[index].EndsWith("Transform", StringComparison.Ordinal)
                ? ((ConstructorInfo)transformConstructor, (MethodInfo)transformInvoke)
                : (funcConstructor, funcInvoke);
            var list = Cases[index].EndsWith("List", StringComparison.Ordinal);
            if (list)
            {
                // s_calls = 0; (Func<int, int>)Delegate.Combine(Count, Twice)
                code.Emit(OpCodes.Ldc_I4_0);
                code.Emit(OpCodes.Stsfld, calls);
                code.Emit(OpCodes.Ldnull);
                code.Emit(OpCodes.Ldftn, count);
                code.Emit(OpCodes.Newobj, constructor);
            }
            code.Emit(OpCodes.Ldnull);
            code.Emit(OpCodes.Ldftn, twice);
            code.Emit(OpCodes.Newobj, constructor);
            if (list)
            {
                code.Emit(OpCodes.Call, combine);
                code.Emit(OpCodes.Castclass, func);
            }
            if (Cases[index].StartsWith("AddressOf", StringComparison.Ordinal))
            {
                // new Func<int, int>(inner, ldvirtftn inner.Invoke), then callvirt on it.
                code.Emit(OpCodes.Dup);
                code.Emit(OpCodes.Ldvirtftn, invoke);
                code.Emit(OpCodes.Newobj, funcConstructor);
                code.Emit(OpCodes.Ldc_I4_S, (sbyte)21);
                code.Emit(OpCodes.Callvirt, funcInvoke);
            }
            else
            {
                code.Emit(OpCodes.Ldc_I4_S, (sbyte)21);
                code.Emit(OpCodes.Call, invoke);
            }
            if (list)
            {
                code.Emit(OpCodes.Ldc_I4, 100);
                code.Emit(OpCodes.Mul);
                code.Emit(OpCodes.Ldsfld, calls);
                code.Emit(OpCodes.Add);
            }
            code.Emit(OpCodes.Ret);
        }

        transform.CreateType();
        entry.CreateType();
        using var stream = new MemoryStream();
        assembly.Save(stream);
        return stream.ToArray();
    }
}
