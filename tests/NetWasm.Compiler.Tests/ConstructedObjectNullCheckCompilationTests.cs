using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// newobj never produces null, so the compiler leaves out the null check of a field
// access or callvirt on the object it produced, on a copy dup made, or through a
// local the block stored it to or that is stored only once, with it, before the
// access. A constructor's this is proven the same way when the whole program only
// reaches the constructor through newobj and chained constructors. Everything else
// keeps its check and still throws NullReferenceException.
public sealed class ConstructedObjectNullCheckCompilationTests
{
    private const string Types = """
        using System.Runtime.CompilerServices;

        public class Box
        {
            public int A;
            public int B;

            [MethodImpl(MethodImplOptions.NoInlining)]
            public int Get() => A;
        }

        public class Base
        {
            public int X;

            public Base(int x)
            {
                X = x;
            }
        }

        public sealed class Derived : Base
        {
            public int Y;

            public Derived(int y) : base(y + 1)
            {
                Y = y;
            }
        }
        """;

    // Each field access and callvirt in Probe, in order, and whether its object is
    // proven non-null.
    [Theory]
    [InlineData("var b = new Box(); b.A = index; result = b.A;", "stfld+ ldfld+")]
    [InlineData("var b = new Box { A = index, B = 2 }; result = b.A + b.B;", "stfld+ stfld+ ldfld+ ldfld+")]
    [InlineData("result = new Box { A = index }.Get();", "stfld+ callvirt+")]
    [InlineData("var b = new Box(); Keep(b); if (index > 0) result = b.A;", "ldfld+")]
    [InlineData("Box b = null; for (var i = 0; i < index; i++) { b = new Box(); result += b.A; }", "ldfld+")]
    [InlineData("var b = new Box(); if (index > 0) b = null; result = b.A;", "ldfld-")]
    [InlineData("var b = index > 0 ? new Box() : null; result = b.A;", "ldfld-")]
    [InlineData("var b = new Box(); Touch(ref b); result = b.A;", "ldfld-")]
    [InlineData("var b = new Box(); Keep(b); try { result = b.A; } finally { result++; }", "ldfld-")]
    public void AccessesOfObjectsNewobjProducedSkipTheirNullChecks(string statement, string expected)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("ConstructedObjects", Types + $$"""

            public static class EntryPoint
            {
                public static int Run(int count) => Probe(count);

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int Probe(int index)
                {
                    var result = 0;
                    {{statement}}
                    return result;
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static void Touch<T>(ref T value) => value = default!;

                // Escapes the object, so it stays an object rather than becoming locals.
                [MethodImpl(MethodImplOptions.NoInlining)]
                private static void Keep(object value) { }
            }
            """);

        var enabled = Describe(Method(Compile(assets, assembly, ManagedNewObjectNullCheckElimination.Enabled), "EntryPoint", "Probe").Body);
        var disabled = Describe(Method(Compile(assets, assembly, ManagedNewObjectNullCheckElimination.Disabled), "EntryPoint", "Probe").Body);

        Assert.Equal(expected, enabled);
        // An earlier access may prove a later one either way; the first is newobj's alone.
        if (expected.Contains('+', StringComparison.Ordinal))
            Assert.True(disabled.Count(static c => c == '+') < enabled.Count(static c => c == '+'), disabled);
        else
            Assert.Equal(expected, disabled);
    }

    // A constructor only newobj and chained constructors reach stores to its this
    // without null checks, the chained constructor's too.
    [Fact]
    public void ConstructorsReachedOnlyByNewobjAndChainsStoreToThisUnchecked()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("ConstructorReceivers", Types + """

            public static class EntryPoint
            {
                public static int Run(int count) => new Derived(count).X + new Base(count).X;
            }
            """);

        var program = Compile(assets, assembly, ManagedNewObjectNullCheckElimination.Enabled);

        Assert.Equal("stfld+", Describe(Method(program, "Base", ".ctor").Body));
        // Base's constructor is inlined into Derived's, which stores both fields.
        Assert.Equal("stfld+ stfld+", Describe(Method(program, "Derived", ".ctor").Body));
        var disabled = Compile(assets, assembly, ManagedNewObjectNullCheckElimination.Disabled);
        Assert.Equal("stfld-", Describe(Method(disabled, "Base", ".ctor").Body));
        Assert.Equal("stfld- stfld-", Describe(Method(disabled, "Derived", ".ctor").Body));
    }

    // Every preset from O1 proves objects newobj produced within each method; only O3, Os
    // and Oz pay for the whole-program pass that proves a constructor's this.
    [Theory]
    [InlineData(FinalWasmOptimization.O0, "stfld- ldfld-", "stfld-")]
    [InlineData(FinalWasmOptimization.O1, "stfld+ ldfld+", "stfld-")]
    [InlineData(FinalWasmOptimization.O2, "stfld+ ldfld+", "stfld-")]
    [InlineData(FinalWasmOptimization.O3, "stfld+ ldfld+", "stfld+")]
    [InlineData(FinalWasmOptimization.Os, "stfld+ ldfld+", "stfld+")]
    public void PresetsFromO1ProveNewObjectsAndTheWholeProgramPresetsConstructors(
        FinalWasmOptimization optimization, string probe, string constructor)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("NewObjectPresets" + optimization, Types + """

            public static class EntryPoint
            {
                public static int Run(int count) => Probe(count) + new Base(count).X;

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int Probe(int index)
                {
                    var b = new Box { A = index };
                    Keep(b);
                    return b.A;
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static void Keep(object value) { }
            }
            """);

        var program = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Optimization: optimization)).Program;

        Assert.Equal(probe, Describe(Method(program, "EntryPoint", "Probe").Body));
        Assert.Equal(constructor, Describe(Method(program, "Base", ".ctor").Body));
    }

    // IL may call a constructor on any object, null included. That constructor, and
    // the one it chains to, keep their checks and throw as desktop .NET does.
    [Theory]
    [InlineData(FinalWasmOptimization.Oz)]
    [InlineData(FinalWasmOptimization.O3)]
    public void AConstructorCalledOnAnotherObjectKeepsItsChecks(FinalWasmOptimization optimization)
    {
        using var assets = TestAssets.Create();
        var assembly = Path.Combine(assets.Directory, "ConstructorOnNull" + optimization + ".dll");
        File.WriteAllBytes(assembly, BuildConstructorCalledOnNull());

        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            ReferenceAssemblyAliases: CoreLibAlias, ProjectDirectory: assets.Directory, Optimization: optimization,
            OptimizationOverrides: new(NewObjectNullCheckElimination: ManagedNewObjectNullCheckElimination.Enabled)));

        Assert.Equal("stfld-", Describe(Method(compilation.Program, "Leaf", ".ctor").Body));
        Assert.Equal("stfld-", Describe(Method(compilation.Program, "Root", ".ctor").Body));
        CompilerTestSupport.ValidateWithNode(compilation.ApplicationModule, assets.Directory);
        // 1 for the throw on null, 40 and 1 from the objects newobj built.
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(
            compilation.ApplicationModule, assets.Directory, 0, WasmTarget.Wasm32, compilation.StaticDataEnd));
    }

    // The cases that keep their checks throw NullReferenceException with the
    // optimization on, as they do with it off.
    [Theory]
    [InlineData(ManagedNewObjectNullCheckElimination.Enabled)]
    [InlineData(ManagedNewObjectNullCheckElimination.Disabled)]
    public void ReassignedMergedAndExposedObjectsStillThrow(ManagedNewObjectNullCheckElimination setting)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("ConstructedObjectsThrow", Types + """

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var score = new Box { A = 41 }.Get() + new Derived(input).Y;
                    try { score += Reassigned(input); } catch (System.NullReferenceException) { score += 100; }
                    try { score += Merged(input); } catch (System.NullReferenceException) { score += 1000; }
                    try { score += Exposed(input); } catch (System.NullReferenceException) { score += 10000; }
                    return score;
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int Reassigned(int input)
                {
                    var b = new Box();
                    if (input > 0) b = null;
                    return b.A;
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int Merged(int input)
                {
                    var b = input > 0 ? null : new Box();
                    return b.A;
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int Exposed(int input)
                {
                    var b = new Box();
                    Reset(ref b, input);
                    return b.A;
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static void Reset(ref Box box, int input)
                {
                    if (input > 0) box = null;
                }
            }
            """);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], ProjectDirectory: assets.Directory,
            Optimization: FinalWasmOptimization.Oz, OptimizationOverrides: new(NewObjectNullCheckElimination: setting)));

        CompilerTestSupport.ValidateWithNode(compilation.ApplicationModule, assets.Directory);
        Assert.Equal(11142, CompilerTestSupport.ExecuteWithStandardWasiNode(
            compilation.ApplicationModule, assets.Directory, 1, WasmTarget.Wasm32, compilation.StaticDataEnd));
    }

    private static readonly ImmutableDictionary<string, string> CoreLibAlias =
        ImmutableDictionary<string, string>.Empty.Add("System.Private.CoreLib", "NetWasm.CoreLib");

    private static ReachableProgram Compile(TestAssets assets, string assembly, ManagedNewObjectNullCheckElimination setting) =>
        NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            Optimization: FinalWasmOptimization.O3,
            OptimizationOverrides: new(NewObjectNullCheckElimination: setting))).Program;

    private static ManagedMethodBody Method(ReachableProgram program, string type, string name) =>
        program.Methods.Values.Concat(program.ConstructedMethods.Values).Single(method =>
            method.Method.Definition.Name == name && method.Method.DeclaringType.CanonicalName.EndsWith(type, StringComparison.Ordinal));

    // Each field access and callvirt, with + where its object is proven non-null.
    private static string Describe(CilMethodBody body) => string.Join(" ", body.Instructions
        .Where(static instruction => instruction.Operation is CilOperation.LoadField or CilOperation.StoreField or
            CilOperation.CallVirtual)
        .Select(instruction => (instruction.Operation switch
        {
            CilOperation.LoadField => "ldfld",
            CilOperation.StoreField => "stfld",
            _ => "callvirt",
        }) + (body.ArrayBoundsProof is { } proof && proof.ProvesNonNull(instruction.Offset) ? "+" : "-")));

    // Root's constructor stores 1 to its field; Leaf's chains to Root's and stores 40 to
    // its own. Run calls Leaf's constructor on null, which throws, and then constructs
    // both with newobj: 1 + 40 + 1.
    private static byte[] BuildConstructorCalledOnNull()
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("ConstructorOnNull"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("ConstructorOnNull.dll");
        var root = module.DefineType("Root", TypeAttributes.Public | TypeAttributes.Class);
        var rootValue = root.DefineField("Value", typeof(int), FieldAttributes.Public);
        var rootConstructor = root.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, []);
        var code = rootConstructor.GetILGenerator();
        code.Emit(OpCodes.Ldarg_0);
        code.Emit(OpCodes.Call, typeof(object).GetConstructor([])!);
        code.Emit(OpCodes.Ldarg_0);
        code.Emit(OpCodes.Ldc_I4_1);
        code.Emit(OpCodes.Stfld, rootValue);
        code.Emit(OpCodes.Ret);
        var leaf = module.DefineType("Leaf", TypeAttributes.Public | TypeAttributes.Class, root);
        var leafValue = leaf.DefineField("Extra", typeof(int), FieldAttributes.Public);
        var leafConstructor = leaf.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, []);
        code = leafConstructor.GetILGenerator();
        code.Emit(OpCodes.Ldarg_0);
        code.Emit(OpCodes.Call, rootConstructor);
        code.Emit(OpCodes.Ldarg_0);
        code.Emit(OpCodes.Ldc_I4, 40);
        code.Emit(OpCodes.Stfld, leafValue);
        code.Emit(OpCodes.Ret);
        var entry = module.DefineType("EntryPoint", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        var run = entry.DefineMethod("Run", MethodAttributes.Public | MethodAttributes.Static, typeof(int), [typeof(int)]);
        code = run.GetILGenerator();
        var score = code.DeclareLocal(typeof(int));
        code.BeginExceptionBlock();
        code.Emit(OpCodes.Ldnull);
        code.Emit(OpCodes.Call, leafConstructor);
        code.BeginCatchBlock(typeof(NullReferenceException));
        code.Emit(OpCodes.Pop);
        code.Emit(OpCodes.Ldc_I4_1);
        code.Emit(OpCodes.Stloc, score);
        code.EndExceptionBlock();
        code.Emit(OpCodes.Ldloc, score);
        code.Emit(OpCodes.Newobj, leafConstructor);
        code.Emit(OpCodes.Ldfld, leafValue);
        code.Emit(OpCodes.Add);
        code.Emit(OpCodes.Newobj, rootConstructor);
        code.Emit(OpCodes.Ldfld, rootValue);
        code.Emit(OpCodes.Add);
        code.Emit(OpCodes.Ret);
        root.CreateType();
        leaf.CreateType();
        entry.CreateType();
        using var stream = new MemoryStream();
        assembly.Save(stream);
        return stream.ToArray();
    }
}
