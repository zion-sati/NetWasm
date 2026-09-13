namespace NetWasm.Compiler.Diagnostics;

public interface ICompilerProgressReporter
{
    void Report(CompilerProgressStage stage);
}
