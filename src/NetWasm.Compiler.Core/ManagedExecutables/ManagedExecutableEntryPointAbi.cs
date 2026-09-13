namespace NetWasm.Compiler.Core.ManagedExecutables;

public enum ManagedExecutableParameterShape
{
    None,
    StringArray,
}

public enum ManagedExecutableReturnShape
{
    Void,
    ExitCode,
}

public enum ManagedExecutableCompletionShape
{
    Synchronous,
    Asynchronous,
}

public sealed record ManagedExecutableEntryPointAbi(
    ManagedExecutableParameterShape ParameterShape,
    ManagedExecutableReturnShape ReturnShape,
    ManagedExecutableCompletionShape CompletionShape =
        ManagedExecutableCompletionShape.Synchronous);
