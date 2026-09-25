namespace NetWasm.Compiler.Tests.Correctness;

internal interface ICorpusApplicationCompiler
{
    CorpusCompilerResponse Compile(
        CorpusCompilation compilation,
        CorpusCompilerRequest compilerRequest,
        CancellationToken cancellationToken = default);
}

internal sealed class CorpusApplicationCompiler(
    CompilerCorrectnessEnvironment environment,
    ICorpusCompilerRequestWriter requests,
    IQualifiedProcessRunner processes,
    IOracleOperationProgressReporter progress,
    ICorpusCompilerProcessVerifier verifier,
    ICorpusCompilerResponseReader responses) : ICorpusApplicationCompiler
{
    public CorpusCompilerResponse Compile(
        CorpusCompilation compilation,
        CorpusCompilerRequest compilerRequest,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        ArgumentNullException.ThrowIfNull(compilerRequest);
        cancellationToken.ThrowIfCancellationRequested();
        var requestPath = compilerRequest.ModulePath + ".request.json";
        var responsePath = compilerRequest.ModulePath + ".response.json";
        requests.Write(requestPath, compilerRequest);
        var request = new QualifiedProcessRequest(
            environment.DotNetPath,
            [environment.CompilerHostPath, requestPath, responsePath],
            environment.ProcessTimeout)
        {
            Progress = (completed, total) => progress.ReportCompilerPhases(
                compilation.Fixture.Name, compilerRequest.Target, completed, total),
        };
        var process = processes.Run(request, cancellationToken);
        var invocation = new CorpusCompilerInvocation(
            compilation, compilerRequest.Target, request, requestPath, responsePath,
            compilerRequest.ModulePath, compilerRequest.DiagnosticTracePath,
            compilerRequest.ReferencePaths, compilerRequest.SourcePaths);
        verifier.Verify(invocation, process);
        return responses.Read(invocation, process);
    }
}
