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
        registrations.AddSingleton<IManagedExecutableEntryPointSelector, ManagedExecutableEntryPointSelector>();
        using var services = registrations.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
        var options = request.Options;
        var entries = services.GetRequiredService<IManagedExecutableEntryPointSelector>();
        var image = options.EntryPointKind == CompilerEntryPointKind.ManagedExecutable
            ? services.GetRequiredService<IManagedAssemblyImageReader>().Read(options.EntryAssemblyPath)
            : null;
        if (request.SelectManagedExecutableEntryPoint)
        {
            var selected = entries.SelectEntryPoint(options.EntryAssemblyPath, image!);
            options = options with
            {
                EntryTypeName = selected.TypeName,
                EntryMethodName = selected.MethodName,
                EntryMethodToken = selected.MetadataToken,
            };
        }

        var result = services.GetRequiredService<INetWasmCompiler>().Compile(options);
        var actualEntry = image is null ? null : entries.SelectEntryPoint(
            options.EntryAssemblyPath, image, result.Program.EntryPoint.Key.MetadataToken);
        if (actualEntry is not null)
        {
            options = options with
            {
                EntryTypeName = actualEntry.TypeName,
                EntryMethodName = actualEntry.MethodName,
                EntryMethodToken = actualEntry.MetadataToken,
            };
        }

        return services.GetRequiredService<IBrowserCompilationResultProjector>().Project(
            result, options, actualEntry?.Abi);
    }
}
