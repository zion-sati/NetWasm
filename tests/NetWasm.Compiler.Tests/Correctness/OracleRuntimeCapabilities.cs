namespace NetWasm.Compiler.Tests.Correctness;

[Flags]
internal enum OracleRuntimeCapabilities
{
    None = 0,
    GarbageCollection = 1,
    Finalization = 2,
    WeakReferenceClearing = 4,
}
