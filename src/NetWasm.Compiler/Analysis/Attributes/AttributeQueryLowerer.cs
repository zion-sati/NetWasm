// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Attributes;

namespace NetWasm.Compiler.Analysis.Attributes;

// Mediator: connect call-site proofs and metadata plans to ordinary CIL. All
// allocation, dependency, exception and root analysis runs on the resulting body.
internal sealed class AttributeQueryLowerer(
    ICalledMethodResolver calls,
    IAttributeQueryClassifier classifier,
    IControlFlowGraphBuilder graphs,
    ITypedStackValidator stacks,
    IAttributeProvenanceAnalyzer provenance,
    IAttributeQueryCompileTimeAnswerer answers,
    IAttributeQueryBodyRewriter bodies,
    ISymbolFormatter symbols) : IAttributeQueryLowerer
{
    public CilMethodBody Lower(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var queries = ImmutableDictionary.CreateBuilder<int, AttributeQueryCall>();
        foreach (var instruction in body.Instructions)
        {
            // Indirect calls retain their managed implementations. Only direct
            // calls can carry the caller's bounded target and filter proofs.
            if (instruction.Operation is not (CilOperation.Call or CilOperation.CallVirtual))
            {
                continue;
            }
            var method = calls.Resolve(instruction);
            if (method is null)
            {
                continue;
            }
            try
            {
                var query = classifier.Classify(method);
                if (query is null)
                {
                    continue;
                }
                queries.Add(instruction.Offset, query);
            }
            catch (CompilerException exception)
            {
                throw AtQuery(exception, instruction);
            }
        }
        if (queries.Count == 0)
        {
            return body;
        }
        var proofs = provenance.Analyze(stacks.Validate(graphs.Build(body)));
        var replacements = ImmutableDictionary.CreateBuilder<int, AttributeCilFragment>();
        var nextLocal = body.Locals.Length;
        foreach (var instruction in body.Instructions)
        {
            if (!queries.TryGetValue(instruction.Offset, out var call))
            {
                continue;
            }
            try
            {
                if (!proofs.TryGetValue(instruction.Offset, out var stack))
                {
                    continue;
                }
                switch (answers.Answer(call, stack, nextLocal))
                {
                    case AttributeQueryAnswer.Answered { Fragment: var fragment }:
                        replacements.Add(instruction.Offset, fragment);
                        nextLocal += fragment.LocalTypes.Length;
                        break;
                    case AttributeQueryAnswer.Unbuildable { Broad: false, Exception: var unbuildable }:
                        throw unbuildable;
                        // An attribute the program did not name that cannot be built here, or a target or filter the body
                        // does not name: the query keeps its fallback, and the settled program reports it
                        // (UnplannedAttributeQueryFinder, NW1019) unless the reducer answers it there.
                }
            }
            catch (CompilerException exception)
            {
                throw AtQuery(exception, instruction);
            }
        }
        return replacements.Count == 0 ? body : bodies.Rewrite(body, replacements.ToImmutable());

        CompilerException AtQuery(CompilerException exception, CilInstruction instruction) => new(
            exception.Diagnostic with
            {
                Method = symbols.Format(body.Method),
                IlOffset = instruction.SourceOffset
            });
    }
}
