namespace NetWasm.Compiler.ComponentModel.Raw;

using NetWasm.Compiler.ComponentModel;

public sealed record RawModuleLinkRequest(
    string ApplicationModulePath,
    string RuntimeModulePath,
    string OutputPath,
    ComponentTarget Target,
    FinalWasmOptimization Optimization = FinalWasmOptimization.Size);
