using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Instructions.Interop;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

using NetWasm.Compiler.Wasm.Emission.Instructions.Calls.ManagedCallSites;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Calls;

internal static class CallEmissionServiceCollectionExtensions
{
    public static IServiceCollection AddWasmCallEmission(this IServiceCollection services)
    {
        services.AddManagedCallSiteResolution();
        services.AddSingleton<IConstrainedReferenceReceiverEmitter,
            ConstrainedReferenceReceiverEmitter>();
        services.AddSingleton<DelegateInvokeCallEmitter>();
        services.AddSingleton<VirtualDispatchCallEmitter>();
        services.AddSingleton<JSImportCallEmitter>();
        services.AddSingleton<AsyncJSImportCallEmitter>();
        services.AddSingleton<DirectCallEmitter>();
        services.AddSingleton<ICallEmissionKindResolver, CallEmissionKindResolver>();
        services.AddSingleton<CallEmissionRegistration>(provider => new(
            CallEmissionKind.DelegateInvoke,
            provider.GetRequiredService<DelegateInvokeCallEmitter>()));
        services.AddSingleton<CallEmissionRegistration>(provider => new(
            CallEmissionKind.VirtualDispatch,
            provider.GetRequiredService<VirtualDispatchCallEmitter>()));
        services.AddSingleton<CallEmissionRegistration>(provider => new(
            CallEmissionKind.RuntimeIntrinsic,
            provider.GetRequiredService<RuntimeIntrinsicCallEmitter>()));
        services.AddSingleton<CallEmissionRegistration>(provider => new(
            CallEmissionKind.AsyncJSImport,
            provider.GetRequiredService<AsyncJSImportCallEmitter>()));
        services.AddSingleton<CallEmissionRegistration>(provider => new(
            CallEmissionKind.JavaScriptImport,
            provider.GetRequiredService<JSImportCallEmitter>()));
        services.AddSingleton<CallEmissionRegistration>(provider => new(
            CallEmissionKind.Direct,
            provider.GetRequiredService<DirectCallEmitter>()));
        services.AddSingleton<ICallEmissionRegistry, CallEmissionRegistry>();
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
