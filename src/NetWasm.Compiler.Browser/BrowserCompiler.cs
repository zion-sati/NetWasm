using System;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.Browser.Inputs;
using NetWasm.Compiler.Browser.Results;
using NetWasm.Compiler.Browser.Wit;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ExceptionTypes;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Browser;

/// <summary>
/// Composes the compiler with caller-owned virtual inputs. CompilerException
/// diagnostics propagate to the caller without translation.
/// </summary>
public static class BrowserCompiler
{
    public static BrowserCompilationResult Compile(BrowserCompilationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var registrations = new ServiceCollection().AddNetWasmCompiler();
        registrations.AddSingleton<IManagedAssemblyImageReader>(
            new VirtualManagedAssemblyImageReader(request.Inputs));
        registrations.AddSingleton<ICompilationInputHasher>(
            new VirtualCompilationInputHasher(request.Inputs));
        registrations.AddSingleton<IWitDocumentReader>(
            new VirtualWitDocumentReader(request.NormalizedWitDocuments, new WitDocumentJsonReader()));
        registrations.AddSingleton<IBrowserCompilationResultProjector, BrowserCompilationResultProjector>();
        using var services = registrations.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
        var result = services.GetRequiredService<INetWasmCompiler>().Compile(request.Options);
        return services.GetRequiredService<IBrowserCompilationResultProjector>().Project(result, request.Options);
    }
}
