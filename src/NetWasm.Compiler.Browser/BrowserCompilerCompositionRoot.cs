using System;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.Browser.Inputs;
using NetWasm.Compiler.Browser.Results;
using NetWasm.Compiler.Browser.Wit;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ExceptionTypes;
using NetWasm.Compiler.Metadata;
using NetWasm.Compiler.Metadata.ManagedExecutables;

namespace NetWasm.Compiler.Browser;

internal static class BrowserCompilerCompositionRoot
{
    internal static BrowserCompilerComposition Create()
    {
        var registrations = new ServiceCollection().AddNetWasmCompiler();
        registrations.AddSingleton<BrowserCompilationRequestState>();
        registrations.AddSingleton<IBrowserCompilationRequestFactory,
            BrowserCompilationRequestFactory>();
        registrations.AddSingleton<IBrowserCompilationRequestResolver,
            BrowserCompilationRequestResolver>();
        registrations.AddSingleton<IManagedAssemblyImageReader,
            VirtualManagedAssemblyImageReader>();
        registrations.AddSingleton<ICompilationInputHasher,
            VirtualCompilationInputHasher>();
        registrations.AddSingleton<IWitDocumentJsonReader, WitDocumentJsonReader>();
        registrations.AddSingleton<IWitDocumentReader, VirtualWitDocumentReader>();
        registrations.AddSingleton<IBrowserCompilationResultProjector,
            BrowserCompilationResultProjector>();
        registrations.AddSingleton<IManagedExecutableEntryPointSelector,
            ManagedExecutableEntryPointSelector>();
        registrations.AddSingleton<IBrowserCompilationCommand, BrowserCompilationCommand>();
        var services = registrations.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
        return new(
            services,
            services.GetRequiredService<IBrowserCompilationRequestFactory>(),
            services.GetRequiredService<IBrowserCompilationCommand>());
    }
}

internal sealed class BrowserCompilerComposition(
    IDisposable services,
    IBrowserCompilationRequestFactory requests,
    IBrowserCompilationCommand compiler)
{
    internal IDisposable Services { get; } = services ??
        throw new ArgumentNullException(nameof(services));
    internal IBrowserCompilationRequestFactory Requests { get; } = requests ??
        throw new ArgumentNullException(nameof(requests));
    internal IBrowserCompilationCommand Compiler { get; } = compiler ??
        throw new ArgumentNullException(nameof(compiler));
}
