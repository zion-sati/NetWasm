using System;
using Microsoft.Extensions.DependencyInjection;

namespace NetWasm.Compiler.ComponentModel.Raw;

internal static class RawBindingServiceCollectionExtensions
{
    public static IServiceCollection AddCompilerRawBindings(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IRawCanonicalImportIdentityFormatter, RawCanonicalImportIdentityFormatter>();
        services.AddSingleton<IRawCanonicalFunctionLayoutPlanner, RawCanonicalFunctionLayoutPlanner>();
        services.AddSingleton<IRawWitFunctionLayoutBuilder, RawWitFunctionLayoutBuilder>();
        services.AddSingleton<IRawWitImportCatalogBuilder, RawWitImportCatalogBuilder>();
        services.AddSingleton<IRawImportBindingSelector, RawImportBindingSelector>();
        services.AddSingleton<IRawResourceIntrinsicLayoutPlanner, RawResourceIntrinsicLayoutPlanner>();
        services.AddSingleton<IRawWitImportLayoutBuilder>(provider => new RawWitImportLayoutBuilder(
        [
            new(typeof(RawWitImportDeclaration.Callable), new RawCallableImportLayoutBuilder(
                provider.GetRequiredService<IRawWitFunctionLayoutBuilder>())),
            new(typeof(RawWitImportDeclaration.Resource), new RawResourceImportLayoutBuilder(
                provider.GetRequiredService<IRawResourceIntrinsicLayoutPlanner>())),
        ]));
        services.AddSingleton<IRawWitBindingPlanBuilder, RawWitBindingPlanBuilder>();
        services.AddSingleton<IRawCliCoreSignatureProjector, RawCliCoreSignatureProjector>();
        services.AddSingleton<IRawFinalImportSignatureValidator, RawFinalImportSignatureValidator>();
        services.AddSingleton<IRawAdapterWriter, RawAdapterWriter>();
        services.AddSingleton<IRawModuleInspectionProcess, RawModuleInspectionProcess>();
        services.AddSingleton<IRawModuleInspectionProtocolReader, RawModuleInspectionProtocolReader>();
        services.AddSingleton<IRawModuleImportSignatureReader, RawModuleImportSignatureReader>();
        services.AddSingleton<IRawCompilerImportDeclarationBuilder, RawCompilerImportDeclarationBuilder>();
        services.AddSingleton<IRawBuildImportSourceValidator,
            RawBuildImportSourceValidator>();
        services.AddSingleton<IRawBuildImportValidator, RawBuildImportValidator>();
        services.AddSingleton<IRawDeploymentFunctionProjector,
            RawDeploymentFunctionProjector>();
        return services;
    }
}
