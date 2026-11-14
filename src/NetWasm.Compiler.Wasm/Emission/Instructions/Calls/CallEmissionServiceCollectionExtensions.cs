using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Instructions.Interop;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

using NetWasm.Compiler.Wasm.Emission.Instructions.Calls.ManagedCallSites;
using NetWasm.Compiler.Wasm.Emission.AddressTakenValues;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Calls;

internal static class CallEmissionServiceCollectionExtensions
{
    public static IServiceCollection AddWasmCallEmission(this IServiceCollection services)
    {
        services.AddAddressTakenValuePromotion();
        services.AddManagedCallSiteResolution();
        services.AddSingleton<IConstrainedReferenceReceiverEmitter,
            ConstrainedReferenceReceiverEmitter>();
        services.AddSingleton<IVirtualDispatchEmissionPlanBuilder,
            VirtualDispatchEmissionPlanBuilder>();
        services.AddSingleton<IVirtualDispatchCasePlanner, VirtualDispatchCasePlanner>();
        services.AddSingleton<DelegateInvokeCallEmitter>();
        services.AddSingleton<VirtualDispatchCallEmitter>();
        services.AddSingleton<SharedObjectDispatchCallEmitter>();
        services.AddSingleton<IJSImportInvocationEmitter, JSImportInvocationEmitter>();
        services.AddSingleton<JSImportCallEmitter>();
        services.AddSingleton<AsyncJSImportCallEmitter>();
        services.AddSingleton<DirectCallEmitter>();
        services.AddSingleton<INativeAbiCallEmitter, NativeAbiCallEmitter>();
        services.AddSingleton<NativeCallEmitter>();
        services.AddSingleton<ICallEmissionKindResolver, CallEmissionKindResolver>();
        services.AddSingleton<CallEmissionRegistration>(provider => new(
            CallEmissionKind.DelegateInvoke,
            provider.GetRequiredService<DelegateInvokeCallEmitter>()));
        services.AddSingleton<CallEmissionRegistration>(provider => new(
            CallEmissionKind.VirtualDispatch,
            provider.GetRequiredService<VirtualDispatchCallEmitter>()));
        services.AddSingleton<CallEmissionRegistration>(provider => new(
            CallEmissionKind.SharedObjectDispatch,
            provider.GetRequiredService<SharedObjectDispatchCallEmitter>()));
        services.AddSingleton<CallEmissionRegistration>(provider => new(
            CallEmissionKind.RuntimeIntrinsic,
            new InitializingCallEmitter(provider.GetRequiredService<RuntimeIntrinsicCallEmitter>(),
                provider.GetRequiredService<IMethodInitializationTriggerClassifier>(),
                provider.GetRequiredService<IStaticInitializationEmitter>())));
        services.AddSingleton<CallEmissionRegistration>(provider => new(
            CallEmissionKind.AsyncJSImport,
            new InitializingCallEmitter(provider.GetRequiredService<AsyncJSImportCallEmitter>(),
                provider.GetRequiredService<IMethodInitializationTriggerClassifier>(),
                provider.GetRequiredService<IStaticInitializationEmitter>())));
        services.AddSingleton<CallEmissionRegistration>(provider => new(
            CallEmissionKind.JavaScriptImport,
            new InitializingCallEmitter(provider.GetRequiredService<JSImportCallEmitter>(),
                provider.GetRequiredService<IMethodInitializationTriggerClassifier>(),
                provider.GetRequiredService<IStaticInitializationEmitter>())));
        services.AddSingleton<CallEmissionRegistration>(provider => new(
            CallEmissionKind.Direct,
            provider.GetRequiredService<DirectCallEmitter>()));
        services.AddSingleton<CallEmissionRegistration>(provider => new(
            CallEmissionKind.Native,
            provider.GetRequiredService<NativeCallEmitter>()));
        services.AddSingleton<ICallEmissionRegistry, CallEmissionRegistry>();
        services.AddSingleton<INativeFunctionAddressEmitter,
            NativeFunctionAddressEmitter>();
        services.AddSingleton<FunctionLoadEmitter>();
        services.AddSingleton<IFunctionLoader>(provider =>
            provider.GetRequiredService<FunctionLoadEmitter>());
        services.AddSingleton<VirtualFunctionLoadEmitter>();
        services.AddSingleton<IVirtualFunctionLoader>(provider =>
            provider.GetRequiredService<VirtualFunctionLoadEmitter>());
        services.AddInstructionCommandProvider<CallableLoadingEmitter>();
        services.AddInstructionCommandProvider<CallIndirectEmitter>();
        services.AddInstructionCommandProvider<CallInstructionEmitter>();
        return services;
    }
}
