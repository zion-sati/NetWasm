using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace NetWasm.Compiler.ControlFlow.Draft;

internal sealed class ExceptionGroupCollector : IExceptionGroupCollector
{
    public ImmutableArray<StructuredExceptionGroupDraft> Collect(StructuredMethodDraft method)
    {
        ArgumentNullException.ThrowIfNull(method);

        var seen = new HashSet<StructuredExceptionGroupDraft>(ReferenceEqualityComparer.Instance);
        var groups = ImmutableArray.CreateBuilder<StructuredExceptionGroupDraft>();

        foreach (var exceptionGroup in method.ExceptionGroups)
        {
            AddGroup(exceptionGroup);
        }

        AddSequence(method.Body);
        return groups.ToImmutable();

        void AddGroup(StructuredExceptionGroupDraft exceptionGroup)
        {
            if (!seen.Add(exceptionGroup))
            {
                return;
            }

            groups.Add(exceptionGroup);
            foreach (var part in exceptionGroup.ProtectedParts)
            {
                switch (part)
                {
                    case StructuredExceptionCodeDraft code:
                        AddSequence(code.Body);
                        break;
                    case StructuredNestedExceptionGroupDraft nested:
                        AddGroup(nested.Group);
                        break;
                }
            }

            foreach (var clause in exceptionGroup.Clauses)
            {
                AddSequence(clause.HandlerBody);
                if (clause.FilterBody is not null)
                {
                    AddSequence(clause.FilterBody);
                }
            }

            foreach (var continuation in exceptionGroup.NormalContinuations)
            {
                AddSequence(continuation.Body);
            }

            if (exceptionGroup.ContinuationDispatcher is not null)
            {
                AddSequence(new StructuredSequenceDraft([exceptionGroup.ContinuationDispatcher]));
            }
        }

        void AddSequence(StructuredSequenceDraft sequence)
        {
            foreach (var region in sequence.Regions)
            {
                switch (region)
                {
                    case StructuredExceptionRegionDraft exception:
                        AddGroup(exception.Group);
                        break;
                    case StructuredIfDraft conditional:
                        AddSequence(conditional.WhenTrue);
                        AddSequence(conditional.WhenFalse);
                        break;
                    case StructuredLoopDraft loop:
                        AddSequence(loop.Body);
                        AddSequence(loop.ContinueBody);
                        AddSequence(loop.ExitBody);
                        break;
                    case StructuredPostTestLoopDraft loop:
                        AddSequence(loop.Body);
                        AddSequence(loop.ContinueBody);
                        AddSequence(loop.ExitBody);
                        break;
                    case StructuredDispatcherDraft dispatcher:
                        foreach (var exit in dispatcher.Exits)
                        {
                            AddSequence(exit.Body);
                        }
                        break;
                }
            }
        }
    }
}
