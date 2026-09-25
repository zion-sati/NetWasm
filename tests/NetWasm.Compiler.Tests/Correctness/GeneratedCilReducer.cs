using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

internal sealed class GeneratedCilReducer(
    IEnumerable<IGeneratedCilReductionPass> passes,
    IGeneratedCilValidator validator,
    TimeProvider timeProvider) : IGeneratedCilReducer
{
    private readonly ImmutableArray<IGeneratedCilReductionPass> _passes =
        [.. passes];

    public GeneratedCilReductionResult Reduce(GeneratedCilReductionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Oracle);
        if (request.Timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }
        Validate(request.Original);
        using var deadline = new CancellationTokenSource(request.Timeout);
        var started = timeProvider.GetTimestamp();
        var best = request.Original;
        var bestScore = GeneratedCilReductionScore.Measure(best);
        var attempts = 0;
        var applied = ImmutableArray.CreateBuilder<string>();
        var improved = true;
        while (improved && !deadline.IsCancellationRequested)
        {
            improved = false;
            foreach (var pass in _passes)
            {
                foreach (var candidate in pass.Generate(best))
                {
                    if (deadline.IsCancellationRequested ||
                        timeProvider.GetElapsedTime(started) >= request.Timeout)
                    {
                        return Result(timedOut: true);
                    }
                    var score = GeneratedCilReductionScore.Measure(candidate);
                    if (score.CompareTo(bestScore) >= 0 || !IsValid(candidate))
                    {
                        continue;
                    }
                    attempts++;
                    var observed = request.Oracle(candidate, deadline.Token);
                    if (observed != request.Fingerprint)
                    {
                        continue;
                    }
                    best = candidate;
                    bestScore = score;
                    applied.Add(pass.Name);
                    improved = true;
                    break;
                }
                if (improved || deadline.IsCancellationRequested)
                {
                    break;
                }
            }
        }
        return Result(deadline.IsCancellationRequested);

        GeneratedCilReductionResult Result(bool timedOut) => new(
            request.Original,
            best,
            request.Fingerprint,
            timedOut,
            attempts,
            applied.ToImmutable());
    }

    private bool IsValid(GeneratedCilReductionCase candidate)
    {
        try
        {
            Validate(candidate);
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private void Validate(GeneratedCilReductionCase candidate)
    {
        validator.Validate(candidate.Program);
        foreach (var method in candidate.SupportingMethods)
        {
            validator.Validate(method);
        }
    }
}
