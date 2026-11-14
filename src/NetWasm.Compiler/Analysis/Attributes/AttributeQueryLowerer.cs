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
                var query = resolver.Resolve(call, stack);
                if (query is null)
                {
                    continue;
                }
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
        return replacements.Count == 0 ? body : bodies.Rewrite(body, replacements.ToImmutable());

        CompilerException AtQuery(CompilerException exception, CilInstruction instruction) => new(
            exception.Diagnostic with
            {
                Method = symbols.Format(body.Method),
                IlOffset = instruction.SourceOffset
            });
    }
}
