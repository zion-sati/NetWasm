// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions.Startup;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class ComponentBoundaryEmitterTests
{
    private static readonly AssemblyIdentity Assembly = new("ComponentTest");
    private static readonly EntityKey TypeKey = new(Assembly, 0x02000001);
    private static readonly EntityKey EntryKey = new(Assembly, 0x06000001);
    private static readonly EntityKey ImportKey = new(Assembly, 0x06000002);
    private static readonly EntityKey ExportKey = new(Assembly, 0x06000003);

    [Theory]
    [InlineData(WasmTarget.Wasm32, "cm32p2")]
    [InlineData(WasmTarget.Wasm64, "cm64p2")]
    public void EmitsTargetWidthComponentInfrastructureAndPostReturn(
        WasmTarget target,
        string prefix)
    {
        var methods = new Repository();
        var (emitter, imports) = CreateEmitter(target);
        var request = CreateRequest(methods, target);
        var exportName = CanonicalAbiNames.Export(
            methods.Export.WitExport!.InterfaceName,
            methods.Export.WitExport.FunctionName,
            target);

        var result = emitter.Emit(
            request,
            target,
            initialization: TestRuntimeInitialization.Create(4096),
            importedFunctionCount: 31,
            definedFunctionCount: 7,
            ImmutableDictionary<string, int>.Empty.Add(exportName, 37));

        Assert.Equal(3, result.Functions.Length);
        Assert.Equal(
            [$"{prefix}_memory", $"{prefix}_realloc", $"{prefix}_initialize",
                $"{prefix}|example:component/api@1|read_post"],
            result.Exports.Select(export => export.Name));
        Assert.Equal(WasmExportKind.Memory, result.Exports[0].Kind);
        Assert.Equal(0, result.Exports[0].Index);
        Assert.Equal([38, 39, 40], result.Exports.Skip(1).Select(export => export.Index));
        Assert.Equal(
            [CliValueKind.ManagedAddress, CliValueKind.ManagedAddress,
                CliValueKind.ManagedAddress, CliValueKind.ManagedAddress],
            result.Functions[0].Type.Parameters.ToArray());
        Assert.Equal(CliValueKind.ManagedAddress, result.Functions[0].Type.Result);
        var reallocateIndex = imports.Resolve(RuntimeImportSymbol.ComponentReallocate);
        Assert.InRange(reallocateIndex, 0, 127);
        // Canonical lowering can request buffers while runtime initialization
        // is obtaining the environment. Forward the four arguments without
        // entering initialization again.
        Assert.Equal<byte>(
            [0, WasmOpcodes.LocalGet, 0, WasmOpcodes.LocalGet, 1,
                WasmOpcodes.LocalGet, 2, WasmOpcodes.LocalGet, 3,
                WasmOpcodes.Call, (byte)reallocateIndex, WasmOpcodes.End],
            result.Functions[0].Body);
        // Instantiation readies the runtime state only: the module initializers, which may wait,
        // run in the first entry, since the Component Model traps a wait during instantiation.
        Assert.True(result.Functions[1].Body.AsSpan().IndexOf(
            [WasmOpcodes.Call, (byte)TestRuntimeInitialization.StateInitializerFunctionIndex]) >= 0);
        Assert.True(result.Functions[1].Body.AsSpan().IndexOf(
            [WasmOpcodes.Call, (byte)TestRuntimeInitialization.InitializerFunctionIndex]) < 0);
        Assert.Equal([CliValueKind.I4], result.Functions[2].Type.Parameters.ToArray());
        Assert.Empty(result.RuntimeFeatures);
    }

    [Fact]
    public void OmitsInfrastructureWithoutWitBoundary()
    {
        var methods = new Repository();
        var request = CreateRequest(methods, WasmTarget.Wasm32) with
        {
            WitImportMethods = [],
            RequestedExports = ImmutableDictionary<string, EntityKey>.Empty,
            ComponentContract = ComponentBoundaryContract.Empty,
        };
        var (emitter, _) = CreateEmitter(WasmTarget.Wasm32);
        var result = emitter.Emit(
            request,
            WasmTarget.Wasm32,
            TestRuntimeInitialization.Create(4096),
            31,
            7,
            ImmutableDictionary<string, int>.Empty);

        Assert.Empty(result.Functions);
        Assert.Empty(result.Exports);
        Assert.Empty(result.RuntimeFeatures);
    }

    [Fact]
    public void RejectsComponentContractForCoreApplicationProfile()
    {
        var methods = new Repository();

        Assert.Throws<ArgumentException>(() =>
            CreateRequest(methods, WasmTarget.Wasm32, WasmModuleProfile.CoreApplication));
    }

    [Fact]
    public void RejectsEmptyContractForComponentCoreModuleProfile()
    {
        var methods = new Repository();

        Assert.Throws<ArgumentException>(() =>
            CreateRequest(
                methods,
                WasmTarget.Wasm32,
                WasmModuleProfile.ComponentCoreModule,
                ComponentBoundaryContract.Empty));
    }

    [Fact]
    public void OrdersExportsAndSkipsResourceAndManagedPostReturnFunctions()
    {
        var methods = new Repository();
        var target = WasmTarget.Wasm32;
        var (emitter, _) = CreateEmitter(target);
        var alpha = CreateExport(methods, "example:component/api@1.0.0", "alpha");
        var drop = CreateExport(
            methods,
            "example:component/api@1.0.0",
            "drop",
            CanonicalAbiFunctionKind.ExportedResourceDestructor);
        var zeta = CreateExport(methods, "example:component/api@1.0.0", "zeta");
        var last = CreateExport(methods, "example:component/zapi@1.0.0", "zeta");
        var contract = new ComponentBoundaryContract(
            "example:component@1",
            "component",
            [],
            [last, zeta, drop, alpha]);
        var request = CreateRequest(methods, target) with
        {
            ComponentContract = contract,
        };
        var managedExportIndices = ImmutableDictionary.CreateBuilder<string, int>();
        foreach (var function in contract.Exports)
        {
            managedExportIndices[CanonicalAbiNames.Export(function, target)] = 50;
        }
        managedExportIndices[CanonicalAbiNames.PostReturn(
            alpha.InterfaceName,
            alpha.FunctionName,
            target)] = 51;

        var result = emitter.Emit(
            request,
            target,
            initialization: TestRuntimeInitialization.Create(100),
            importedFunctionCount: 3,
            definedFunctionCount: 4,
            managedExportIndices.ToImmutable());

        Assert.Equal(
            ["cm32p2_memory", "cm32p2_realloc", "cm32p2_initialize",
                "cm32p2|example:component/api@1|zeta_post",
                "cm32p2|example:component/zapi@1|zeta_post"],
            result.Exports.Select(export => export.Name));
        Assert.Equal([7, 8, 9, 10], result.Exports.Skip(1).Select(export => export.Index));
        Assert.Equal(
            ["component.realloc", "component.initialize",
                "component.post-return.cm32p2|example:component/api@1|zeta",
                "component.post-return.cm32p2|example:component/zapi@1|zeta"],
            result.Functions.Select(function => function.Name));
    }

    [Fact]
    public void RejectsExportWithoutManagedWrapper()
    {
        var methods = new Repository();
        var (emitter, _) = CreateEmitter(WasmTarget.Wasm32);

        var exception = Assert.Throws<InvalidOperationException>(() => emitter.Emit(
            CreateRequest(methods, WasmTarget.Wasm32),
            WasmTarget.Wasm32,
            initialization: TestRuntimeInitialization.Create(0),
            importedFunctionCount: 0,
            definedFunctionCount: 0,
            ImmutableDictionary<string, int>.Empty));

        Assert.Contains("no managed wrapper", exception.Message);
    }

    [Fact]
    public void RejectsNullRequestThroughBoundaryContract()
    {
        var (emitter, _) = CreateEmitter(WasmTarget.Wasm32);

        Assert.Throws<ArgumentNullException>(() => emitter.Emit(
            null!,
            WasmTarget.Wasm32,
            initialization: TestRuntimeInitialization.Create(0),
            importedFunctionCount: 0,
            definedFunctionCount: 0,
            ImmutableDictionary<string, int>.Empty));
    }

    [Fact]
    public void RejectsNullManagedExportIndicesThroughBoundaryContract()
    {
        var methods = new Repository();
        var (emitter, _) = CreateEmitter(WasmTarget.Wasm32);

        Assert.Throws<ArgumentNullException>(() => emitter.Emit(
            CreateRequest(methods, WasmTarget.Wasm32),
            WasmTarget.Wasm32,
            initialization: TestRuntimeInitialization.Create(0),
            importedFunctionCount: 0,
            definedFunctionCount: 0,
            null!));
    }

    // jco calls the initialize export while it instantiates the component, where the Component Model
    // traps any wait, so it readies the runtime state and runs no startup preflight, with a process
    // entry (which runs them itself) or without one (which reads local time and symbols at first
    // use, as a raw module does). A TZ naming a zone made a component without a process entry
    // fail every export, even one that never reads local time. The feature is still reported.
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void InitializeReadiesTheRuntimeStateAndRunsNoStartupPreflight(bool processEntry, bool usesLocalTime)
    {
        var methods = new Repository();
        var (emitter, _) = CreateEmitter(
            WasmTarget.Wasm32,
            usesLocalTime ? new([methods.Export.Key], UsesLocalTime: true) : StartupPreflights.None);
        var request = CreateRequest(methods, WasmTarget.Wasm32) with
        {
            EntryPoint = processEntry ? methods.Export : null,
            EntryPointProfile = processEntry ? WasmEntryPointProfile.Process : WasmEntryPointProfile.None,
        };
        var exportName = CanonicalAbiNames.Export(
            methods.Export.WitExport!.InterfaceName,
            methods.Export.WitExport.FunctionName,
            WasmTarget.Wasm32);

        var result = emitter.Emit(
            request,
            WasmTarget.Wasm32,
            TestRuntimeInitialization.Create(4096),
            31,
            7,
            ImmutableDictionary<string, int>.Empty.Add(exportName, 37));

        var initialize = result.Functions.Single(function => function.Name == "component.initialize");
        Assert.Equal<byte>(
            [0, WasmOpcodes.Call, (byte)TestRuntimeInitialization.StateInitializerFunctionIndex, WasmOpcodes.End],
            initialize.Body);
        Assert.Equal(usesLocalTime ? [NetWasmRuntimeFeatureIds.LocalTime] : [], result.RuntimeFeatures.ToArray());
    }

    private static WasmEmissionRequest CreateRequest(
        Repository methods,
        WasmTarget target,
        WasmModuleProfile moduleProfile = WasmModuleProfile.ComponentCoreModule,
        ComponentBoundaryContract? componentContract = null)
    {
        var exportName = CanonicalAbiNames.Export(
            methods.Export.WitExport!.InterfaceName,
            methods.Export.WitExport.FunctionName,
            target);
        return WasmEmissionRequest.Create(
            methods.Entry,
            ImmutableDictionary<EntityKey,
                NetWasm.Compiler.ControlFlow.Structured.StructuredMethod>.Empty,
            ImmutableDictionary<EntityKey, MethodRootMap>.Empty,
            [],
            ImmutableDictionary<string, EntityKey>.Empty.Add(exportName, ExportKey),
            witImportMethods: [methods.Import],
            componentContract: componentContract ?? new ComponentBoundaryContract(
                "example:component@1.0.0",
                "component",
                [new CanonicalAbiFunction(
                    methods.Import.WitImport!.InterfaceName,
                    methods.Import.WitImport.FunctionName,
                    ImportKey,
                    [],
                    new(CanonicalAbiTypeKind.U64,
                        methods.Import.Signature.ReturnSignatureType))],
                [new CanonicalAbiFunction(
                    methods.Export.WitExport!.InterfaceName,
                    methods.Export.WitExport.FunctionName,
                    ExportKey,
                    [],
                    new(CanonicalAbiTypeKind.S32,
                        methods.Export.Signature.ReturnSignatureType))]),
            moduleProfile: moduleProfile);
    }

    private static (IComponentBoundaryEmitter Emitter, RuntimeImportCatalog Imports) CreateEmitter(
        WasmTarget target,
        StartupPreflights? preflights = null)
    {
        var imports = WasmRuntimeImports.CreateCatalog();
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var emitter = new ComponentBoundaryEmitter(
            imports,
            new InstantiationStateInitializer(),
            CreateCanonicalAbiFunctionTypePlanner(),
            new FixedStartupPreflightMethodSelector(preflights ?? StartupPreflights.None),
            new GeneratedFunctionWriterFactory());
        return (new[] { emitter }.Cast<IComponentBoundaryEmitter>().Single(), imports);
    }

    private static CanonicalAbiFunction CreateExport(
        Repository methods,
        string interfaceName,
        string functionName,
        CanonicalAbiFunctionKind kind = CanonicalAbiFunctionKind.Function) => new(
            interfaceName,
            functionName,
            ExportKey,
            [],
            new(CanonicalAbiTypeKind.S32, methods.Export.Signature.ReturnSignatureType))
        {
            Kind = kind,
        };

    private static CanonicalAbiFunctionTypePlanner CreateCanonicalAbiFunctionTypePlanner() => new(
        new CanonicalAbiSignaturePlanner(new CanonicalAbiTypeFlattener()));

    private sealed class FixedStartupPreflightMethodSelector(StartupPreflights preflights) : IStartupPreflightMethodSelector
    {
        public StartupPreflights Select(WasmEmissionRequest request) => preflights;
    }

    private sealed class Repository : IMethodRepository
    {
        public MethodDefinitionModel Entry { get; } = new(
            EntryKey,
            TypeKey,
            "Run",
            true,
            MethodSignatureModel.Create(CliValueKind.I4, CliValueKind.I4),
            1);

        public MethodDefinitionModel Import { get; } = new(
            ImportKey,
            TypeKey,
            "Clock",
            true,
            MethodSignatureModel.Create(CliValueKind.I8),
            0)
        {
            WitImport = new("wasi:clocks/monotonic-clock@0.2.0", "now"),
        };

        public MethodDefinitionModel Export { get; } = new(
            ExportKey,
            TypeKey,
            "Read",
            true,
            MethodSignatureModel.Create(CliValueKind.I4),
            1)
        {
            WitExport = new("example:component/api@1.0.0", "read"),
        };

        public MethodDefinitionModel GetMethod(EntityKey key) => key == EntryKey
            ? Entry
            : key == ImportKey
                ? Import
                : key == ExportKey
                    ? Export
                    : throw new KeyNotFoundException();
    }
}
