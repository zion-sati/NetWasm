namespace NetWasm.Compiler.Wasm.Emission;

public interface IManagedBoundaryFailureDispositionResolver
{
    ManagedBoundaryFailureDisposition Resolve(ManagedBoundaryKind kind);
}
