using System;
using System.Collections.Concurrent;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

internal sealed class StaticDataViewResolver(
    ICalleeMethodBodyReader methodBodies,
    IMethodSpecializer specializer,
    ITypeRepository types,
    IMethodRepository methods) : IStaticDataViewResolver
{
    private readonly ConcurrentDictionary<string, Resolution> _resolutions = [];

    public CilOperand.ReadOnlySpanData? Resolve(MethodInstanceModel producer)
    {
        ArgumentNullException.ThrowIfNull(producer);
        return _resolutions.GetOrAdd(
            producer.CanonicalName,
            _ => ResolveUncached(producer)).Span;
    }

    private Resolution ResolveUncached(MethodInstanceModel producer)
    {
        if (!producer.Definition.IsStatic ||
            !producer.Definition.HasManagedBody ||
            !producer.Signature.ParameterSignatureTypes.IsEmpty ||
            !ReadOnlySpanDataResolver.TryGetReadOnlySpanElement(
                producer.Signature.ReturnSignatureType,
                out var returnElement) ||
            producer.Signature.ReturnSignatureType.ContainsGenericParameters)
        {
            return Resolution.NotProven;
        }

        var declaringType = types.GetTypeDefinition(producer.Definition.DeclaringType);
        if (!declaringType.IsBeforeFieldInit &&
            declaringType.Methods
            .Select(methods.GetMethod)
            .Any(method => method.Name == ".cctor"))
        {
            return Resolution.NotProven;
        }

        if (!methodBodies.TryReadMethodBody(producer, out var read))
        {
            return Resolution.NotProven;
        }

        var body = specializer.Rewrite(read) with
        {
            MethodInstance = producer,
        };
        var effective = body.Instructions
            .Where(instruction => instruction.Operation != CilOperation.Nop)
            .ToArray();
        if (body.Method.Key != producer.Definition.Key ||
            body.MaxStack < 1 ||
            !body.ExceptionRegions.IsEmpty ||
            effective is not
            [
            { Operation: CilOperation.CreateReadOnlySpanFromRva } creation,
            { Operation: CilOperation.Return, Operand: CilOperand.None },
            ] ||
            creation.Operand is not CilOperand.ReadOnlySpanData span ||
            !span.SpanType.Equals(producer.Signature.ReturnSignatureType) ||
            !span.ElementType.Equals(returnElement))
        {
            return Resolution.NotProven;
        }

        return new(span);
    }

    private sealed record Resolution(CilOperand.ReadOnlySpanData? Span)
    {
        public static Resolution NotProven { get; } = new(
            (CilOperand.ReadOnlySpanData?)null);
    }
}
