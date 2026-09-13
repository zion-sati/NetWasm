using System;
using System.Collections.Generic;
using System.Linq;

namespace NetWasm.Compiler.ControlFlow.Draft;

internal sealed class ExceptionRegionOwnershipProjectorDraft(
    IExceptionGroupCollector exceptionGroups,
    IExceptionGroupParentMapBuilder exceptionGroupParents) :
    IExceptionRegionOwnershipProjectorDraft
{
    public StructuredMethodDraft Project(StructuredMethodDraft method)
    {
        ArgumentNullException.ThrowIfNull(method);

        var parents = exceptionGroupParents.Build(exceptionGroups.Collect(method));
        var projectedGroups = new Dictionary<StructuredExceptionGroupDraft, StructuredExceptionGroupDraft>(
            ReferenceEqualityComparer.Instance);

        return method with
        {
            Body = ProjectSequence(method.Body, null),
            ExceptionGroups = [.. method.ExceptionGroups.Select(ProjectGroup)],
            BodyParts = [.. method.BodyParts.Select(part => ProjectPart(part, null))]
        };

        StructuredExceptionGroupDraft ProjectGroup(StructuredExceptionGroupDraft group)
        {
            if (projectedGroups.TryGetValue(group, out var projected))
            {
                return projected;
            }

            projected = group with
            {
                ProtectedParts = [.. group.ProtectedParts.Select(part => ProjectPart(part, group))],
                Clauses =
                [
                    .. group.Clauses.Select(clause => clause with
                    {
                        HandlerBody = ProjectSequence(clause.HandlerBody, group),
                        FilterBody = clause.FilterBody is null
                            ? null
                            : ProjectSequence(clause.FilterBody, group)
                    })
                ],
                NormalContinuations =
                [
                    .. group.NormalContinuations.Select(continuation => continuation with
                    {
                        Body = ProjectSequence(
                            continuation.Body,
                            parents.GetValueOrDefault(group))
                    })
                ],
                ContinuationDispatcher = ProjectDispatcher(
                    group.ContinuationDispatcher,
                    parents.GetValueOrDefault(group))
            };
            projectedGroups.Add(group, projected);
            return projected;
        }

        StructuredExceptionPartDraft ProjectPart(
            StructuredExceptionPartDraft part,
            StructuredExceptionGroupDraft? activeGroup) =>
            part switch
            {
                StructuredExceptionCodeDraft code => code with
                {
                    Body = ProjectSequence(code.Body, activeGroup)
                },
                StructuredNestedExceptionGroupDraft nested => nested with
                {
                    Group = ProjectGroup(nested.Group)
                },
                _ => part
            };

        StructuredSequenceDraft ProjectSequence(
            StructuredSequenceDraft sequence,
            StructuredExceptionGroupDraft? activeGroup) =>
            sequence with
            {
                Regions =
                [
                    .. sequence.Regions
                        .Where(region =>
                            region is not StructuredExceptionRegionDraft exception ||
                            ReferenceEquals(
                                parents.GetValueOrDefault(exception.Group),
                                activeGroup))
                        .Select(region => ProjectRegion(region, activeGroup))
                ]
            };

        StructuredRegionDraft ProjectRegion(
            StructuredRegionDraft region,
            StructuredExceptionGroupDraft? activeGroup) =>
            region switch
            {
                StructuredExceptionRegionDraft exception => exception with
                {
                    Group = ProjectGroup(exception.Group)
                },
                StructuredIfDraft conditional => conditional with
                {
                    WhenTrue = ProjectSequence(conditional.WhenTrue, activeGroup),
                    WhenFalse = ProjectSequence(conditional.WhenFalse, activeGroup)
                },
                StructuredLoopDraft loop => loop with
                {
                    Body = ProjectSequence(loop.Body, activeGroup),
                    ContinueBody = ProjectSequence(loop.ContinueBody, activeGroup),
                    ExitBody = ProjectSequence(loop.ExitBody, activeGroup)
                },
                StructuredPostTestLoopDraft loop => loop with
                {
                    Body = ProjectSequence(loop.Body, activeGroup),
                    ContinueBody = ProjectSequence(loop.ContinueBody, activeGroup),
                    ExitBody = ProjectSequence(loop.ExitBody, activeGroup)
                },
                StructuredDispatcherDraft dispatcher => ProjectDispatcher(dispatcher, activeGroup)!,
                _ => region
            };

        StructuredDispatcherDraft? ProjectDispatcher(
            StructuredDispatcherDraft? dispatcher,
            StructuredExceptionGroupDraft? activeGroup) =>
            dispatcher is null
                ? null
                : dispatcher with
                {
                    Exits =
                    [
                        .. dispatcher.Exits.Select(exit => exit with
                        {
                            Body = ProjectSequence(exit.Body, activeGroup)
                        })
                    ]
                };
    }
}
