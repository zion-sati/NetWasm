// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers.RuntimeAsync;

// The runtime async IL of an async iterator machine's MoveNextAsync or DisposeAsync (AsyncIteratorMoveNext,
// AsyncIteratorDisposeOf): rewritten from the machine's MoveNext (AsyncIteratorRewriter), or, when MoveNext has a shape
// the rewrite does not take (or is missing), the method as Roslyn wrote it awaiting the ValueTask it returns without its
// caller's context, as the caller would await it. Both members decide from the same MoveNext, so both are rewritten or
// neither.
internal sealed class AsyncIteratorBodyBuilder(
    ICilDecoderFactory decoders,
    IManagedAssemblyResolver assemblies,
    IMethodRepository methods,
    IAsyncIteratorRewriter rewriter,
    Func<IRuntimeAsyncMemberResolver> members,
    ICilBodyLayout layout) : IStateMachineBodyBuilder
{
    private readonly Lazy<IRuntimeAsyncMemberResolver> _members = new(members);

    public CilMethodBody Build(MethodInstanceModel method)
    {
        ArgumentNullException.ThrowIfNull(method);
        var assembly = assemblies.Resolve(method.Definition.Key.Assembly);
        var body = decoders.Create().Decode(assembly, method);
        var moveNextAsync = method.Definition.AsyncIteratorDisposeOf is { } awaited ? Sibling(method, awaited) : method;
        if (moveNextAsync.Definition.AsyncIteratorMoveNext is { } key)
        {
            var moveNext = Sibling(method, key);
            try
            {
                if (rewriter.Rewrite(method, body, moveNextAsync, moveNext, decoders.Create().Decode(assembly, moveNext)) is { } rewritten)
                    return rewritten;
            }
            catch (CompilerException error) when (error.Diagnostic.MissingMember is not null)
            {
                // MoveNext references a member its assembly lacks: the machine compiles as any other method and reports it.
            }
        }
        return StateMachineShapes.Awaiting(_members.Value, layout, method, body);
    }

    // A method of the same machine, instantiated as the member is.
    private MethodInstanceModel Sibling(MethodInstanceModel member, EntityKey key)
    {
        var definition = methods.GetMethod(key);
        return new MethodInstanceModel(definition, member.DeclaringType, [], definition.Signature);
    }
}
