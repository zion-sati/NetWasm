using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class WasmModuleEmitterTests
{
    [Fact]
    public void ConstructorRejectsMissingMethodRepository()
    {
        Assert.Throws<ArgumentNullException>(() => new WasmModuleEmitter(
            null!, null!, null!, null!, null!, null!, null!, null!, null!, null!,
            null!, null!, null!, null!, null!, null!, null!, null!, null!, null!,
            null!));
    }

    [Fact]
    public void ConstructorRejectsMissingSymbolFormatter()
    {
        Assert.Throws<ArgumentNullException>(() => new WasmModuleEmitter(
            new FakeProgram(), null!, null!, null!, null!, null!, null!, null!, null!,
            null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!,
            null!));
    }

    [Fact]
    public void ConstructorRejectsMissingTargetLayout()
    {
        Assert.Throws<ArgumentNullException>(() => new WasmModuleEmitter(
            new FakeProgram(), new FakeProgram(), null!, null!, null!, null!, null!,
            null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!,
            null!));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void EmitsOnlyFromTheSuppliedImmutableModuleTarget(bool hasModuleInitializer, bool entryless)
    {
        var program = new FakeProgram();
        var layouts = new RecordingLayoutProvider();
        var request = CreateEmissionRequest(program);
        if (entryless)
        {
            var method = Assert.IsType<MethodDefinitionModel>(request.EntryPoint);
            var instance = new MethodInstanceModel(method,
                new TestCilTypeIdentityResolver().Resolve(method.DeclaringType), [], method.Signature);
            request = request with
            {
                EntryPoint = null,
                EntryPointProfile = WasmEntryPointProfile.None,
                RequestedExports = new Dictionary<string, EntityKey> { ["value"] = EntryKey },
                MethodInstances = ImmutableDictionary<string, MethodInstanceModel>.Empty
                    .Add(instance.CanonicalName, instance),
            };
        }
        var services = new ServiceCollection();
        EmitterTestSupport.AddWasmModuleEmission(
            services,
            program,
            new FakeIntrinsics(),
            layouts);
        var initialization = new RecordingInitialization();
        services.AddSingleton<IRuntimeStateInitializer>(initialization);
        var entryValidation = new RecordingEntryValidation();
        services.AddSingleton<IEntryPointValidator>(entryValidation);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
        var target = provider.GetRequiredService<IWasmModuleTargetFactory>()
            .Create(request);
        if (hasModuleInitializer)
        {
            var data = target.ModuleData with
            {
                StaticInitializerGuards = target.ModuleData.StaticInitializerGuards.Add(
                    StaticInitializerGuard.KeyFor(EntryKey),
                    new(240, EntryKey, null) { FunctionIndex = OptionalFunctionIndex.At(91) }),
            };
            target = new(request with { ModuleInitializers = [EntryKey] }, target.Plan, data, target.HostCallbacks);
        }

        var result = provider.GetRequiredService<IWasmModuleEmitter>().Emit(target);

        Assert.NotEmpty(result.Module);
        Assert.Equal(entryless ? 0 : 1, entryValidation.Calls);
        Assert.Equal(target.ModuleData.StaticDataEnd, result.StaticDataEnd);
        var planned = Assert.Single(initialization.Plans);
        Assert.Equal(hasModuleInitializer ? [new ModuleInitializerCall(240, 91)] : [], planned.ModuleInitializers.ToArray());
    }

    [Fact]
    public void EmissionRequestRequiresAnEntryOnlyForEntryBearingProfiles()
    {
        var request = CreateEmissionRequest();
        var library = WasmEmissionRequest.Create(null, request.Methods, request.RootMaps, [],
            request.RequestedExports, entryPointProfile: WasmEntryPointProfile.None);

        Assert.Null(library.EntryPoint);
        Assert.Equal(WasmEntryPointProfile.None, library.EntryPointProfile);
        Assert.Throws<ArgumentException>(() => WasmEmissionRequest.Create(request.EntryPoint,
            request.Methods, request.RootMaps, [], request.RequestedExports,
            entryPointProfile: WasmEntryPointProfile.None));
        Assert.Throws<ArgumentNullException>(() => WasmEmissionRequest.Create(null,
            request.Methods, request.RootMaps, [], request.RequestedExports));
        var contract = new ComponentBoundaryContract("example:worker@1.0.0", "worker", [],
            [new CanonicalAbiFunction("", "value",
                Assert.IsType<MethodDefinitionModel>(request.EntryPoint).Key, [], null)]);
        Assert.Throws<ArgumentException>(() => WasmEmissionRequest.Create(request.EntryPoint,
            request.Methods, request.RootMaps, [], request.RequestedExports, componentContract: contract));
        Assert.Throws<ArgumentException>(() => WasmEmissionRequest.Create(request.EntryPoint,
            request.Methods, request.RootMaps, [], request.RequestedExports,
            moduleProfile: WasmModuleProfile.ComponentCoreModule));
    }

    private sealed class RecordingEntryValidation : IEntryPointValidator
    {
        public int Calls { get; private set; }
        public void Validate(MethodDefinitionModel entryPoint) => Calls++;
    }

    private sealed class RecordingInitialization : IRuntimeStateInitializer
    {
        public List<RuntimeInitializationPlan> Plans { get; } = [];
        public void Initialize(GeneratedFunctionWriterLease code, RuntimeInitializationPlan plan) => Plans.Add(plan);
    }
}
