using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// C# compiles fixed (char* p = text) against a string without GetPinnableReference (.NET Standard 2.0's, as
// Microsoft.NET.StringTools' netstandard2.0 build was) to a pinned string local, conv.u of the reference, a null test
// and RuntimeHelpers.OffsetToStringData, which CoreCLR accepts. The compiler rejected conv.u of an object reference.
public sealed class LegacyFixedStringCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.Oz)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.Oz)]
    public void FixedStringsLoweredWithoutGetPinnableReferenceReadTheirCharacters(
        WasmTarget target,
        FinalWasmOptimization optimization)
    {
        using var assets = TestAssets.Create();
        var assembly = Path.Combine(assets.Directory, "LegacyFixed_" + target + optimization + ".dll");
        File.WriteAllBytes(assembly, Build());
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], target,
            ReferenceAssemblyAliases: ImmutableDictionary<string, string>.Empty.Add("System.Private.CoreLib", "NetWasm.CoreLib"),
            ProjectDirectory: assets.Directory, Optimization: optimization));
        CompilerTestSupport.ValidateWithNode(compilation.ApplicationModule, assets.Directory);

        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(
            compilation.ApplicationModule, assets.Directory, 41, target, compilation.StaticDataEnd));
    }

    // Run(input): hashes "netwasm" through the pointer and through the indexer; equal hashes return input + 1.
    private static byte[] Build()
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("LegacyFixed"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("LegacyFixed.dll");
        var type = module.DefineType("EntryPoint", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        var run = type.DefineMethod("Run", MethodAttributes.Public | MethodAttributes.Static, typeof(int), [typeof(int)]);
        var code = run.GetILGenerator();
        var text = code.DeclareLocal(typeof(string));
        var pinned = code.DeclareLocal(typeof(string), pinned: true);
        var pointer = code.DeclareLocal(typeof(char*));
        var viaPointer = code.DeclareLocal(typeof(int));
        var viaIndexer = code.DeclareLocal(typeof(int));
        var index = code.DeclareLocal(typeof(int));
        var offset = typeof(RuntimeHelpers).GetProperty("OffsetToStringData")!.GetMethod!;
        var length = typeof(string).GetProperty(nameof(string.Length))!.GetMethod!;
        var chars = typeof(string).GetMethod("get_Chars", [typeof(int)])!;

        code.Emit(OpCodes.Ldstr, "netwasm");
        code.Emit(OpCodes.Stloc, text);
        // fixed (char* pointer = text)
        code.Emit(OpCodes.Ldloc, text);
        code.Emit(OpCodes.Stloc, pinned);
        code.Emit(OpCodes.Ldloc, pinned);
        code.Emit(OpCodes.Conv_U);
        code.Emit(OpCodes.Stloc, pointer);
        var adjusted = code.DefineLabel();
        code.Emit(OpCodes.Ldloc, pointer);
        code.Emit(OpCodes.Brfalse_S, adjusted);
        code.Emit(OpCodes.Ldloc, pointer);
        code.Emit(OpCodes.Call, offset);
        code.Emit(OpCodes.Add);
        code.Emit(OpCodes.Stloc, pointer);
        code.MarkLabel(adjusted);
        Hash(code, text, index, viaPointer, length, (generator, i) =>
        {
            generator.Emit(OpCodes.Ldloc, pointer);
            generator.Emit(OpCodes.Ldloc, i);
            generator.Emit(OpCodes.Conv_I);
            generator.Emit(OpCodes.Ldc_I4_2);
            generator.Emit(OpCodes.Mul);
            generator.Emit(OpCodes.Add);
            generator.Emit(OpCodes.Ldind_U2);
        });
        code.Emit(OpCodes.Ldnull);
        code.Emit(OpCodes.Stloc, pinned);
        Hash(code, text, index, viaIndexer, length, (generator, i) =>
        {
            generator.Emit(OpCodes.Ldloc, text);
            generator.Emit(OpCodes.Ldloc, i);
            generator.Emit(OpCodes.Callvirt, chars);
        });
        var different = code.DefineLabel();
        code.Emit(OpCodes.Ldloc, viaPointer);
        code.Emit(OpCodes.Ldloc, viaIndexer);
        code.Emit(OpCodes.Bne_Un, different);
        code.Emit(OpCodes.Ldarg_0);
        code.Emit(OpCodes.Ldc_I4_1);
        code.Emit(OpCodes.Add);
        code.Emit(OpCodes.Ret);
        code.MarkLabel(different);
        code.Emit(OpCodes.Ldc_I4_1);
        code.Emit(OpCodes.Ret);
        type.CreateType();
        using var stream = new MemoryStream();
        assembly.Save(stream);
        return stream.ToArray();
    }

    // hash = hash * 31 + character, for each index of text.
    private static void Hash(ILGenerator code, LocalBuilder text, LocalBuilder index, LocalBuilder hash,
        MethodInfo length, Action<ILGenerator, LocalBuilder> character)
    {
        var test = code.DefineLabel();
        var body = code.DefineLabel();
        code.Emit(OpCodes.Ldc_I4_0);
        code.Emit(OpCodes.Stloc, index);
        code.Emit(OpCodes.Br, test);
        code.MarkLabel(body);
        code.Emit(OpCodes.Ldloc, hash);
        code.Emit(OpCodes.Ldc_I4, 31);
        code.Emit(OpCodes.Mul);
        character(code, index);
        code.Emit(OpCodes.Add);
        code.Emit(OpCodes.Stloc, hash);
        code.Emit(OpCodes.Ldloc, index);
        code.Emit(OpCodes.Ldc_I4_1);
        code.Emit(OpCodes.Add);
        code.Emit(OpCodes.Stloc, index);
        code.MarkLabel(test);
        code.Emit(OpCodes.Ldloc, index);
        code.Emit(OpCodes.Ldloc, text);
        code.Emit(OpCodes.Callvirt, length);
        code.Emit(OpCodes.Blt, body);
    }
}
