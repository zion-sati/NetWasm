using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.Emission;
using NetWasm.Compiler.Diagnostics;

namespace NetWasm.Compiler.Tests;

public sealed class DiagnosticsServiceCollectionExtensionsTests
{
    [Fact]
    public void RegistrationRejectsANullServiceCollection()
    {
        IServiceCollection? services = null;

        Assert.Throws<ArgumentNullException>(() =>
            services!.AddCompilerDiagnostics());
    }

    [Fact]
    public void RegistrationReturnsTheOriginalServiceCollection()
    {
        var services = new ServiceCollection();

        Assert.Same(services, services.AddCompilerDiagnostics());
    }

    [Fact]
    public void DiagnosticsRegistrationOverridesThePlanningNullLogger()
    {
        var services = new ServiceCollection();
        services.AddCompilerEmission();
        services.AddCompilerDiagnostics();

        using var provider = services.BuildServiceProvider();
        var logger = provider.GetRequiredService<global::Microsoft.Extensions.Logging.ILogger>();

        Assert.IsType<Logger<CompilerDiagnosticCompilationDecorator>>(logger);
        Assert.IsType<CompilerDiagnosticFileLoggerProvider>(
            Assert.Single(provider.GetServices<ILoggerProvider>()));
    }

    [Fact]
    public void DiagnosticsRegistrationUsesTheDiagnosticProgressReporter()
    {
        var services = new ServiceCollection();
        services.AddCompilerDiagnostics();

        using var provider = services.BuildServiceProvider();
        var reporter = provider.GetRequiredService<ICompilerProgressReporter>();

        Assert.IsType<CompilerDiagnosticProgressReporter>(reporter);
    }

}
