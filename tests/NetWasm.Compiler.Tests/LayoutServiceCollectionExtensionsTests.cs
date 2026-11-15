// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.Layout;

namespace NetWasm.Compiler.Tests;

public sealed class LayoutServiceCollectionExtensionsTests
{
    [Fact]
    public void AddCompilerLayoutRegistersTheCompleteLayoutGraph()
    {
        var services = new ServiceCollection();

        var result = services.AddCompilerLayout();

        Assert.Same(services, result);

        var serviceTypes = services.Select(static descriptor => descriptor.ServiceType).ToHashSet();
        Type[] expected =
        [
            typeof(IExplicitValueLayoutResolver),
            typeof(IValueLayoutResolverFactory),
            typeof(IValueLayoutProviderFactory),
            typeof(IManagedTypeLayoutCompilerFactory),
            typeof(IManagedObjectLayoutBuilderFactory),
            typeof(IExceptionTypeNameResolver),
            typeof(IManagedStaticDataBuilderFactory),
            typeof(IManagedLayoutCompiler),
            typeof(ITypeLayoutProviderFactory),
            typeof(IInstanceFieldLayoutProviderFactory),
            typeof(IStaticFieldLayoutProviderFactory),
            typeof(IStaticDataLayoutProviderFactory),
            typeof(IMemberDescriptorLayoutProviderFactory),
            typeof(IManagedExceptionObjectProviderFactory),
        ];

        Assert.All(expected, serviceType => Assert.Contains(serviceType, serviceTypes));
    }
}
