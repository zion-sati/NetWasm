namespace NetWasm.Compiler.Diagnostics;

internal interface ICompilerReproductionCommandBuilder
{
    string Build(CompilerOptions options);
}
