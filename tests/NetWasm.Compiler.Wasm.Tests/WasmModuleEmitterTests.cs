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
            null!, null!, null!, null!, null!, null!, null!, null!, null!));
    }

    [Fact]
    public void ConstructorRejectsMissingSymbolFormatter()
    {
        Assert.Throws<ArgumentNullException>(() => new WasmModuleEmitter(
            new FakeProgram(), null!, null!, null!, null!, null!, null!, null!, null!,
            null!, null!, null!, null!, null!, null!, null!, null!, null!, null!));
    }

    [Fact]
    public void ConstructorRejectsMissingTargetLayout()
    {
        Assert.Throws<ArgumentNullException>(() => new WasmModuleEmitter(
            new FakeProgram(), new FakeProgram(), null!, null!, null!, null!, null!,
            null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmitsOnlyFromTheSuppliedImmutableModuleTarget(bool hasModuleInitializer)
    {
        var program = new FakeProgram();
        var layouts = new RecordingLayoutProvider();
        var request = CreateEmissionRequest(program);
        var services = new ServiceCollection();
        EmitterTestSupport.AddWasmModuleEmission(
            services,
            program,
            new FakeIntrinsics(),
            layouts);
        var initialization = new RecordingInitialization();
        services.AddSingleton<IRuntimeStateInitializer>(initialization);
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
        Assert.Equal(target.ModuleData.StaticDataEnd, result.StaticDataEnd);
        var planned = Assert.Single(initialization.Plans);
        Assert.Equal(hasModuleInitializer ? [new ModuleInitializerCall(240, 91)] : [], planned.ModuleInitializers.ToArray());
    }

    private sealed class RecordingInitialization : IRuntimeStateInitializer
    {
        public List<RuntimeInitializationPlan> Plans { get; } = [];
        public void Initialize(GeneratedFunctionWriterLease code, RuntimeInitializationPlan plan) => Plans.Add(plan);
    }
}
