namespace NetWasm.Compiler.StackTraces;

internal interface ISourceDocumentIdentityFormatter
{
    string Format(string document, CompilerOptions options);
}
