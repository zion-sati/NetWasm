using System;
using Microsoft.Extensions.DependencyInjection;

namespace NetWasm.Compiler.Wasm.Units;

internal static class CompiledUnitServiceCollectionExtensions
{
    // Registers compiled units off; a compile that keeps units replaces the state with its own.
    public static IServiceCollection AddCompiledUnits(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton(new CompiledUnitState(null));
        services.AddSingleton<IWasmIndexOperandScanner, WasmIndexOperandScanner>();
        services.AddSingleton<IWasmFunctionSymbolBuilder, WasmFunctionSymbolBuilder>();
        services.AddSingleton<ICompiledUnitSymbolBinder, CompiledUnitSymbolBinder>();
        services.AddSingleton<ICompiledUnitCapturer, CompiledUnitCapturer>();
        services.AddSingleton<ICompiledUnitPublisher, CompiledUnitPublisher>();
        return services;
    }
}
