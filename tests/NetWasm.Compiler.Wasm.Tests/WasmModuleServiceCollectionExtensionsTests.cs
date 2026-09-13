using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Instructions.Calls;
using NetWasm.Compiler.Wasm.Emission.Instructions.Interop;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class WasmModuleServiceCollectionExtensionsTests
{
    [Fact]
    public void RejectsATypeDescriptorSourceWithoutEnumMetadata()
    {
        var services = new ServiceCollection();
        var program = new FakeProgram();
        var layouts = new RecordingLayoutProvider();

        var exception = Assert.Throws<ArgumentException>(() =>
            services.AddWasmModuleEmission(
                program,
                program,
                program,
                program,
                program,
                program,
                new FakeIntrinsics(),
                layouts,
                layouts,
                layouts,
                layouts,
                layouts,
                layouts,
                layouts,
                layouts,
                new DescriptorSourceWithoutEnumMetadata()));

        Assert.Equal("typeDescriptors", exception.ParamName);
    }

    [Fact]
    public void ComposesTheCompleteTargetFreeModuleGraph()
    {
        var services = new ServiceCollection();
        var program = new FakeProgram();
        var layouts = new RecordingLayoutProvider();

        var returned = EmitterTestSupport.AddWasmModuleEmission(
            services,
            program,
            new FakeIntrinsics(),
            layouts);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });

        Assert.Same(services, returned);
        Assert.IsType<WasmModuleTargetFactory>(
            provider.GetRequiredService<IWasmModuleTargetFactory>());
        Assert.IsType<WasmModuleEmitter>(
            provider.GetRequiredService<IWasmModuleEmitter>());
        Assert.IsType<StackTypeCompatibilityValidator>(
            provider.GetRequiredService<IStackTypeCompatibilityValidator>());
        Assert.IsType<WasmModulePlanner>(
            provider.GetRequiredService<WasmModulePlanner>());
        Assert.IsType<WasmModulePlanner>(
            provider.GetRequiredService<IWasmModulePlanner>());
        Assert.IsType<ModuleDataPlanner>(
            provider.GetRequiredService<ModuleDataPlanner>());
        Assert.IsType<ModuleDataPlanner>(
            provider.GetRequiredService<IModuleDataPlanner>());
        Assert.IsType<ManagedBoundaryPlanBuilder>(
            provider.GetRequiredService<IManagedBoundaryPlanBuilder>());
        Assert.IsType<ManagedMethodEmitter>(
            provider.GetRequiredService<IManagedMethodEmitter>());
        Assert.IsType<CanonicalAbiTypeFlattener>(
            provider.GetRequiredService<ICanonicalAbiTypeFlattener>());
        Assert.IsType<CanonicalAbiSignaturePlanner>(
            provider.GetRequiredService<ICanonicalAbiSignaturePlanner>());
        Assert.IsType<CanonicalAbiFunctionTypePlanner>(
            provider.GetRequiredService<ICanonicalAbiFunctionTypePlanner>());
        Assert.IsType<ManagedTerminalExceptionBoundaryEmitter>(
            provider.GetRequiredService<IManagedTerminalExceptionBoundaryEmitter>());
        Assert.IsType<HostCallbackStringArgumentMarshaller>(
            provider.GetRequiredService<IHostCallbackStringArgumentMarshaller>());
        Assert.IsType<HostCallbackByteArrayArgumentMarshaller>(
            provider.GetRequiredService<IHostCallbackByteArrayArgumentMarshaller>());

        Assert.IsType<FunctionLoadEmitter>(
            provider.GetRequiredService<IFunctionLoader>());
        Assert.IsType<VirtualFunctionLoadEmitter>(
            provider.GetRequiredService<IVirtualFunctionLoader>());
        Assert.IsType<DelegateCountFunctionEmitter>(
            provider.GetRequiredService<IDelegateCountFunctionEmitter>());
        Assert.IsType<DelegateLeafFunctionEmitter>(
            provider.GetRequiredService<IDelegateLeafFunctionEmitter>());
        Assert.IsType<DelegateEqualityFunctionEmitter>(
            provider.GetRequiredService<IDelegateEqualityFunctionEmitter>());
        Assert.IsType<DelegateRemoveFunctionEmitter>(
            provider.GetRequiredService<IDelegateRemoveFunctionEmitter>());
        Assert.IsType<DelegateInvokeFunctionEmitter>(
            provider.GetRequiredService<IDelegateInvokeFunctionEmitter>());
        Assert.IsType<HostCallbackFunctionEmitter>(
            provider.GetRequiredService<IHostCallbackFunctionEmitter>());
        Assert.IsType<AsyncJSImportResolveEmitter>(
            provider.GetRequiredService<IAsyncJSImportResolveEmitter>());
        Assert.IsType<AsyncJSImportRejectEmitter>(
            provider.GetRequiredService<IAsyncJSImportRejectEmitter>());
        Assert.IsType<AsyncJSImportCancelEmitter>(
            provider.GetRequiredService<IAsyncJSImportCancelEmitter>());
        Assert.IsType<AsyncJSImportResolveTypeResolver>(
            provider.GetRequiredService<IAsyncJSImportResolveTypeResolver>());
        Assert.IsType<AsyncJSExportWrapperEmitter>(
            provider.GetRequiredService<IAsyncJSExportWrapperEmitter>());
        Assert.IsType<AsyncJSExportStatusEmitter>(
            provider.GetRequiredService<IAsyncJSExportStatusEmitter>());
        Assert.IsType<AsyncJSExportResultEmitter>(
            provider.GetRequiredService<IAsyncJSExportResultEmitter>());
        Assert.IsType<AsyncJSExportCompletionEmitter>(
            provider.GetRequiredService<IAsyncJSExportCompletionEmitter>());
        Assert.IsType<AsyncJSExportResultTypeResolver>(
            provider.GetRequiredService<IAsyncJSExportResultTypeResolver>());
        Assert.IsType<FilterFuncletEmitter>(
            provider.GetRequiredService<IFilterFuncletEmitter>());
        Assert.IsType<FilterDispatcherEmitter>(
            provider.GetRequiredService<IFilterDispatcherEmitter>());
        Assert.IsType<FinalizerDispatcherEmitter>(
            provider.GetRequiredService<IFinalizerDispatcherEmitter>());
        Assert.IsType<EntryPointEmitter>(
            provider.GetRequiredService<IEntryPointEmitter>());
        Assert.IsType<ComponentBoundaryEmitter>(
            provider.GetRequiredService<IComponentBoundaryEmitter>());

        var commands = provider.GetServices<IInstructionCommandProvider>()
            .SelectMany(commandProvider => commandProvider.Commands)
            .ToArray();
        var registry = new InstructionCommandRegistry(commands);
        Assert.Equal(SupportedCil.Operations.Length, commands.Length);
        Assert.All(SupportedCil.Operations, operation =>
            Assert.Equal(operation, registry.Resolve(operation).Operation));

        var callEmitters = provider.GetServices<CallEmissionRegistration>()
            .ToDictionary(registration => registration.Kind);
        Assert.Equal(Enum.GetValues<CallEmissionKind>().Length, callEmitters.Count);
        Assert.All(Enum.GetValues<CallEmissionKind>(), kind =>
            Assert.Same(
                callEmitters[kind].Emitter,
                provider.GetRequiredService<ICallEmissionRegistry>().Get(kind)));

        Assert.IsType<DelegateInvokeCallEmitter>(
            callEmitters[CallEmissionKind.DelegateInvoke].Emitter);
        Assert.IsType<VirtualDispatchCallEmitter>(
            callEmitters[CallEmissionKind.VirtualDispatch].Emitter);
    }

    private sealed class DescriptorSourceWithoutEnumMetadata : ITypeDescriptorSource
    {
        public ImmutableArray<TypeDescriptorLayout> TypeDescriptors => [];
        public ImmutableArray<ConstructedTypeDescriptorLayout> ConstructedTypeDescriptors => [];
        public ImmutableArray<ValueTypeDescriptorLayout> ValueTypeDescriptors => [];
    }
}
