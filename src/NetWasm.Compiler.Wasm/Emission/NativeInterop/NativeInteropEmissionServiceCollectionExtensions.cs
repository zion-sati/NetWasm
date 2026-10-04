using System;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.ModuleEncoding;

namespace NetWasm.Compiler.Wasm.Emission.NativeInterop;

internal static class NativeInteropEmissionServiceCollectionExtensions
{
    public static IServiceCollection AddWasmNativeInteropEmission(
        this IServiceCollection services)
    {
        services.AddSingleton<IUnsignedLeb128Encoder, UnsignedLeb128Encoder>();
        services.AddSingleton<IUtf8StringEncoder, Utf8StringEncoder>();
        services.AddSingleton<IWasmSectionWriter, WasmSectionWriter>();
        services.AddSingleton<Func<WasmBinaryBuffer, IWasmBinaryWriter>>(
            static _ => static buffer => new WasmBinaryWriter(buffer));
        services.AddSingleton<INativeCallbackObjectWriter, NativeCallbackObjectWriter>();
        return services;
    }
}
