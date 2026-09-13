namespace NetWasm.Compiler.ExceptionTypes;

public sealed record ReachableExceptionType(
    int TypeId,
    string CanonicalIdentity,
    string DisplayName,
    string AssemblyIdentity);
