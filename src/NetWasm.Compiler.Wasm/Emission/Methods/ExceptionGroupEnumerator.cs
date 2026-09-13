using System;
using System.Collections.Generic;
using NetWasm.Compiler.ControlFlow.Structured;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal sealed class ExceptionGroupEnumerator : IExceptionGroupEnumerator
{
    public IEnumerable<StructuredExceptionGroupId> Enumerate(StructuredMethod method)
    {
        var seen = new HashSet<StructuredExceptionGroupId>();
        foreach (var group in method.TopLevelExceptionGroups)
        {
            foreach (var nested in Enumerate(group, method, seen))
            {
                yield return nested;
            }
        }
    }

    private static IEnumerable<StructuredExceptionGroupId> Enumerate(
        StructuredExceptionGroupId id,
        StructuredMethod method,
        HashSet<StructuredExceptionGroupId> seen)
    {
        if (!seen.Add(id))
        {
            yield break;
        }
        if (!method.ExceptionGroups.TryGetValue(id, out var group))
        {
            throw new InvalidOperationException(
                $"structured exception group '{id.Value}' is not defined");
        }

        yield return id;
        foreach (var part in group.ProtectedParts)
        {
            switch (part)
            {
                case StructuredNestedExceptionGroup nested:
                    foreach (var descendant in Enumerate(nested.Group, method, seen))
                    {
                        yield return descendant;
                    }
                    break;
                case StructuredExceptionCode code:
                    foreach (var descendant in EnumerateSequence(code.Body, method, seen))
                    {
                        yield return descendant;
                    }
                    break;
            }
        }
        foreach (var clause in group.Clauses)
        {
            foreach (var descendant in EnumerateSequence(clause.HandlerBody, method, seen))
            {
                yield return descendant;
            }
            if (clause.FilterBody is not { } filterBody)
            {
                continue;
            }
            foreach (var descendant in EnumerateSequence(filterBody, method, seen))
            {
                yield return descendant;
            }
        }
        foreach (var continuation in group.NormalContinuations)
        {
            foreach (var descendant in EnumerateSequence(continuation.Body, method, seen))
            {
                yield return descendant;
            }
        }
        if (group.ContinuationDispatcher is { } dispatcher)
        {
            foreach (var descendant in EnumerateSequence(
                         new StructuredSequence([dispatcher]),
                         method,
                         seen))
            {
                yield return descendant;
            }
        }
    }

    private static IEnumerable<StructuredExceptionGroupId> EnumerateSequence(
        StructuredSequence sequence,
        StructuredMethod method,
        HashSet<StructuredExceptionGroupId> seen)
    {
        foreach (var region in sequence.Regions)
        {
            switch (region)
            {
                case StructuredExceptionRegion exception:
                    foreach (var nested in Enumerate(exception.Group, method, seen))
                    {
                        yield return nested;
                    }
                    break;
                case StructuredIf conditional:
                    foreach (var nested in EnumerateSequence(conditional.WhenTrue, method, seen))
                    {
                        yield return nested;
                    }
                    foreach (var nested in EnumerateSequence(conditional.WhenFalse, method, seen))
                    {
                        yield return nested;
                    }
                    break;
                case StructuredLoop loop:
                    foreach (var nested in EnumerateSequence(loop.Body, method, seen))
                    {
                        yield return nested;
                    }
                    foreach (var nested in EnumerateSequence(loop.ContinueBody, method, seen))
                    {
                        yield return nested;
                    }
                    foreach (var nested in EnumerateSequence(loop.ExitBody, method, seen))
                    {
                        yield return nested;
                    }
                    break;
                case StructuredPostTestLoop loop:
                    foreach (var nested in EnumerateSequence(loop.Body, method, seen))
                    {
                        yield return nested;
                    }
                    foreach (var nested in EnumerateSequence(loop.ContinueBody, method, seen))
                    {
                        yield return nested;
                    }
                    foreach (var nested in EnumerateSequence(loop.ExitBody, method, seen))
                    {
                        yield return nested;
                    }
                    break;
                case StructuredDispatcher dispatcher:
                    foreach (var exit in dispatcher.Exits)
                    {
                        foreach (var nested in EnumerateSequence(exit.Body, method, seen))
                        {
                            yield return nested;
                        }
                    }
                    break;
            }
        }
    }
}
