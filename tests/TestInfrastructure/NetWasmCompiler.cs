using Microsoft.Extensions.DependencyInjection;

namespace NetWasm.Compiler;

// Compiles with a compiler of its own, as each test wants: the CLI, build tasks
// and browser compiler compose theirs from the same registration.
internal static class NetWasmCompiler
{
    public static CompilationResult Compile(CompilerOptions options)
    {
        using var services = new ServiceCollection()
            .AddNetWasmCompiler()
            .BuildServiceProvider();
        return services.GetRequiredService<INetWasmCompiler>().Compile(options);
    }
}
