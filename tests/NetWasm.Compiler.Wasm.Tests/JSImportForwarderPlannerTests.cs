using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class JSImportForwarderPlannerTests
{
    private static readonly EntityKey SecondImportKey = new(Assembly, 0x06000009);

    // A delegate or function pointer names its target by loading its address: every JSImport
    // whose address the program loads gets a forwarder, and no other method does.
    [Fact]
    public void LeavesThePlanUnchangedWithoutAddressTakenJSImports()
    {
        var program = new FakeProgram();
        var (request, plan) = WasmModulePlanInvariantValidatorTests.CreateNativePlan();
        var ordinary = Instance(program.GetMethod(EntryKey));
        request = request with { CallableMethods = Callables(ordinary) };

        var planned = Planner(program).Plan(request, plan);

        Assert.Same(plan, planned);
    }

    [Fact]
    public void PlacesOneForwarderPerAddressTakenJSImportBeforeStaticInitializers()
    {
        var program = new FakeProgram();
        var (request, plan) = WasmModulePlanInvariantValidatorTests.CreateNativePlan();
        var first = Instance(program.GetMethod(EntryKey) with { JSImport = new("first", "tests") });
        var second = Instance(program.GetMethod(EntryKey) with
        {
            Key = SecondImportKey,
            JSImport = new("second", "tests"),
        });
        var ordinary = Instance(program.GetMethod(ConstructorKey));
        request = request with
        {
            JSImportMethods = [second.Definition, first.Definition],
            CallableMethods = Callables(second, first, ordinary),
        };
        plan = plan with { StaticInitializerFunctionBase = 20 };

        var planned = Planner(program).Plan(request, plan);

        Assert.Equal([EntryKey, SecondImportKey],
            planned.JSImportForwarders.Select(target => target.Definition.Key));
        Assert.Equal(new WasmFunctionIndex(20), planned.FunctionIndices.JSImportForwarders[EntryKey]);
        Assert.Equal(new WasmFunctionIndex(21), planned.FunctionIndices.JSImportForwarders[SecondImportKey]);
        Assert.Equal(22, planned.StaticInitializerFunctionBase);
        Assert.Same(plan.FunctionIndices.DirectMethods, planned.FunctionIndices.DirectMethods);
    }

    [Fact]
    public void RejectsAnAddressTakenCallbackRegisteringJSImport()
    {
        var program = new FakeProgram();
        var (request, plan) = WasmModulePlanInvariantValidatorTests.CreateNativePlan();
        var import = Instance(program.GetMethod(EntryKey) with { JSImport = new("register", "tests") });
        request = request with
        {
            JSImportMethods = [import.Definition],
            CallableMethods = Callables(import),
            HostCallbacks = [new HostCallbackDeclaration(EntryKey, 0, import, "callback")],
        };

        var exception = Assert.Throws<CompilerException>(() => Planner(program).Plan(request, plan));

        Assert.Equal(DiagnosticCode.UnsupportedMetadata, exception.Diagnostic.Code);
        Assert.Contains("registers a JavaScript callback", exception.Diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("Bind a lambda", exception.Diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsAnAddressTakenAsynchronousJSImport()
    {
        var program = new FakeProgram();
        var (request, plan) = WasmModulePlanInvariantValidatorTests.CreateNativePlan();
        var import = Instance(program.GetMethod(EntryKey) with { JSImport = new("fetch", "tests") });
        request = request with
        {
            JSImportMethods = [import.Definition],
            CallableMethods = Callables(import),
            JavaScriptAsyncBindings = ImmutableDictionary<EntityKey, JavaScriptAsyncMethodBinding>.Empty.Add(
                EntryKey,
                new JavaScriptAsyncMethodBinding(
                    EntryKey,
                    new(JavaScriptAsyncReturnKind.Task, CliTypeIdentity.FromStackKind(CliValueKind.I4)),
                    CliTypeIdentity.FromStackKind(CliValueKind.ManagedReference),
                    import,
                    import,
                    import)),
        };

        var exception = Assert.Throws<CompilerException>(() => Planner(program).Plan(request, plan));

        Assert.Equal(DiagnosticCode.UnsupportedMetadata, exception.Diagnostic.Code);
        Assert.Contains("returns a Task or ValueTask", exception.Diagnostic.Message, StringComparison.Ordinal);
    }

    private static IJSImportForwarderPlanner Planner(FakeProgram program) =>
        Assert.IsAssignableFrom<IJSImportForwarderPlanner>(new JSImportForwarderPlanner(program));

    private static MethodInstanceModel Instance(MethodDefinitionModel method) => new(
        method,
        CliTypeIdentity.Named(Assembly, "Test", "Type", isValueType: false),
        [],
        method.Signature);

    private static Dictionary<string, MethodInstanceModel> Callables(params MethodInstanceModel[] targets) =>
        targets.ToDictionary(target => target.CanonicalName, StringComparer.Ordinal);
}
