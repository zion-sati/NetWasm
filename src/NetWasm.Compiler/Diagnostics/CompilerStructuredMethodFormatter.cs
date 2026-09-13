using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Structured = NetWasm.Compiler.ControlFlow.Structured;

namespace NetWasm.Compiler.Diagnostics;

internal sealed class CompilerStructuredMethodFormatter : ICompilerStructuredMethodFormatter
{
    public string FormatStructure(Structured.StructuredMethod method)
    {
        ArgumentNullException.ThrowIfNull(method);
        var text = new StringBuilder();
        WriteSequence(
            text,
            method,
            method.Body,
            "  ",
            []);
        return text.ToString().ReplaceLineEndings("\n");
    }

    private static void WriteSequence(
        StringBuilder text,
        Structured.StructuredMethod method,
        Structured.StructuredSequence sequence,
        string indent,
        HashSet<Structured.StructuredExceptionGroupId> writtenGroups)
    {
        foreach (var region in sequence.Regions)
        {
            switch (region)
            {
                case Structured.StructuredCode code:
                    text.Append(indent).Append("block IL_")
                        .Append(Hex(method.Blocks[code.Occurrence.Block].StartOffset))
                        .Append(" role=")
                        .Append(code.Occurrence.Role)
                        .Append(" leave=")
                        .Append(code.Occurrence.LeaveContinuation?.Value.ToString(
                            CultureInfo.InvariantCulture) ?? "none")
                        .AppendLine();
                    break;
                case Structured.StructuredIf conditional:
                    text.Append(indent).Append("if IL_")
                        .Append(Hex(method.Blocks[conditional.Condition.Block].StartOffset))
                        .AppendLine();
                    WriteSequence(
                        text, method, conditional.WhenTrue, indent + "  true ", writtenGroups);
                    WriteSequence(
                        text, method, conditional.WhenFalse, indent + "  false ", writtenGroups);
                    break;
                case Structured.StructuredLoop loop:
                    text.Append(indent).Append("loop IL_")
                        .Append(Hex(method.Blocks[loop.Condition.Block].StartOffset)).AppendLine();
                    WriteSequence(text, method, loop.Body, indent + "  ", writtenGroups);
                    WriteSequence(
                        text, method, loop.ContinueBody, indent + "  continue ", writtenGroups);
                    WriteSequence(text, method, loop.ExitBody, indent + "  exit ", writtenGroups);
                    break;
                case Structured.StructuredPostTestLoop loop:
                    text.Append(indent).Append("post-loop IL_")
                        .Append(Hex(method.Blocks[loop.Condition.Block].StartOffset)).AppendLine();
                    WriteSequence(text, method, loop.Body, indent + "  body ", writtenGroups);
                    WriteSequence(
                        text, method, loop.ContinueBody, indent + "  continue ", writtenGroups);
                    WriteSequence(text, method, loop.ExitBody, indent + "  exit ", writtenGroups);
                    break;
                case Structured.StructuredExceptionRegion exception:
                    var group = method.ExceptionGroups[exception.Group];
                    text.Append(indent).Append("exception-region try=IL_")
                        .Append(Hex(group.TryOffset))
                        .Append(" fallthrough=")
                        .Append(exception.FallthroughContinuation?.Value.ToString(
                            CultureInfo.InvariantCulture) ?? "none")
                        .AppendLine();
                    WriteGroup(text, method, exception.Group, indent + "  ", writtenGroups);
                    break;
                case Structured.StructuredDispatcher dispatcher:
                    text.Append(indent).Append("dispatcher entry=")
                        .Append(dispatcher.EntryBlock?.Value).Append(" blocks=")
                        .AppendJoin(',', dispatcher.Blocks.Select(block =>
                            $"{block.Occurrence.Block.Value}@IL_{Hex(method.Blocks[block.Occurrence.Block].StartOffset)}:" +
                            $"{block.Occurrence.Role}:" +
                            $"{block.Occurrence.LeaveContinuation?.Value.ToString(CultureInfo.InvariantCulture) ?? "none"}"))
                        .AppendLine();
                    foreach (var exit in dispatcher.Exits)
                    {
                        text.Append(indent).Append("  exit block=")
                            .Append(exit.Target.Value).AppendLine();
                        WriteSequence(text, method, exit.Body, indent + "    ", writtenGroups);
                    }
                    break;
                case Structured.StructuredLoopBreak:
                    text.Append(indent).AppendLine("break");
                    break;
                case Structured.StructuredLoopContinue:
                    text.Append(indent).AppendLine("continue");
                    break;
                case Structured.StructuredDispatcherContinue dispatcherContinue:
                    text.Append(indent).Append("dispatcher-continue block=")
                        .Append(dispatcherContinue.Target.Value).AppendLine();
                    break;
            }
        }
    }

    private static void WriteGroup(
        StringBuilder text,
        Structured.StructuredMethod method,
        Structured.StructuredExceptionGroupId groupId,
        string indent,
        HashSet<Structured.StructuredExceptionGroupId> writtenGroups)
    {
        if (!writtenGroups.Add(groupId))
        {
            text.Append(indent).AppendLine("group already described");
            return;
        }
        var group = method.ExceptionGroups[groupId];
        foreach (var part in group.ProtectedParts)
        {
            switch (part)
            {
                case Structured.StructuredExceptionCode code:
                    text.Append(indent).AppendLine("protected code");
                    WriteSequence(text, method, code.Body, indent + "  ", writtenGroups);
                    break;
                case Structured.StructuredNestedExceptionGroup nested:
                    var nestedGroup = method.ExceptionGroups[nested.Group];
                    text.Append(indent).Append("nested try=IL_")
                        .Append(Hex(nestedGroup.TryOffset)).AppendLine();
                    WriteGroup(text, method, nested.Group, indent + "  ", writtenGroups);
                    break;
            }
        }
        foreach (var clause in group.Clauses)
        {
            text.Append(indent).Append("handler ")
                .Append(clause.Kind).AppendLine();
            if (clause.FilterBody is not null)
            {
                WriteSequence(
                    text, method, clause.FilterBody, indent + "  filter ", writtenGroups);
            }
            WriteSequence(text, method, clause.HandlerBody, indent + "  ", writtenGroups);
        }
        foreach (var continuation in group.NormalContinuations)
        {
            text.Append(indent).Append("continuation IL_")
                .Append(Hex(continuation.TargetOffset)).AppendLine();
            WriteSequence(text, method, continuation.Body, indent + "  ", writtenGroups);
        }
    }

    private static string Hex(int value) =>
        value.ToString("x4", CultureInfo.InvariantCulture);
}
