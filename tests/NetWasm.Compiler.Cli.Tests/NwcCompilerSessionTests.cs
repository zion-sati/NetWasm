using System.Collections.Immutable;
using NetWasm.Compiler.Cli.Tests.TestSupport;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.ExceptionTypes;
using NetWasm.Compiler.StackTraces;
using NetWasm.Compiler.Wasm;

namespace NetWasm.Compiler.Cli.Tests;

public sealed class NwcCompilerSessionTests
{
    private static readonly EntityKey Main = new(new AssemblyIdentity("App"), 0x06000001);

    [Theory]
    [InlineData(WasmTarget.Wasm32, "wasm32")]
    [InlineData(WasmTarget.Wasm64, "wasm64")]
    public void MapsEveryCompilerFact(WasmTarget target, string expectedTarget)
    {
        var symbols = new StackTraceSymbolArtifact([41, 42], "application/json", "symbols.json", "digest");
        var exceptions = new ExceptionTypeMapArtifact([1], "application/json", "exceptions.json", "digest");
        var callbacks = CliTestData.CallbackSupport(WasmValueType.I32);
        var result = new ModuleCompilationResult([0, 97, 115, 109], CliTestData.InteropManifest(expectedTarget), 99)
        {
            StackTraceSymbols = symbols,
            DiagnosticArtifacts = new DiagnosticArtifactBundle([], exceptions, [], null!),
            RuntimeFeatures = [NetWasmRuntimeFeatureIds.LocalTime],
            FunctionImports = [new("host", "read", WasmFunctionType.Create(CliValueKind.I4))],
            NativeImports = [new("library", "compute", [WasmValueType.F64], WasmValueType.I64)],
            NativeCallbackSupport = callbacks,
        };
        var compiler = new RecordingCompiler(result);
        var options = Options(target);

        var actual = new NwcCompilerSession(compiler, new Disposable()).Compile(options);

        Assert.Same(options, compiler.Options);
        Assert.Same(result.ApplicationModule, actual.CoreModule);
        Assert.Equal(99, actual.StaticDataEnd);
        Assert.Same(result.InteropManifest, actual.InteropManifest);
        Assert.Same(symbols, actual.StackTraceSymbols);
        Assert.Same(exceptions, actual.ExceptionTypeMap);
        Assert.Equal(expectedTarget, actual.Target);
        Assert.Equal(result.RuntimeFeatures, actual.RuntimeFeatures);
        Assert.Equal(result.FunctionImports, actual.FunctionImports);
        Assert.Equal(result.NativeImports, actual.NativeImports);
        Assert.Same(callbacks, actual.NativeCallbackSupport);
        Assert.Null(actual.EntryPoint);
        Assert.True(actual.SynchronousEntry);
    }

    [Fact]
    public void LeavesTheExceptionTypeMapOutWithoutDiagnosticArtifacts()
    {
        var result = new ModuleCompilationResult([], CliTestData.InteropManifest("wasm32"), 0);

        var actual = new NwcCompilerSession(new RecordingCompiler(result), new Disposable())
            .Compile(Options(WasmTarget.Wasm32));

        Assert.Null(actual.ExceptionTypeMap);
    }

    // An entry export completes before it returns unless Main is asynchronous: an
    // asynchronous Main is a callback-lifted wasi:cli/run, however CoreLib enters it.
    [Theory]
    [InlineData(false, false, false, true)]
    [InlineData(true, false, false, true)]
    [InlineData(true, true, true, false)]
    [InlineData(true, true, false, false)]
    public void RecordsWhetherTheEntryExportCompletesSynchronously(
        bool hasEntry, bool isAsync, bool enteredThroughCoreLib, bool expected)
    {
        var program = new ReachableProgram(
            hasEntry ? new MethodDefinitionModel(Main, Main, "Main", true, null!, 0) : null,
            ImmutableDictionary<EntityKey, ManagedMethodBody>.Empty,
            [], [], [], [], [], [],
            ImmutableDictionary<EntityKey, MethodRootMap>.Empty,
            ImmutableDictionary<CliTypeIdentity, MethodInstanceModel>.Empty,
            [])
        {
            JavaScriptAsyncBindings = isAsync
                ? ImmutableDictionary<EntityKey, JavaScriptAsyncMethodBinding>.Empty.Add(
                    Main,
                    new JavaScriptAsyncMethodBinding(Main, default, null!, null!, null!, null!)
                    {
                        CommandEntry = enteredThroughCoreLib
                            ? new(new MethodInstanceModel(null!, null!, [], null!), null!, null!)
                            : null,
                    })
                : ImmutableDictionary<EntityKey, JavaScriptAsyncMethodBinding>.Empty,
        };
        var result = ModuleCompilationResult.Of(
            new CompilationResult([], program, null!, CliTestData.InteropManifest("wasm32"), 0));

        var actual = new NwcCompilerSession(new RecordingCompiler(result), new Disposable())
            .Compile(Options(WasmTarget.Wasm32));

        Assert.Equal(expected, actual.SynchronousEntry);
    }

    [Fact]
    public void DisposesTheServicesItOwns()
    {
        var services = new Disposable();
        var session = new NwcCompilerSession(new RecordingCompiler(null!), services);

        session.Dispose();

        Assert.True(services.Disposed);
    }

    [Fact]
    public void ComposesTheCompilerWithTheWasmToolsTheOptionsName()
    {
        var factory = new NwcCompilerSessionFactory(new RecordingConsole());

        using var standard = factory.Create(null);
        using var selected = factory.Create(new ExternalToolCommand("node", ["run.mjs", "wasm-tools.wasm"]));

        Assert.IsType<NwcCompilerSession>(standard);
        Assert.IsType<NwcCompilerSession>(selected);
    }

    [Fact]
    public void RejectsMissingInputs()
    {
        var compiler = new RecordingCompiler(null!);

        Assert.Throws<ArgumentNullException>(() => new NwcCompilerSession(null!, new Disposable()));
        Assert.Throws<ArgumentNullException>(() => new NwcCompilerSession(compiler, null!));
        Assert.Throws<ArgumentNullException>(() =>
            new NwcCompilerSession(compiler, new Disposable()).Compile(null!));
        Assert.Throws<ArgumentNullException>(() => new NwcCompilerSessionFactory(null!));
        Assert.Null(compiler.Options);
    }

    private static CompilerOptions Options(WasmTarget target) => new("App.dll", [], "Entry", "Run", [], target);

    private sealed class RecordingCompiler(ModuleCompilationResult result) : INetWasmModuleCompiler
    {
        public CompilerOptions? Options { get; private set; }

        public ModuleCompilationResult Compile(CompilerOptions options)
        {
            Options = options;
            return result;
        }
    }

    private sealed class Disposable : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }
}
