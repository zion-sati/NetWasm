// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

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
        return WithReports(responses.Read(responsePath, request, typeNames), result.StandardError);
    }

    // A program that traps has often said why on its standard error first, as the runtime's checks do (the
    // collector's check of write barriers names the store it caught), and a trap carries nothing else to show.
    private static LinkedCorpusObservationResult WithReports(LinkedCorpusObservationResult result, string standardError)
    {
        var reported = standardError.Trim();
        if (reported.Length == 0)
        {
            return result;
        }
        if (reported.Length > 4000)
        {
            reported = "..." + reported[^4000..];
        }
        return result with
        {
            Observations = result.Observations.ToImmutableDictionary(
                pair => pair.Key,
                pair => pair.Value.Kind == OracleObservationKind.Trap && pair.Value.Detail is null
                    ? pair.Value with { Detail = reported }
                    : pair.Value),
        };
    }
}
