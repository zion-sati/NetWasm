using System;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Loading;

internal static class LoadingServiceCollectionExtensions
{
    public static IServiceCollection AddCompilerLoading(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IManagedAssemblyImageReader, ManagedAssemblyImageReader>();
        services.AddSingleton<IManagedAssemblyLoader, ManagedAssemblyLoader>();
        services.AddSingleton<IValueTypeDefinitionStackKindResolver, ValueTypeDefinitionStackKindResolver>();
        services.AddSingleton<IMetadataCompilationMaterializationFactory,
            MetadataCompilationMaterializationFactory>();
        services.AddSingleton<ITypeRepositoryFactory, MetadataTypeRepositoryFactory>();
        services.AddSingleton<IFieldRepositoryFactory, MetadataFieldRepositoryFactory>();
        services.AddSingleton<IMethodRepositoryFactory, MetadataMethodRepositoryFactory>();
        services.AddSingleton<ITypeFinderFactory, MetadataTypeFinderFactory>();
        services.AddSingleton<ITypeDefinitionResolverFactory,
            MetadataTypeDefinitionResolverFactory>();
        services.AddSingleton<ITypeIdentityResolverFactory, MetadataTypeIdentityResolverFactory>();
        services.AddSingleton<IMethodInstanceResolverFactory,
            MetadataMethodInstanceResolverFactory>();
        services.AddSingleton<IMetadataEntityBaseTypeResolverFactory,
            MetadataEntityBaseTypeResolverFactory>();
        services.AddSingleton<IMetadataIdentityBaseTypeResolverFactory,
            MetadataIdentityBaseTypeResolverFactory>();
        services.AddSingleton<IBaseTypeIdentityResolverFactory,
            MetadataBaseTypeIdentityResolverFactory>();
        services.AddSingleton<ITypeClassifierFactory, MetadataTypeClassifierFactory>();
        services.AddSingleton<IImplementedInterfaceResolverFactory,
            MetadataImplementedInterfaceResolverFactory>();
        services.AddSingleton<IMethodImplementationResolverFactory,
            MetadataMethodImplementationResolverFactory>();
        services.AddSingleton<IMethodBodyReaderFactory, MetadataMethodBodyReaderFactory>();
        services.AddSingleton<IMethodFinderFactory, MetadataMethodFinderFactory>();
        services.AddSingleton<ISymbolFormatterFactory, MetadataSymbolFormatterFactory>();
        services.AddSingleton<IReferenceClosureValidator, ReferenceClosureValidator>();
        services.AddSingleton<IMetadataCompilationFactory, MetadataCompilationFactory>();
        services.AddSingleton<IMetadataCompilationLoader, MetadataCompilationLoader>();
        services.AddSingleton<IAssemblyIdentityFormatterFactory,
            AssemblyIdentityFormatterFactory>();
        return services;
    }
}
