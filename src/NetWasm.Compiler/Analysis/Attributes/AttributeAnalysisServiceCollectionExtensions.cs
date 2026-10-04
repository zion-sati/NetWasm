using Microsoft.Extensions.DependencyInjection;

namespace NetWasm.Compiler.Analysis.Attributes;

internal static class AttributeAnalysisServiceCollectionExtensions
{
    public static IServiceCollection AddAttributeAnalysis(this IServiceCollection services) =>
        services.AddSingleton<IAttributeQueryMethodSpecializerFactory, AttributeQueryMethodSpecializerFactory>();
}
