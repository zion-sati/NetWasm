using NetWasm.Compiler;
using NetWasm.Compiler.Diagnostics;

namespace NetWasm.Testing.CompilerHost;

internal sealed class ConsoleCompilerProgressReporter : ICompilerProgressReporter
{
    public void Report(CompilerProgressStage stage)
    {
        Console.Error.WriteLine(
            $"NETWASM_PROGRESS {(int)stage}/{(int)CompilerProgressStage.Complete}");
    }
}
