// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers.RuntimeAsync;

// A runtime-async method written as one (neither a rewritten state machine's kickoff nor an async iterator's member) runs
// its lowered body in a method the compiler provides beside it (MethodDefinitionModel.RuntimeAsyncBody): the body takes
// the method's parameters and then the call mode, and is not wrapped in the lowering's catch (its own protected blocks
// stay). Wasmtime's exception ABI makes every call inside a try_table clobber every register, so a catch around the
// whole body made every call in it save and reload what it keeps; the catch now stands only around the entry's call of
// the body (RuntimeAsyncEntryBuilder).
internal static class RuntimeAsyncBodies
{
    // The body's token: the method's with bits 30 and 31 set, which no metadata token has (a resumption stub takes bit 30
    // alone and a native marshalling stub's inner method bit 31 alone).
    internal static int Token(int methodToken) => methodToken | unchecked((int)0xC0000000);

    // Whether a token is a body's, and the token of the method whose body it is: a body has that method's generic
    // parameters, constraints included, which metadata records only for the method.
    internal static bool IsBody(int token) => (token & unchecked((int)0xC0000000)) == unchecked((int)0xC0000000);

    internal static int Method(int bodyToken) => bodyToken & ~unchecked((int)0xC0000000);

    // The body's signature: the method's parameters, then the call mode.
    internal static MethodSignatureModel Signature(MethodSignatureModel method) =>
        new(method.ReturnSignatureType, method.ParameterSignatureTypes.Add(CliTypeIdentity.FromStackKind(CliValueKind.I4)));

    // The body of the method's instance, over the same declaring type and method arguments.
    internal static MethodInstanceModel For(IMethodRepository methods, MethodInstanceModel method) =>
        Over(methods.GetMethod(method.Definition.RuntimeAsyncBody ??
            throw new System.ArgumentException("not a runtime-async method with a separate body", nameof(method))), method);

    // The body definition's instance over the method instance's declaring type and method arguments; its signature is the
    // method instance's, whose types the stack-type resolver has settled (another assembly's enum as its integer), and the
    // mode.
    internal static MethodInstanceModel Over(MethodDefinitionModel body, MethodInstanceModel method) =>
        new(body, method.DeclaringType, method.MethodArguments, Signature(method.Signature));

    // The method whose body the instance is, over the same declaring type and method arguments.
    internal static MethodInstanceModel Of(IMethodRepository methods, MethodInstanceModel body)
    {
        var method = methods.GetMethod(body.Definition.RuntimeAsyncBodyOf ??
            throw new System.ArgumentException("not a runtime-async body", nameof(body)));
        var parameters = body.Signature.ParameterSignatureTypes;
        return new(method, body.DeclaringType, body.MethodArguments,
            new MethodSignatureModel(body.Signature.ReturnSignatureType, parameters.RemoveAt(parameters.Length - 1)));
    }
}
