using System.Collections.Immutable;
using System.Reflection;
using System.Security.Cryptography;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.NativeInterop;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.NativeInterop;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission.Results;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.ModuleEncoding;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class WasmModuleEmissionResultBuilderTests
{
    [Fact]
    public void BuildsModuleAndProjectsManagedMethodMetrics()
    {
        var modules = new RecordingModuleBuilder();
        var metrics = new RecordingMetricProjector();
        var builder = CreateBuilder(modules, metrics);
        var import = new WasmFunctionImport("host", "call",
            WasmFunctionType.Create(CliValueKind.Void));
        var function = new WasmFunctionDefinition("run",
            WasmFunctionType.Create(CliValueKind.Void), [1]);
        var export = new WasmExport("run", 1);
        var emission = new ManagedMethodEmissionRecord("run", "key",
            new([1], 2, 3, 4, "key", FilterEnvironmentLayout.Empty,
                ImmutableDictionary<int, int>.Empty));
        var request = new WasmModuleEmissionBuildRequest(
            new([import], "runtime", "memory", [function], [export], [], true,
                WasmTarget.Wasm64, IncludeNameSection: false),
            512,
            [emission],
            [],
            [NetWasmRuntimeFeatureIds.LocalTime]);

        var result = builder.Build(request);

        Assert.Equal([7, 8], result.Module);
        Assert.Equal(512, result.StaticDataEnd);
        Assert.Equal("run", Assert.Single(result.ManagedMethodMetrics).Identity);
        Assert.Equal(WasmTarget.Wasm64, modules.Target);
        Assert.True(modules.IncludeManagedExceptionTag);
        Assert.False(modules.IncludeNameSection);
        Assert.Same(import, Assert.Single(modules.Imports));
        Assert.Same(import, Assert.Single(result.FunctionImports));
        Assert.Same(function, Assert.Single(modules.Functions));
        Assert.Same(export, Assert.Single(modules.Exports));
        Assert.Same(emission, Assert.Single(metrics.Emissions));
        Assert.Equal([NetWasmRuntimeFeatureIds.LocalTime], result.RuntimeFeatures.ToArray());
        Assert.Empty(result.NativeImports);
        Assert.Null(result.NativeCallbackSupport);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, WasmValueType.I32)]
    [InlineData(WasmTarget.Wasm64, WasmValueType.I64)]
    public void PreservesReachedNativeLibrariesAndResolvesPhysicalPointerWidths(
        WasmTarget target, WasmValueType pointerType)
    {
        var signature = MethodSignatureModel.Create(CliValueKind.NativeInt,
            CliValueKind.I4, CliValueKind.I8, CliValueKind.F4, CliValueKind.F8,
            CliValueKind.NativeInt, CliValueKind.ManagedAddress);
        var native = CreateNativeImport(1, "mule", "scalar", signature);
        var empty = CreateNativeImport(2, "__Internal", "reset", MethodSignatureModel.Create(CliValueKind.Void));
        var request = new WasmModuleEmissionBuildRequest(
            new([native.Import, empty.Import], "runtime", "memory", [], [], [], false, target, false),
            512, [], [], [])
        {
            NativeImports = new([native, empty]),
        };

        var result = CreateBuilder(new RecordingModuleBuilder(), new RecordingMetricProjector()).Build(request);

        Assert.Equal(2, result.NativeImports.Length);
        var first = result.NativeImports[0];
        Assert.Equal("mule", first.LibraryName);
        Assert.Equal("scalar", first.EntryPoint);
        Assert.Equal([WasmValueType.I32, WasmValueType.I64, WasmValueType.F32, WasmValueType.F64,
            pointerType, pointerType], first.Parameters.ToArray());
        Assert.Equal(pointerType, first.ReturnType);
        Assert.Equal("__Internal", result.NativeImports[1].LibraryName);
        Assert.Equal("reset", result.NativeImports[1].EntryPoint);
        Assert.Empty(result.NativeImports[1].Parameters);
        Assert.Null(result.NativeImports[1].ReturnType);
    }

    [Theory]
    [InlineData(CliValueKind.I4, WasmValueType.I32)]
    [InlineData(CliValueKind.I8, WasmValueType.I64)]
    [InlineData(CliValueKind.F4, WasmValueType.F32)]
    [InlineData(CliValueKind.F8, WasmValueType.F64)]
    public void PreservesScalarNativeReturnTypes(CliValueKind kind, WasmValueType expected)
    {
        var native = CreateNativeImport(1, "mule", "scalar", MethodSignatureModel.Create(kind));
        var request = new WasmModuleEmissionBuildRequest(
            new([native.Import], "runtime", "memory", [], [], [], false, WasmTarget.Wasm64, false),
            512, [], [], [])
        { NativeImports = new([native]) };

        var result = CreateBuilder(new RecordingModuleBuilder(), new RecordingMetricProjector()).Build(request);

        Assert.Equal(expected, Assert.Single(result.NativeImports).ReturnType);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, WasmValueType.I32, true)]
    [InlineData(WasmTarget.Wasm64, WasmValueType.I64, true)]
    [InlineData(WasmTarget.Wasm32, WasmValueType.I32, false)]
    public void ProjectsTheExactCallbackSupportArtifact(
        WasmTarget target,
        WasmValueType pointerType,
        bool returnsValue)
    {
        var assembly = new AssemblyIdentity("Callbacks");
        var signature = MethodSignatureModel.Create(
            returnsValue ? CliValueKind.NativeInt : CliValueKind.Void,
            CliValueKind.I4,
            CliValueKind.NativeInt);
        var definition = new MethodDefinitionModel(
            new(assembly, 1),
            new(assembly, 100),
            "Callback",
            true,
            signature,
            1)
        {
            NativeCallback = new([], null, false, false),
        };
        var method = new MethodInstanceModel(
            definition,
            CliTypeIdentity.Named(assembly, "Tests", "Callbacks", false),
            [],
            signature);
        var callbacks = NativeAbiTestSupport.CallbackPlanner().Build(
            ImmutableDictionary<string, MethodInstanceModel>.Empty.Add(
                method.CanonicalName,
                method),
            ImmutableHashSet.Create(StringComparer.Ordinal, method.CanonicalName),
            10);
        var request = new WasmModuleEmissionBuildRequest(
            new([], "runtime", "memory", [], [], [], false, target, false),
            512,
            [],
            [],
            [])
        {
            NativeCallbacks = callbacks,
        };

        var result = CreateBuilder(
            new RecordingModuleBuilder(),
            new RecordingMetricProjector()).Build(request);

        var support = Assert.IsType<WasmNativeCallbackSupportArtifact>(
            result.NativeCallbackSupport);
        Assert.NotEmpty(support.ObjectBytes);
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(support.ObjectBytes)).ToLowerInvariant(),
            support.Sha256);
        var callback = Assert.Single(support.Callbacks);
        Assert.Equal("__netwasm_native_callback_0", callback.NativeSymbol);
        Assert.Equal("__netwasm_application_callback_0", callback.ApplicationExportName);
        Assert.Equal("__netwasm_callback_address_0", callback.RuntimeGetterExportName);
        Assert.Equal([WasmValueType.I32, pointerType], callback.Parameters.ToArray());
        Assert.Equal(
            returnsValue ? pointerType : (WasmValueType?)null,
            callback.ReturnType);
        Assert.Equal([callback.ApplicationExportName],
            support.TemporaryApplicationExports.ToArray());
        Assert.Equal([callback.RuntimeGetterExportName!],
            support.TemporaryRuntimeExports.ToArray());
    }

    private static NativeMethodImport CreateNativeImport(int token, string library, string entry,
        MethodSignatureModel signature)
    {
        var assembly = new AssemblyIdentity("Native");
        var declaration = new NativeImportDeclaration(library, entry, MethodImportAttributes.CallingConventionCDecl,
            false, false, false, false);
        var definition = new MethodDefinitionModel(new(assembly, token), new(assembly, 100), entry, true, signature, 0)
        { NativeImport = declaration };
        var method = new MethodInstanceModel(definition, CliTypeIdentity.Named(assembly, "Test", "Native", false), [], signature);
        return new(method, NativeAbiTestSupport.Plan(declaration, signature, signature),
            new(RuntimeAbi.RuntimeModule, entry, new(signature.ParameterTypes, signature.ReturnType)));
    }

    [Fact]
    public void RejectsMissingRequest()
    {
        Assert.Throws<ArgumentNullException>(() =>
            CreateBuilder(new RecordingModuleBuilder(), new RecordingMetricProjector())
                .Build(null!));
    }

    private static IWasmModuleEmissionResultBuilder CreateBuilder(
        IWasmModuleBuilder modules,
        IManagedMethodEmissionMetricProjector metrics) => new[]
    {
        new WasmModuleEmissionResultBuilder(modules, metrics, CallbackObjects()),
    }.Cast<IWasmModuleEmissionResultBuilder>().Single();

    private static NativeCallbackObjectWriter CallbackObjects()
    {
        var unsigned = new UnsignedLeb128Encoder();
        return new NativeCallbackObjectWriter(
            unsigned,
            new Utf8StringEncoder(unsigned),
            new WasmSectionWriter(unsigned),
            static buffer => new WasmBinaryWriter(buffer));
    }

    private sealed class RecordingModuleBuilder : IWasmModuleBuilder
    {
        public IReadOnlyList<WasmFunctionImport> Imports { get; private set; } = [];
        public IReadOnlyList<WasmFunctionDefinition> Functions { get; private set; } = [];
        public IReadOnlyList<WasmExport> Exports { get; private set; } = [];
        public bool IncludeManagedExceptionTag { get; private set; }
        public bool IncludeNameSection { get; private set; }
        public WasmTarget Target { get; private set; }

        public byte[] Build(IReadOnlyList<WasmFunctionImport> functionImports,
            string memoryImportModule, string memoryImportName,
            IReadOnlyList<WasmFunctionDefinition> functions,
            IReadOnlyList<WasmExport> exports, IReadOnlyList<DataSegment> dataSegments,
            bool includeManagedExceptionTag = false,
            WasmTarget target = WasmTarget.Wasm32,
            bool includeNameSection = true)
        {
            Imports = functionImports;
            Functions = functions;
            Exports = exports;
            IncludeManagedExceptionTag = includeManagedExceptionTag;
            IncludeNameSection = includeNameSection;
            Target = target;
            return [7, 8];
        }
    }

    private sealed class RecordingMetricProjector :
        IManagedMethodEmissionMetricProjector
    {
        public IReadOnlyList<ManagedMethodEmissionRecord> Emissions { get; private set; } = [];

        public ImmutableArray<WasmManagedMethodEmissionMetric> Project(
            IEnumerable<ManagedMethodEmissionRecord> emissions)
        {
            Emissions = [.. emissions];
            return [new("run", 1, 2, 3, 4,
                ImmutableDictionary<int, int>.Empty)];
        }
    }
}
