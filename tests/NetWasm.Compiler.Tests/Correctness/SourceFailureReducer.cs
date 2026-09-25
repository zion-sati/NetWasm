using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NetWasm.Compiler.Tests.Correctness;

internal sealed record SourceReductionRequest(
    string Source,
    Func<string, bool> PreservesFailure,
    TimeSpan Deadline);

internal sealed record SourceReductionResult(
    string Original,
    string Reduced,
    int Attempts,
    bool Completed)
{
    public bool Changed => !StringComparer.Ordinal.Equals(Original, Reduced);
}

internal interface ISourceFailureReducer
{
    SourceReductionResult Reduce(SourceReductionRequest request);
}

internal sealed class SourceFailureReducer(TimeProvider timeProvider) :
    ISourceFailureReducer
{
    public SourceReductionResult Reduce(SourceReductionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Source);
        ArgumentNullException.ThrowIfNull(request.PreservesFailure);
        if (request.Deadline <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }

        var started = timeProvider.GetTimestamp();
        var best = Parse(request.Source);
        var attempts = 0;
        var changed = true;
        while (changed && !Expired())
        {
            changed = false;
            foreach (var candidate in Candidates(best))
            {
                if (Expired())
                {
                    return Result(completed: false);
                }
                attempts++;
                var source = candidate.NormalizeWhitespace().ToFullString();
                if (!request.PreservesFailure(source))
                {
                    continue;
                }
                best = candidate;
                changed = true;
                break;
            }
        }
        return Result(completed: !Expired());

        bool Expired() => timeProvider.GetElapsedTime(started) >= request.Deadline;

        SourceReductionResult Result(bool completed) => new(
            request.Source,
            best.NormalizeWhitespace().ToFullString(),
            attempts,
            completed);
    }

    private static CompilationUnitSyntax Parse(string source)
    {
        var root = CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot();
        if (root.ContainsDiagnostics)
        {
            throw new ArgumentException("source reduction requires valid C# syntax");
        }
        return root;
    }

    private static IEnumerable<CompilationUnitSyntax> Candidates(
        CompilationUnitSyntax root)
    {
        foreach (var member in root.DescendantNodes()
                     .OfType<MemberDeclarationSyntax>()
                     .Where(member => member.Parent is TypeDeclarationSyntax)
                     .Reverse())
        {
            yield return root.RemoveNode(member, SyntaxRemoveOptions.KeepNoTrivia)!;
        }
        foreach (var statement in root.DescendantNodes()
                     .OfType<StatementSyntax>()
                     .Where(statement => statement.Parent is BlockSyntax)
                     .Reverse())
        {
            yield return root.RemoveNode(statement, SyntaxRemoveOptions.KeepNoTrivia)!;
        }
    }
}
