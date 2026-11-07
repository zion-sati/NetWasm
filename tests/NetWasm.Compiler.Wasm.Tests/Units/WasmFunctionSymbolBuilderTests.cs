using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Units;

namespace NetWasm.Compiler.Wasm.Tests.Units;

public sealed class WasmFunctionSymbolBuilderTests
{
    private static readonly AssemblyIdentity Assembly = new("Program");
    private static readonly EntityKey Run = new(Assembly, 0x06000001);
    private static readonly EntityKey Helper = new(Assembly, 0x06000002);
    private static readonly EntityKey Initializer = new(Assembly, 0x06000003);

    [Fact]
    public void FunctionsAreNamedByImportFieldsCanonicalNamesAndHelperRoles()
    {
        var symbols = new WasmFunctionSymbolBuilder().Build(
            Plan(new(
                ImmutableDictionary<EntityKey, WasmFunctionIndex>.Empty
                    .Add(Run, new(2)).Add(Helper, new(3)).Add(Initializer, new(4)),
                ImmutableDictionary<string, WasmFunctionIndex>.Empty.Add("List<int>::Add", new(5)),
                ImmutableDictionary<string, WasmFunctionIndex>.Empty.Add("Func<int>", new(8)),
                ImmutableDictionary<EntityKey, WasmFunctionIndex>.Empty)
            {
                ManagedThrowFunctionIndex = OptionalFunctionIndex.At(6),
                ManagedRethrowFunctionIndex = OptionalFunctionIndex.At(7),
                DelegateMulticastHelpers = ImmutableDictionary<string, WasmFunctionIndex>.Empty
                    .Add("Func<int>", new(10)),
            }),
            Data(new StaticInitializerGuard(64, Initializer, null)
            {
                FunctionIndex = OptionalFunctionIndex.At(9),
            }),
            [Import("netwasm_runtime", "allocate"), Import("netwasm_host", "write")]);

        Assert.Equal(new Dictionary<uint, string>
        {
            [0] = "import:netwasm_runtime\u0001allocate",
            [1] = "import:netwasm_host\u0001write",
            [2] = "method:Program::Run",
            [3] = "method:Program::Helper",
            [4] = "method:Program::.cctor",
            [5] = "method:List<int>::Add",
            [6] = "managed-throw",
            [7] = "managed-rethrow",
            [8] = "delegate-invoke:Func<int>",
            [9] = "static-initializer:Program::.cctor",
            [10] = "delegate-multicast:Func<int>",
        }, symbols.ByIndex.OrderBy(pair => pair.Key).ToDictionary());
        Assert.Equal(3u, symbols.BySymbol["method:Program::Helper"]);
    }

    [Fact]
    public void AnIndexOrSymbolTwoFunctionsWouldShareIsLeftOut()
    {
        var symbols = new WasmFunctionSymbolBuilder().Build(
            Plan(new(
                ImmutableDictionary<EntityKey, WasmFunctionIndex>.Empty
                    .Add(Run, new(1)).Add(Helper, new(2)),
                ImmutableDictionary<string, WasmFunctionIndex>.Empty
                    .Add("Program::Run", new(3)),
                ImmutableDictionary<string, WasmFunctionIndex>.Empty
                    .Add("Func<int>", new(2)),
                ImmutableDictionary<EntityKey, WasmFunctionIndex>.Empty)),
            Data(),
            [Import("netwasm_runtime", "allocate")]);

        Assert.Equal([0u], symbols.ByIndex.Keys.Order());
        Assert.False(symbols.BySymbol.ContainsKey("method:Program::Run"));
    }

    private static WasmFunctionImport Import(string module, string name) =>
        new(module, name, WasmFunctionType.Create(CliValueKind.Void));

    private static WasmModulePlan Plan(FunctionIndexMap indices) => new(
        new(WasmModuleProfile.CoreApplication, false), FinalizationPlan.Empty, [],
        [
            new(new ManagedMethodIdentity("Program::Run"), Run),
            new(new ManagedMethodIdentity("Program::Helper"), Helper),
            new(new ManagedMethodIdentity("Program::.cctor"), Initializer),
        ],
        [], StackTraceMethodPlan.Disabled, [], indices,
        new([], OptionalFunctionIndex.Missing, OptionalFunctionIndex.Missing,
            OptionalFunctionIndex.Missing, OptionalFunctionIndex.Missing,
            OptionalFunctionIndex.Missing, OptionalFunctionIndex.Missing));

    private static ModuleDataPlan Data(params StaticInitializerGuard[] guards) => new(
        new Dictionary<StructuredExceptionGroupKey, ExceptionGroupMetadata>(),
        [], [],
        guards.Select((guard, index) => (guard, index))
            .ToImmutableDictionary(item => $"guard{item.index}", item => item.guard),
        0);
}
