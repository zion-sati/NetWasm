using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using Structured = NetWasm.Compiler.ControlFlow.Structured;

namespace NetWasm.Compiler.Diagnostics;

internal sealed class StructuredBlockOwnershipAnalyzer : IStructuredBlockOwnershipAnalyzer
{
    public ImmutableArray<StructuredBlockOwnershipVisit> Analyze(Structured.StructuredMethod method)
    {
        ArgumentNullException.ThrowIfNull(method);

        var visits = ImmutableArray.CreateBuilder<StructuredBlockOwnershipVisit>();
        var firstPaths = new Dictionary<Structured.StructuredBlockId, string>();
        var groups = new HashSet<Structured.StructuredExceptionGroupId>();
        VisitSequence(method.Body, "Body");
        return visits.ToImmutable();

        void Record(Structured.StructuredBlockOccurrence occurrence, string path)
        {
            if (occurrence.Role != Structured.StructuredBlockRole.Owner)
                return;
            firstPaths.TryGetValue(occurrence.Block, out var firstPath);
            visits.Add(new StructuredBlockOwnershipVisit(occurrence.Block.Value, path, firstPath));
            firstPaths.TryAdd(occurrence.Block, path);
        }

        void VisitSequence(Structured.StructuredSequence sequence, string path)
        {
            for (var index = 0; index < sequence.Regions.Length; index++)
            {
                var region = sequence.Regions[index];
                var regionPath = At(path, "Regions", index);
                switch (region)
                {
                    case Structured.StructuredCode code:
                        Record(code.Occurrence, regionPath);
                        break;
                    case Structured.StructuredLoopBreak:
                    case Structured.StructuredLoopContinue:
                    case Structured.StructuredDispatcherContinue:
                        break;
                    case Structured.StructuredIf branch:
                        Record(branch.Condition, regionPath + ".Condition");
                        VisitSequence(branch.WhenTrue, regionPath + ".WhenTrue");
                        VisitSequence(branch.WhenFalse, regionPath + ".WhenFalse");
                        break;
                    case Structured.StructuredLoop loop:
                        Record(loop.Condition, regionPath + ".Condition");
                        VisitSequence(loop.Body, regionPath + ".Body");
                        VisitSequence(loop.ContinueBody, regionPath + ".ContinueBody");
                        VisitSequence(loop.ExitBody, regionPath + ".ExitBody");
                        break;
                    case Structured.StructuredPostTestLoop loop:
                        VisitSequence(loop.Body, regionPath + ".Body");
                        Record(loop.Condition, regionPath + ".Condition");
                        VisitSequence(loop.ContinueBody, regionPath + ".ContinueBody");
                        VisitSequence(loop.ExitBody, regionPath + ".ExitBody");
                        break;
                    case Structured.StructuredExceptionRegion exception:
                        VisitGroup(exception.Group, regionPath + ".Group");
                        break;
                    case Structured.StructuredDispatcher dispatcher:
                        for (var blockIndex = 0; blockIndex < dispatcher.Blocks.Length; blockIndex++)
                        {
                            Record(
                                dispatcher.Blocks[blockIndex].Occurrence,
                                At(regionPath, "Blocks", blockIndex));
                        }

                        for (var exitIndex = 0; exitIndex < dispatcher.Exits.Length; exitIndex++)
                        {
                            VisitSequence(
                                dispatcher.Exits[exitIndex].Body,
                                At(regionPath, "Exits", exitIndex) + ".Body");
                        }

                        break;
                    default:
                        throw new InvalidOperationException(
                            $"Unsupported structured region {region.GetType().FullName}.");
                }
            }
        }

        void VisitGroup(Structured.StructuredExceptionGroupId groupId, string path)
        {
            if (!groups.Add(groupId))
            {
                return;
            }
            var group = method.ExceptionGroups[groupId];

            for (var index = 0; index < group.ProtectedParts.Length; index++)
            {
                var part = group.ProtectedParts[index];
                var partPath = At(path, "ProtectedParts", index);
                switch (part)
                {
                    case Structured.StructuredExceptionCode code:
                        VisitSequence(code.Body, partPath + ".Body");
                        break;
                    case Structured.StructuredNestedExceptionGroup nested:
                        VisitGroup(nested.Group, partPath + ".Group");
                        break;
                    default:
                        throw new InvalidOperationException(
                            $"Unsupported structured exception part {part.GetType().FullName}.");
                }
            }

            for (var index = 0; index < group.Clauses.Length; index++)
            {
                var clause = group.Clauses[index];
                var clausePath = At(path, "Clauses", index);
                VisitSequence(clause.HandlerBody, clausePath + ".HandlerBody");
                if (clause.FilterBody is not null)
                {
                    VisitSequence(clause.FilterBody, clausePath + ".FilterBody");
                }
            }

            for (var index = 0; index < group.NormalContinuations.Length; index++)
            {
                VisitSequence(
                    group.NormalContinuations[index].Body,
                    At(path, "NormalContinuations", index) + ".Body");
            }
        }
    }

    private static string At(string path, string collection, int index) =>
        string.Concat(
            path,
            ".",
            collection,
            "[",
            index.ToString(CultureInfo.InvariantCulture),
            "]");
}
