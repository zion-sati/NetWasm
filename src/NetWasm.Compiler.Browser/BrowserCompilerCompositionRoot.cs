using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetWasm.Compiler.Browser.Inputs;
using NetWasm.Compiler.Browser.Results;
using NetWasm.Compiler.Browser.Wit;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Metadata;
using NetWasm.Compiler.Metadata.ManagedExecutables;
using NetWasm.Compiler.Wasm.Units;

namespace NetWasm.Compiler.Browser;

internal static class BrowserCompilerCompositionRoot
{
    internal static BrowserCompilerComposition Create()
    {
        var registrations = new ServiceCollection().AddNetWasmCompiler();
        // Compiled units and their inputs stay in the session's memory; the host keeps them
        // across sessions by exporting and importing them.
        var units = new UnitStoreMemory();
        registrations.UseUnitMemory(units);
        registrations.AddSingleton<BrowserCompilationRequestState>();
        registrations.AddSingleton<IBrowserCompilationRequestFactory,
            BrowserCompilationRequestFactory>();
        registrations.AddSingleton<IBrowserCompilationRequestResolver,
            BrowserCompilationRequestResolver>();
        registrations.AddSingleton<IManagedAssemblyImageReader,
            VirtualManagedAssemblyImageReader>();
        registrations.AddSingleton<IWitDocumentJsonReader, WitDocumentJsonReader>();
        registrations.AddSingleton<IWitDocumentReader, VirtualWitDocumentReader>();
        registrations.AddSingleton<IBrowserCompilationResultProjector,
            BrowserCompilationResultProjector>();
        registrations.AddSingleton<IManagedExecutableEntryPointSelectorFactory,
            ManagedExecutableEntryPointSelectorFactory>();
        registrations.AddSingleton(services =>
            services.GetRequiredService<IManagedExecutableEntryPointSelectorFactory>().Create());
        registrations.AddSingleton<IBrowserCompilationPreparationFactory,
            BrowserCompilationPreparationFactory>();
        registrations.AddSingleton<IBrowserCompilationCommand, BrowserCompilationCommand>();
        var services = registrations.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
        return new(
            services,
            services.GetRequiredService<IBrowserCompilationRequestFactory>(),
            services.GetRequiredService<IBrowserCompilationPreparationFactory>(),
            services.GetRequiredService<IBrowserCompilationCommand>())
        {
            Units = units,
        };
    }
}

internal sealed class BrowserCompilerComposition(
    IDisposable services,
    IBrowserCompilationRequestFactory requests,
    IBrowserCompilationPreparationFactory preparations,
    IBrowserCompilationCommand compiler)
{
    internal IDisposable Services { get; } = services ??
        throw new ArgumentNullException(nameof(services));
    internal UnitStoreMemory Units { get; init; } = new();
    internal IBrowserCompilationRequestFactory Requests { get; } = requests ??
        throw new ArgumentNullException(nameof(requests));
    internal IBrowserCompilationPreparationFactory Preparations { get; } = preparations ??
        throw new ArgumentNullException(nameof(preparations));
    internal IBrowserCompilationCommand Compiler { get; } = compiler ??
        throw new ArgumentNullException(nameof(compiler));
}
