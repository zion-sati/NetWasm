using Microsoft.Extensions.DependencyInjection;

namespace NetWasm.Compiler.Tasks.Compilation;

internal sealed class ManagedModuleCompilationSession(
    ServiceProvider services,
    IManagedModuleCompiler compiler) : IManagedModuleCompilationSession
{
    private readonly ServiceProvider _services = services ??
        throw new ArgumentNullException(nameof(services));
    private readonly IManagedModuleCompiler _compiler = compiler ??
        throw new ArgumentNullException(nameof(compiler));

    public ManagedModuleCompilation Compile(ManagedModuleCompileRequest request) =>
        _compiler.Compile(request);

    public void Dispose() => _services.Dispose();
}
