using System;
using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetWasm.Compiler.Browser.Wit;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Catalogs;
using NetWasm.Compiler.ComponentModel.Raw;

namespace NetWasm.Compiler.Browser;

public sealed record BrowserRawBindingResult(
    byte[] Adapter,
    ImmutableArray<WitInterfaceFunction> RequiredImports);

/// <summary>
/// Produces the same validated raw-module adapter as the desktop build while
/// leaving Wasm inspection to the browser host.
/// </summary>
public static class BrowserRawBindings
{
    private static readonly RawModuleInspectionRequest RuntimeInspection = new(
        "browser", "browser", "browser-runtime", "browser");
    private static readonly RawModuleInspectionRequest FinalInspection = new(
        "browser", "browser", "browser-final", "browser");

    public static BrowserRawBindingResult Build(BrowserRawBindingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var imports = new SuppliedImportSignatureReader(
            request.RuntimeImports,
            request.FinalImports);
        var registrations = new ServiceCollection().AddNetWasmCompiler();
        registrations.RemoveAll<IWitDocumentReader>();
        registrations.AddSingleton<IWitDocumentReader>(new VirtualWitDocumentReader(
            request.NormalizedWitDocuments,
            request.WitCoreBindingInventories,
            new WitDocumentJsonReader()));
        registrations.RemoveAll<IRawModuleImportSignatureReader>();
        registrations.AddSingleton<IRawModuleImportSignatureReader>(imports);

        using var services = registrations.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
        var validation = services.GetRequiredService<IRawBuildImportSourceValidator>()
            .Validate(new(
                request.Source,
                request.WitPath,
                request.World,
                request.Target,
                RuntimeInspection,
                FinalInspection)
            {
                RuntimeWitPath = request.RuntimeWitPath,
                RuntimeWorld = request.RuntimeWorld,
            });
        var projection = services.GetRequiredService<IRawDeploymentFunctionProjector>()
            .Project(validation, request.RuntimeWitPath, request.RuntimeWorld);
        var adapter = services.GetRequiredService<IRawAdapterWriter>()
            .WriteDeployment(new(projection.AdapterPlans));
        imports.RequireComplete();
        return new(adapter, projection.RequiredImports);
    }

    private sealed class SuppliedImportSignatureReader(
        ImmutableArray<RawCoreFunctionImportSignature> runtime,
        ImmutableArray<RawCoreFunctionImportSignature> final) :
        IRawModuleImportSignatureReader
    {
        private bool _runtimeRead;
        private bool _finalRead;

        public ImmutableArray<RawCoreFunctionImportSignature> Read(
            RawModuleInspectionRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (request == RuntimeInspection && !_runtimeRead)
            {
                _runtimeRead = true;
                return runtime;
            }
            if (request == FinalInspection && !_finalRead)
            {
                _finalRead = true;
                return final;
            }
            throw new InvalidOperationException(
                "Raw binding import signatures were requested out of sequence.");
        }

        public void RequireComplete()
        {
            if (!_runtimeRead || !_finalRead)
            {
                throw new InvalidOperationException(
                    "Raw binding validation did not consume both supplied import sets.");
            }
        }
    }
}
