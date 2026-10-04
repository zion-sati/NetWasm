using System;
using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Attributes;

// Mediator: connect call-site proofs and metadata plans to ordinary CIL. All
// allocation, dependency, exception and root analysis runs on the resulting body.
internal sealed class AttributeQueryLowerer(
    ICalledMethodResolver calls,
    IAttributeQueryClassifier classifier,
    IControlFlowGraphBuilder graphs,
    ITypedStackValidator stacks,
    IAttributeProvenanceAnalyzer provenance,
    IAttributeQueryResolver resolver,
    IAttributeQueryPlanner planner,
    IAttributeQueryCilBuilder fragments,
    IAttributeQueryBodyRewriter bodies,
    ISymbolFormatter symbols,
    MethodInstanceModel ambiguityConstructor) : IAttributeQueryLowerer
{
    public CilMethodBody Lower(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var queries = ImmutableDictionary.CreateBuilder<int, AttributeQueryCall>();
        foreach (var instruction in body.Instructions)
        {
            // Method tokens are metadata references, so the ordinary call
            // resolver deliberately ignores them. Inspect the decoder-bound
            // method here as well: otherwise expression-tree metadata could
            // bypass the direct-query boundary without an executable call.
            var method = instruction.Operation == CilOperation.LoadMethodToken
                ? ((CilOperand.MethodInstance)instruction.Operand).Value
                : calls.Resolve(instruction);
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
                if (instruction.Operation is not (CilOperation.Call or CilOperation.CallVirtual))
                {
                    throw new CompilerException(new(DiagnosticCode.UnsupportedMetadata,
                        "Custom attribute queries require a direct bounded call; query method handles are not supported."));
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
                    throw new CompilerException(new(DiagnosticCode.UnsupportedMetadata,
                        "Custom attribute query has no supported type provenance at this control-flow location."));
                }
                var query = resolver.Resolve(call, stack);
                var fragment = fragments.Build(new(call.Operation, planner.Plan(query), call.ArgumentCount,
                    call.InheritArgument, query.InheritConstant, CliTypeIdentity.SzArray(query.Filter),
                    ambiguityConstructor, nextLocal));
                replacements.Add(instruction.Offset, fragment);
                nextLocal += fragment.LocalTypes.Length;
            }
            catch (CompilerException exception)
            {
                throw AtQuery(exception, instruction);
            }
        }
        return bodies.Rewrite(body, replacements.ToImmutable());

        CompilerException AtQuery(CompilerException exception, CilInstruction instruction) => new(
            exception.Diagnostic with
            {
                Method = symbols.Format(body.Method),
                IlOffset = instruction.SourceOffset
            });
    }
}
