using System.Collections.Immutable;
using System.Reflection;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission.Results;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class WasmModuleEmitterContractTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, false)]
    [InlineData(WasmTarget.Wasm32, true)]
    [InlineData(WasmTarget.Wasm64, false)]
    [InlineData(WasmTarget.Wasm64, true)]
    public void EmitPreservesNativeFactsAndRequestOwnedPlansAcrossEveryCapability(WasmTarget width, bool initializes)
    {
        var fixture = new Fixture(width, initializes);
        var emitter = Assert.IsAssignableFrom<IWasmModuleEmitter>(fixture.Emitter);

        var result = emitter.Emit(fixture.Target);

        Assert.Same(fixture.Result, result);
        Assert.Equal(Fixture.SuccessOrder, fixture.Calls.Select(call => call.Capability).ToArray());
        var imports = Assert.IsType<ModuleImportCollectionRequest>(fixture.Arguments("imports")[0]);
        Assert.Same(fixture.Target.Request, imports.Emission);
        Assert.Same(fixture.Target.Plan.NativeImports, imports.NativeImports);
        Assert.Equal(width, imports.Target);
        Assert.Same(fixture.Target.HostCallbacks, imports.HostCallbacks);
        var built = Assert.IsType<WasmModuleEmissionBuildRequest>(fixture.Arguments("result")[0]);
        Assert.Same(fixture.Target.Plan.NativeImports, built.NativeImports);
        Assert.Same(fixture.Target.Plan.NativeCallbacks, built.NativeCallbacks);
        Assert.Equal(96, built.StaticDataEnd);
        Assert.Equal(fixture.Imports.ToArray(), built.Module.FunctionImports.ToArray());
        Assert.Equal(width, built.Module.Target);
        Assert.Equal(!initializes, built.Module.IncludeNameSection);
        Assert.Equal(["feature"], built.RuntimeFeatures.ToArray());
        Assert.Equal(fixture.Target.Plan.StackTraceMethods.Symbols.ToArray(), built.StackTraceSymbols.ToArray());
        var runtime = fixture.Arguments("runtime");
        Assert.Equal(fixture.Imports.Length, Assert.IsType<int>(runtime[1]));
        Assert.Same(fixture.Target.Request.EntryPoint, runtime[4]);
        var initialization = Assert.IsType<RuntimeInitializationPlan>(runtime[5]);
        Assert.Equal(96, initialization.StaticDataEnd);
        Assert.Equal(initializes ? [new ModuleInitializerCall(80, 91)] : [], initialization.ModuleInitializers.ToArray());
        Assert.Same(initialization, fixture.Arguments("callbacks")[5]);
        Assert.Same(initialization, fixture.Arguments("requested")[6]);
        Assert.Same(initialization, fixture.Arguments("component")[5]);
        Assert.Equal(width, Assert.IsType<WasmTarget>(fixture.Arguments("component")[4]));
        Assert.Same(fixture.Target.Instructions, fixture.Arguments("managed")[6]);
        Assert.Same(fixture.Target.Instructions, fixture.Arguments("constructed")[7]);
        Assert.Same(fixture.Target.Instructions, fixture.Arguments("filters")[5]);
        Assert.Equal(21, fixture.Arguments("exports")[1]);
        Assert.Equal(22, fixture.Arguments("exports")[2]);
        Assert.Equal(23, fixture.Arguments("exports")[3]);
        Assert.Same(built.Module.Functions, fixture.Arguments("boundary")[0]);
        Assert.Equal(fixture.Exports, built.Module.Exports.ToArray());
    }

    [Theory]
    [InlineData("imports")]
    [InlineData("boundary")]
    [InlineData("result")]
    public void EmitPropagatesCapabilityFailureWithoutInvokingLaterCapabilities(string stage)
    {
        var fixture = new Fixture(WasmTarget.Wasm32, false) { FailureAt = stage };
        var emitter = Assert.IsAssignableFrom<IWasmModuleEmitter>(fixture.Emitter);

        var failure = Assert.Throws<InvalidOperationException>(() => emitter.Emit(fixture.Target));

        Assert.Same(fixture.Failure, failure);
        var expected = Fixture.SuccessOrder.Take(Array.IndexOf(Fixture.SuccessOrder, stage) + 1);
        Assert.Equal(expected, fixture.Calls.Select(call => call.Capability));
    }

    [Fact]
    public void EmitRejectsAnAbsentTargetBeforeInvokingCapabilities()
    {
        var fixture = new Fixture(WasmTarget.Wasm32, false);
        var emitter = Assert.IsAssignableFrom<IWasmModuleEmitter>(fixture.Emitter);

        Assert.Throws<ArgumentNullException>(() => emitter.Emit(null!));

        Assert.Empty(fixture.Calls);
    }

    [Fact]
    public void EmitRejectsNamedCallbackCollidingWithAComponentExport()
    {
        var fixture = new Fixture(WasmTarget.Wasm32, false)
        {
            NativeCallbackExportName = "initialize",
            ComponentExportName = "initialize",
        };
        var emitter = Assert.IsAssignableFrom<IWasmModuleEmitter>(fixture.Emitter);

        var exception = Assert.Throws<CompilerException>(() => emitter.Emit(fixture.Target));

        Assert.Equal(DiagnosticCode.NativeInterop, exception.Diagnostic.Code);
        Assert.Equal(
            Fixture.SuccessOrder.TakeWhile(capability => capability != "boundary"),
            fixture.Calls.Select(call => call.Capability));
    }

    private sealed class Fixture
    {
        public static readonly string[] SuccessOrder =
        [
            "entry", "indices", "imports", "managed", "constructed", "delegates", "static",
            "nativeCallbacks", "callbacks", "async", "filters", "runtime", "requested", "exports", "component", "exportValidation", "boundary", "result",
        ];

        public Fixture(WasmTarget width, bool initializes)
        {
            var (request, plan) = WasmModulePlanInvariantValidatorTests.CreateNativePlan();
            var data = ModuleDataPlan.Empty with { StaticDataEnd = 96 };
            if (initializes)
            {
                request = request with { ModuleInitializers = [EmitterTestSupport.EntryKey], EmitStackTrace = true };
                data = data with
                {
                    StaticInitializerGuards = data.StaticInitializerGuards.Add(
                        StaticInitializerGuard.KeyFor(EmitterTestSupport.EntryKey),
                        new(80, EmitterTestSupport.EntryKey, null) { FunctionIndex = OptionalFunctionIndex.At(91) }),
                };
            }
            Target = new(request, plan, data, []);
            var program = new FakeProgram();
            Emitter = new WasmModuleEmitter(program, program,
                new RecordingLayoutProvider(width == WasmTarget.Wasm64 ? WasmTargetLayout.Wasm64 : WasmTargetLayout.Wasm32),
                Capability<IManagedDefinitionSetAppender>("managed"),
                Capability<IConstructedMethodSetAppender>("constructed"),
                Capability<IDelegateFunctionAppender>("delegates"),
                Capability<INativeCallbackFunctionAppender>("nativeCallbacks"),
                Capability<IHostCallbackSetAppender>("callbacks"),
                Capability<IAsyncJSImportSetAppender>("async"),
                Capability<IFilterFunctionSetAppender>("filters"),
                Capability<IRuntimeFunctionAppender>("runtime", new RuntimeFunctionAppendResult(22, 23, 21, false)),
                Capability<IRequestedExportSetAppender>("requested"),
                Capability<IComponentFunctionAppender>("component", ImmutableArray.Create("feature")),
                Capability<IFunctionIndexResolverFactory>("indices", Capability<IFunctionIndexResolver>("resolve")),
                Capability<IModuleExportCollector>("exports", Exports),
                Capability<IModuleExportValidator>("exportValidation"),
                Capability<IModuleImportCollector>("imports", Imports),
                Capability<IEntryPointValidator>("entry"),
                Capability<IManagedBoundaryPlanValidator>("boundary"),
                Capability<IWasmModuleEmissionResultBuilder>("result", Result),
                Capability<IStaticInitializerFunctionAppender>("static"));
        }

        public WasmModuleEmitter Emitter { get; }
        public WasmModuleTarget Target { get; }
        public WasmModuleEmissionResult Result { get; } = new([41, 42], 96);
        public ImmutableArray<WasmFunctionImport> Imports { get; } =
            [new("native", "call", WasmFunctionType.Create(CliValueKind.I4))];
        public WasmExport[] Exports { get; } = [new("visible", 21)];
        public List<(string Capability, object?[] Arguments)> Calls { get; } = [];
        public string? FailureAt { get; init; }
        public string? NativeCallbackExportName { get; init; }
        public string? ComponentExportName { get; init; }
        public InvalidOperationException Failure { get; } = new("capability failure");

        public object?[] Arguments(string capability) =>
            Assert.Single(Calls, call => call.Capability == capability).Arguments;

        private T Capability<T>(string name, object? result = null) where T : class
        {
            var capability = DispatchProxy.Create<T, RecordingCapability>();
            ((RecordingCapability)(object)capability).InvokeCapability = (method, arguments) =>
            {
                Calls.Add((name, arguments));
                if (FailureAt == name)
                    throw Failure;
                if (name == "nativeCallbacks" && NativeCallbackExportName is { } callbackName)
                {
                    var callbacks = Assert.IsType<ImmutableDictionary<string, int>.Builder>(
                        arguments[2]);
                    callbacks.Add(callbackName, 24);
                }
                if (name == "component" && ComponentExportName is { } componentName)
                {
                    var exports = Assert.IsAssignableFrom<ICollection<WasmExport>>(
                        arguments[1]);
                    exports.Add(new(componentName, 25));
                }
                if (name == "exports" && NativeCallbackExportName is { } nativeExportName)
                {
                    return new WasmExport[] { new(nativeExportName, 24) };
                }
                if (name == "exportValidation")
                {
                    new ModuleExportValidator().Validate(
                        Assert.IsAssignableFrom<IReadOnlyCollection<WasmExport>>(
                            arguments[0]),
                        Assert.IsAssignableFrom<IReadOnlyDictionary<string, int>>(
                            arguments[1]));
                }
                if (method.ReturnType != typeof(void) && result is null)
                    throw new InvalidOperationException("unexpected capability result");
                return result;
            };
            return capability;
        }
    }

    // Desktop-only in-memory interface doubles. No proxy or reflection enters
    // the NetWasm product or generated application.
    public class RecordingCapability : DispatchProxy
    {
        public Func<MethodInfo, object?[], object?> InvokeCapability { get; set; } = null!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            InvokeCapability(targetMethod!, args ?? []);
    }
}
