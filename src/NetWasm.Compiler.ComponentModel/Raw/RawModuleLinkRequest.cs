namespace NetWasm.Compiler.ComponentModel.Raw;

public sealed record RawModuleLinkRequest(
    string ApplicationModulePath,
    string RuntimeModulePath,
    string OutputPath,
    ComponentTarget Target);
