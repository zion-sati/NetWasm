using Microsoft.Extensions.DependencyInjection;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Interop;

internal static class JavaScriptInteropEmissionServiceCollectionExtensions
{
    public static IServiceCollection AddWasmJavaScriptInteropEmission(
        this IServiceCollection services)
    {
        services.AddSingleton<IJavaScriptResultHandleCapturer,
            JavaScriptResultHandleCapturer>();
        services.AddSingleton<IInteropHandleReleaser, InteropHandleReleaser>();
        services.AddSingleton<IJavaScriptResultLengthValidator,
            JavaScriptResultLengthValidator>();
        services.AddSingleton<IAllocationResultValidator,
            AllocationResultValidator>();
        services.AddSingleton<IStackLocalResolver, StackLocalResolver>();
        services.AddSingleton<IOptionalFunctionIndexValidator,
            OptionalFunctionIndexValidator>();
        services.AddSingleton<StringJavaScriptImportResultMarshaller>();
        services.AddSingleton<ByteArrayJavaScriptImportResultMarshaller>();
        services.AddSingleton<HostObjectJavaScriptImportResultMarshaller>();
        services.AddSingleton<ScalarJavaScriptImportResultMarshaller>();
        services.AddSingleton<IJavaScriptImportResultKindResolver,
            JavaScriptImportResultKindResolver>();
        services.AddSingleton(provider => new JavaScriptImportResultEmitterRegistration(
            JavaScriptImportResultKind.String,
            provider.GetRequiredService<StringJavaScriptImportResultMarshaller>()));
        services.AddSingleton(provider => new JavaScriptImportResultEmitterRegistration(
            JavaScriptImportResultKind.ByteArray,
            provider.GetRequiredService<ByteArrayJavaScriptImportResultMarshaller>()));
        services.AddSingleton(provider => new JavaScriptImportResultEmitterRegistration(
            JavaScriptImportResultKind.HostObject,
            provider.GetRequiredService<HostObjectJavaScriptImportResultMarshaller>()));
        services.AddSingleton(provider => new JavaScriptImportResultEmitterRegistration(
            JavaScriptImportResultKind.Scalar,
            provider.GetRequiredService<ScalarJavaScriptImportResultMarshaller>()));
        services.AddSingleton<IJavaScriptImportResultEmitterRegistry,
            JavaScriptImportResultEmitterRegistry>();
        services.AddSingleton<JavaScriptImportResultMarshaller>();
        services.AddSingleton<IJavaScriptImportResultEmitter>(provider =>
            provider.GetRequiredService<JavaScriptImportResultMarshaller>());
        services.AddSingleton<IJavaScriptResultDescriptorSizeResolver,
            JavaScriptResultDescriptorSizeResolver>();
        services.AddSingleton<IJavaScriptImportParameterTypeResolver,
            JavaScriptImportParameterTypeResolver>();
        services.AddSingleton<IJavaScriptImportArgumentEmitter,
            JavaScriptImportArgumentEmitter>();
        services.AddSingleton<IHostCallbackHandleEmitter,
            HostCallbackHandleEmitter>();
        services.AddSingleton<IHostCallbackStringArgumentMarshaller,
            HostCallbackStringArgumentMarshaller>();
        services.AddSingleton<IHostCallbackByteArrayArgumentMarshaller,
            HostCallbackByteArrayArgumentMarshaller>();
        return services;
    }
}
