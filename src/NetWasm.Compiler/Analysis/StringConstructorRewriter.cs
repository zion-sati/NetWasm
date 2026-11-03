using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

// A string's constructors have no bodies. As CoreCLR, NativeAOT and Mono do, a newobj
// of one calls string's static Ctor method of the same parameters instead, which
// validates the arguments and fills a string from FastAllocateString. Both leave the
// new string where the arguments were.
internal sealed class StringConstructorRewriter(
    ICalledMethodResolver calledMethods,
    ITypeFinder types,
    IMethodRepository methods,
    IMethodInstanceResolver instances,
    ISymbolFormatter symbols) : IMethodRewriteRule
{
    private readonly ConcurrentDictionary<EntityKey, MethodInstanceModel> _targets = new();

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        ImmutableArray<CilInstruction>.Builder? rewritten = null;
        var instructions = body.Instructions;
        for (var index = 0; index < instructions.Length; index++)
        {
            var instruction = instructions[index];
            if (instruction.Operation != CilOperation.NewObject ||
                calledMethods.Resolve(instruction)?.Definition is not { } constructor ||
                symbols.Format(constructor.DeclaringType) != "System.String")
            {
                continue;
            }
            rewritten ??= instructions.ToBuilder();
            rewritten[index] = instruction with
            {
                Operation = CilOperation.Call,
                Operand = new CilOperand.MethodInstance(Resolve(constructor, instruction.Offset)),
            };
        }
        return rewritten is null ? body : body with { Instructions = rewritten.ToImmutable() };
    }

    private MethodInstanceModel Resolve(MethodDefinitionModel constructor, int offset) =>
        _targets.GetOrAdd(constructor.Key, _ =>
        {
            var parameters = constructor.Signature.ParameterSignatureTypes;
            var method = types.FindType("System.String").Methods.Select(methods.GetMethod)
                .SingleOrDefault(method => method is { Name: "Ctor", IsStatic: true } &&
                    method.Signature.ParameterSignatureTypes.SequenceEqual(parameters)) ??
                throw new CompilerException(new CompilerDiagnostic(
                    DiagnosticCode.UnsupportedMetadata,
                    "the System.String constructor signature is not supported",
                    "System.String",
                    offset));
            return instances.ResolveMethodInstance(
                method.Key.Assembly, method.Key.MetadataToken, symbols.Format(method), 0);
        });
}
