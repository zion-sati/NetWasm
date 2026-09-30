namespace NetWasm.Compiler.Tasks.MsBuild;

internal interface ICompilerBuildMessageWriter
{
    void Write(string message);
}
