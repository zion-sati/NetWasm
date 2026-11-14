namespace NetWasm.Compiler.Core.NativeInterop;

public interface INativeImportBinder
{
    // Whether a native import binds to a library the program links, or compiles to a throwing body.
    bool Binds(NativeImportDeclaration import);
}
