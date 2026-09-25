using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

internal interface ILinkedCorpusObservationProcess
{
    LinkedCorpusObservationResult Observe(
        LinkedCorpusObservationRequest request,
        ImmutableDictionary<int, string> typeNames,
        LinkedCorpusObservationEnvironment environment,
        CancellationToken cancellationToken = default);
}

internal sealed class LinkedCorpusObservationProcess(
    ILinkedCorpusObservationRequestWriter requests,
    IQualifiedProcessRunner processes,
    ILinkedCorpusObservationResponseReader responses) : ILinkedCorpusObservationProcess
{
    public LinkedCorpusObservationResult Observe(
        LinkedCorpusObservationRequest request,
        ImmutableDictionary<int, string> typeNames,
        LinkedCorpusObservationEnvironment environment,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(typeNames);
        ArgumentNullException.ThrowIfNull(environment);
        cancellationToken.ThrowIfCancellationRequested();
        var requestPath = request.ModulePath + ".observation.request.json";
        var responsePath = request.ModulePath + ".observation.response.json";
        requests.Write(requestPath, request);
        var processRequest = new QualifiedProcessRequest(
            environment.NodePath,
            [environment.RunnerPath, requestPath, responsePath],
            environment.Timeout);
        var result = processes.Run(processRequest, cancellationToken);
        var invocation = new LinkedCorpusObservationInvocation(
            request, processRequest, requestPath, responsePath);
        if (!result.Succeeded)
        {
            throw new LinkedCorpusObservationException(invocation, result);
        }
        return responses.Read(responsePath, request, typeNames);
    }
}
