// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using Microsoft.Extensions.DependencyInjection;

namespace NetWasm.Compiler.Layout;

internal static class LayoutServiceCollectionExtensions
{
    public static IServiceCollection AddCompilerLayout(this IServiceCollection services)
    {
        services.AddSingleton<NetWasm.Compiler.Core.IManagedLayoutForkSourceFactory,
            ManagedLayoutForkSourceFactory>();
        services.AddSingleton<IExplicitValueLayoutResolver,
            ExplicitValueLayoutResolver>();
        services.AddSingleton<IValueLayoutResolverFactory,
            ValueLayoutResolverFactory>();
        services.AddSingleton<IValueLayoutProviderFactory,
            ValueLayoutProviderFactory>();
        services.AddSingleton<IManagedTypeLayoutCompilerFactory,
            ManagedTypeLayoutCompilerFactory>();
        services.AddSingleton<IManagedObjectLayoutBuilderFactory,
            ManagedObjectLayoutBuilderFactory>();
        services.AddSingleton<IExceptionTypeNameResolver, ExceptionTypeNameResolver>();
        services.AddSingleton<IManagedStaticDataBuilderFactory,
            ManagedStaticDataBuilderFactory>();
        services.AddSingleton<IAssignableTypeMetadataBuilderFactory,
            AssignableTypeMetadataBuilderFactory>();
        services.AddSingleton<IManagedLayoutCompiler, ManagedLayoutCompiler>();
        services.AddSingleton<ITypeLayoutProviderFactory, TypeLayoutProviderFactory>();
        services.AddSingleton<IInstanceFieldLayoutProviderFactory,
            InstanceFieldLayoutProviderFactory>();
        services.AddSingleton<IStaticFieldLayoutProviderFactory,
            StaticFieldLayoutProviderFactory>();
        services.AddSingleton<IStaticDataLayoutProviderFactory,
            StaticDataLayoutProviderFactory>();
        services.AddSingleton<IMemberDescriptorLayoutProviderFactory,
            MemberDescriptorLayoutProviderFactory>();
        services.AddSingleton<IManagedExceptionObjectProviderFactory,
            ManagedExceptionObjectProviderFactory>();
        return services;
    }
}
