using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;
using NetWasm.Compiler.Pipeline;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Units;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests.Units;

// A store keeps each recorded answer's value; reading it back and writing it again must
// give the bytes kept, for every kind the early cutoff replays.
public sealed class UnitAnswerCodecTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, false)]
    [InlineData(WasmTarget.Wasm64, true)]
    public void EveryKeptAnswerReadsBackAsItWasWritten(WasmTarget target, bool stackTrace)
    {
        using var assets = TestAssets.Create();
        const string Name = "answers";
        var memory = new UnitStoreMemory();
        var options = new CompilerOptions(assets.CompileOptimizedSource("UnitAnswers", CompiledUnitRestoreTests.BaseSource),
            [assets.CoreLib], "EntryPoint", "Run", [], Target: target, EmitStackTrace: stackTrace,
            Optimization: FinalWasmOptimization.O0)
        {
            IntermediateOutputPath = Name,
            CompiledUnits = true,
        };
        var services = new ServiceCollection().AddNetWasmCompiler();
        services.UseUnitMemory(memory);
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<INetWasmCompiler>().Compile(options);

        using var lease = provider.GetRequiredService<IMetadataCompilationLoader>().Load(options.EntryAssemblyPath, options.ReferencePaths);
        var methods = provider.GetRequiredService<IMethodRepositoryFactory>().Create(lease.Snapshot);
        var fields = provider.GetRequiredService<IFieldRepositoryFactory>().Create(lease.Snapshot);
        using var store = new MemoryUnitStoreLoader(memory).Load(Name);
        var kept = new Dictionary<UnitQueryKind, int>();
        var bare = new Dictionary<UnitQueryKind, int>();
        foreach (var segment in store.Segments)
        {
            if (segment is null) continue;
            for (var id = 0; id < segment.QueryCount; id++)
            {
                var query = segment.Query(id);
                if (query.Value.IsEmpty)
                {
                    bare[query.Kind] = bare.GetValueOrDefault(query.Kind) + 1;
                    continue;
                }
                kept[query.Kind] = kept.GetValueOrDefault(query.Kind) + 1;
                var key = query.Key.ToString();
                var value = UnitAnswerCodec.Decode(query.Kind, key, query.Value, methods, fields);
                var again = UnitAnswerCodec.Encode(query.Kind, key, value);
                Assert.True(again is not null && query.Value.SequenceEqual(again), $"{query.Kind} '{query.Key.ToString()}' reads back differently");
            }
        }
        Assert.True(kept.Count > 10, "too few kinds kept values: " + string.Join(", ", kept.Keys));
        // The whole-program plans are kept once per compile, beside its module.
        Assert.Equal([UnitQueryKind.TargetPlans], bare.Keys);
    }
}

// A compile keeps its whole-program plans beside its module; read back, they must be the
// plans the compile emitted against.
public sealed class UnitPlanCodecTests
{
    // JavaScript imports and exports, an asynchronous import, a callback and a delegate bound
    // to an import, which the plans keep beside the program's own.
    private const string Interop = """
        using System;
        using System.Runtime.InteropServices.JavaScript;
        using System.Threading.Tasks;

        public static partial class EntryPoint
        {
            public static int Run(int input)
            {
                Func<string, string?> resolve = Host.Resolve;
                using var subscription = Host.Register(value => value * 2);
                Task<int> pending = Host.FetchAsync();
                Task waiting = Host.WaitAsync();
                ValueTask<int> ping = Host.PingAsync();
                return (resolve("x") ?? "").Length + pending.Id + waiting.Id + (ping.IsCompleted ? 1 : 0) +
                    Host.Day(DateTime.UnixEpoch);
            }

            [JSExport]
            public static int Twice(int value) => value * 2;
        }

        internal static class Host
        {
            [JSImport("resolve", "repro.host")]
            internal static extern string? Resolve(string name);

            [JSImport("register", "repro.host")]
            internal static extern JSSubscription Register(Func<int, int> callback);

            [JSImport("fetch", "repro.host")]
            internal static extern Task<int> FetchAsync();

            [JSImport("wait", "repro.host")]
            internal static extern Task WaitAsync();

            [JSImport("ping", "repro.host")]
            internal static extern ValueTask<int> PingAsync();

            [JSImport("day", "repro.host")]
            internal static extern int Day(DateTime value);
        }

        """;

    // An expression tree compiles to delegates over object arrays and reflected members.
    private const string Expressions = """
        using System;
        using System.Linq.Expressions;

        public class Box
        {
            public int Value;
            public virtual int Twice() => Value * 2;
        }

        public sealed class Doubled : Box
        {
            public override int Twice() => Value * 4;
        }

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                Expression<Func<Box, int>> read = box => box.Value + box.Twice();
                return read.Compile()(input > 0 ? new Doubled { Value = input } : new Box { Value = input });
            }
        }
        """;

    // Delegates converted from other delegates bind their Invoke, by definition and, for a
    // generic delegate, by canonical name (issue 223).
    private const string Delegates = """
        using System;

        public delegate int Transform(int value);

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                Func<int, int> twice = value => value * 2;
                var transform = new Transform(twice);
                var back = new Func<int, int>(transform);
                return back(input);
            }
        }
        """;

    [Theory]
    [InlineData(WasmTarget.Wasm32, "base")]
    [InlineData(WasmTarget.Wasm64, "base")]
    [InlineData(WasmTarget.Wasm32, "kinds")]
    [InlineData(WasmTarget.Wasm32, "interop")]
    [InlineData(WasmTarget.Wasm64, "handles")]
    [InlineData(WasmTarget.Wasm32, "expressions")]
    [InlineData(WasmTarget.Wasm64, "delegates")]
    public void KeptPlansReadBackAsTheCompileUsedThem(WasmTarget target, string program)
    {
        using var assets = TestAssets.Create();
        var (assembly, entry) = Program(assets, program);
        // The expression program reaches a WASI 0.3 platform import, which needs the platform world an SDK build has.
        var (kept, decoded, used) = Plans(assets, assembly, entry, target, platformWorld: program == "expressions");

        Assert.NotNull(decoded);
        Assert.Equal(kept, UnitPlanCodec.Encode(decoded.Target, decoded.RuntimeObjects, decoded.Program));
        Assert.NotEmpty(decoded.Program.AllocatedTypes);
        Assert.NotEmpty(decoded.Program.DirectMethods);
        var plans = decoded.Target;
        Assert.Equal(UnitTargetFingerprint.Of(used), UnitTargetFingerprint.Of(plans));
        var (expected, actual) = (used.FunctionIndices, plans.FunctionIndices);
        Assert.Equal(expected.DirectMethods.OrderBy(pair => pair.Key.ToString()), actual.DirectMethods.OrderBy(pair => pair.Key.ToString()));
        Assert.Equal(expected.ConstructedMethods.OrderBy(pair => pair.Key, StringComparer.Ordinal),
            actual.ConstructedMethods.OrderBy(pair => pair.Key, StringComparer.Ordinal));
        Assert.Equal(expected.DelegateInvokeHelpers.OrderBy(pair => pair.Key, StringComparer.Ordinal),
            actual.DelegateInvokeHelpers.OrderBy(pair => pair.Key, StringComparer.Ordinal));
        Assert.Equal(expected.DelegateMulticastHelpers.OrderBy(pair => pair.Key, StringComparer.Ordinal),
            actual.DelegateMulticastHelpers.OrderBy(pair => pair.Key, StringComparer.Ordinal));
        Assert.Equal(expected.ImportedMethods.OrderBy(pair => pair.Key.ToString()), actual.ImportedMethods.OrderBy(pair => pair.Key.ToString()));
        Assert.Equal(expected.JSImportForwarders.OrderBy(pair => pair.Key.ToString()), actual.JSImportForwarders.OrderBy(pair => pair.Key.ToString()));
        Assert.Equal(expected.DelegateInvokeMethods.OrderBy(pair => pair.Key.ToString()),
            actual.DelegateInvokeMethods.OrderBy(pair => pair.Key.ToString()));
        Assert.Equal(expected.ConstructedDelegateInvokeMethods.OrderBy(pair => pair.Key, StringComparer.Ordinal),
            actual.ConstructedDelegateInvokeMethods.OrderBy(pair => pair.Key, StringComparer.Ordinal));
        if (program == "delegates")
        {
            Assert.NotEmpty(actual.DelegateInvokeMethods);
            Assert.NotEmpty(actual.ConstructedDelegateInvokeMethods);
        }
        Assert.Equal(expected.ManagedThrowFunctionIndex, actual.ManagedThrowFunctionIndex);
        Assert.Equal(expected.ManagedRethrowFunctionIndex, actual.ManagedRethrowFunctionIndex);
        Assert.Equal(used.ModuleData.StaticDataEnd, plans.ModuleData.StaticDataEnd);
    }

    // Native imports and callbacks bind at link time, which a replay cannot redo; such plans
    // are kept without their content and never read back.
    [Theory]
    [InlineData("[DllImport(\"native_math\", EntryPoint = \"add\")] private static extern int Add(int a, int b);")]
    [InlineData("[DllImport(\"host_ui\", EntryPoint = \"add\"), WasmImportLinkage] private static extern int Add(int a, int b);")]
    [InlineData("[UnmanagedCallersOnly(EntryPoint = \"add\", CallConvs = [typeof(CallConvCdecl)])] " +
        "private static int Add(int a, int b) => a + b;")]
    public void PlansWithNativeImportsOrCallbacksAreNotReplayable(string native)
    {
        using var assets = TestAssets.Create();
        var callback = native.Contains("UnmanagedCallersOnly", StringComparison.Ordinal);
        var assembly = assets.CompileOptimizedUnsafeSource("UnitPlans", $$"""
            using System.Runtime.CompilerServices;
            using System.Runtime.InteropServices;

            public static unsafe class EntryPoint
            {
                {{native}}

                public static int Run(int input) =>
                    {{(callback ? "(delegate* unmanaged[Cdecl]<int, int, int>)&Add == null ? 0 : input" : "Add(input, 2)")}};
            }
            """);

        var (kept, decoded, used) = Plans(assets, assembly, "EntryPoint", WasmTarget.Wasm32);

        Assert.False(callback ? used.NativeCallbacks.Methods.IsEmpty : used.NativeImports.Methods.IsEmpty);
        Assert.Equal(9, kept.Length);
        Assert.Null(decoded);
        Assert.NotEqual(default, UnitTargetFingerprint.Of(used));
    }

    // Issue 226: an unmanaged calli's trampoline is a native import too, and the target names it.
    [Fact]
    public void PlansWithUnmanagedCallsAreNotReplayable()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedUnsafeSource("UnitPlans", """
            public static unsafe class EntryPoint
            {
                public static int Run(int input) =>
                    input == 0 ? 0 : ((delegate* unmanaged<int, int>)(nint)input)(input);
            }
            """);

        var (kept, decoded, used) = Plans(assets, assembly, "EntryPoint", WasmTarget.Wasm32);

        Assert.Equal("netwasm_calli_ii", Assert.Single(used.NativeImports.CallSites.Trampolines).Symbol);
        Assert.Equal(9, kept.Length);
        Assert.Null(decoded);
        Assert.NotEqual(default, UnitTargetFingerprint.Of(used));
    }

    [Theory]
    [InlineData("short")]
    [InlineData("magic")]
    [InlineData("version")]
    [InlineData("trailing")]
    public void DamagedPlansAreNotRead(string damage)
    {
        using var assets = TestAssets.Create();
        var (kept, _, _) = Plans(assets, assets.CompileOptimizedSource("UnitPlans", CompiledUnitRestoreTests.BaseSource),
            "EntryPoint", WasmTarget.Wasm32);
        var bytes = damage switch
        {
            "short" => kept[..8],
            "magic" => [(byte)(kept[0] ^ 1), .. kept[1..]],
            "version" => [.. kept[..4], (byte)(kept[4] + 1), .. kept[5..]],
            _ => [.. kept, 0],
        };

        var read = () => UnitPlanCodec.Decode(bytes, new UnreadMethods(), new UnreadFields());

        if (damage == "trailing")
            Assert.Throws<InvalidDataException>(() => read());
        else
            Assert.Null(read());
    }

    private static (string Assembly, string Entry) Program(TestAssets assets, string program) => program switch
    {
        "base" => (assets.CompileOptimizedSource("UnitPlans", CompiledUnitRestoreTests.BaseSource), "EntryPoint"),
        "kinds" => (assets.CompileOptimizedUnsafeSource("UnitPlans", UnitQueryKindTests.Source), "EntryPoint"),
        "interop" => (assets.CompileOptimizedSource("UnitPlans", Interop), "EntryPoint"),
        "expressions" => (assets.CompileOptimizedSource("UnitPlans", Expressions), "EntryPoint"),
        "delegates" => (assets.CompileOptimizedSource("UnitPlans", Delegates), "EntryPoint"),
        _ => (Handles(assets), "MemberHandles.EntryPoint"),
    };

    private static string Handles(TestAssets assets)
    {
        var path = Path.Combine(assets.Directory, "MemberHandles.dll");
        File.WriteAllBytes(path, [.. new Correctness.MemberHandleFixtureBuilder().Build()]);
        return path;
    }

    // The plans a compile kept, read back, and the target it emitted against.
    private static (byte[] Kept, UnitPlans? Decoded, InstructionModuleTarget Used) Plans(TestAssets assets, string assembly,
        string entry, WasmTarget target, bool platformWorld = false)
    {
        const string Name = "plans";
        var memory = new UnitStoreMemory();
        var options = new CompilerOptions(assembly, [assets.CoreLib], entry, "Run", [], Target: target,
            WitPath: platformWorld ? Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0") : null,
            WitWorld: platformWorld ? "netwasm:platform/platform@1.0.0" : null,
            Optimization: FinalWasmOptimization.O0,
            ReferenceAssemblyAliases: ImmutableDictionary<string, string>.Empty.Add("System.Private.CoreLib", "NetWasm.CoreLib"))
        {
            IntermediateOutputPath = Name,
            CompiledUnits = true,
        };
        var services = new ServiceCollection().AddNetWasmCompiler();
        services.UseUnitMemory(memory);
        CapturingUnitStates? captured = null;
        services.Replace(ServiceDescriptor.Singleton<ICompiledUnitStateFactory>(provider =>
            captured = new(ActivatorUtilities.CreateInstance<CompiledUnitStateFactory>(provider))));
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<INetWasmCompiler>().Compile(options);
        var used = captured?.Last?.Target;
        Assert.NotNull(used);

        var kept = memory.Kept(Name);
        Assert.NotNull(kept);
        using var lease = provider.GetRequiredService<IMetadataCompilationLoader>().Load(options.EntryAssemblyPath,
            options.ReferencePaths, options.ReferenceAssemblyAliases);
        var methods = provider.GetRequiredService<IMethodRepositoryFactory>().Create(lease.Snapshot);
        var fields = provider.GetRequiredService<IFieldRepositoryFactory>().Create(lease.Snapshot);
        return (kept.Plans.ToArray(), UnitPlanCodec.Decode(kept.Plans, methods, fields), used);
    }

    private sealed class UnreadMethods : IMethodRepository
    {
        public MethodDefinitionModel GetMethod(EntityKey key) => throw new InvalidOperationException("not read");
    }

    private sealed class UnreadFields : IFieldRepository
    {
        public FieldDefinitionModel GetField(EntityKey key) => throw new InvalidOperationException("not read");
    }

    private sealed class CapturingUnitStates(ICompiledUnitStateFactory inner) : ICompiledUnitStateFactory
    {
        public CompiledUnitState? Last { get; private set; }

        public CompiledUnitState? Create(CompilerOptions options, MetadataCompilationSnapshot metadata,
            CompilationInputObservation? inputs, ReachableProgram program,
            NetWasm.Compiler.ComponentModel.Optimization.BranchPrefixDuplicationPolicy branchPrefixDuplication,
            NetWasm.Compiler.ComponentModel.Optimization.DispatcherEliminationPolicy dispatcherElimination,
            NetWasm.Compiler.Wasm.Units.UnitKeptCompile? servedFrom) =>
            Last = inner.Create(options, metadata, inputs, program, branchPrefixDuplication, dispatcherElimination,
                servedFrom);
    }
}
