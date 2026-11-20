// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests.Analysis;

public sealed class ConstrainedCallTargetResolverTests
{
    private static readonly AssemblyIdentity Assembly = new("Conversions");

    // Without an exact implementation of an abstract declaration, a candidate is matched by stack kinds of its
    // parameters and its result: of Value's conversions to int and to long and from long, only the first matches.
    [Fact]
    public void AStackKindMatchNeedsTheResultToo()
    {
        var holder = new EntityKey(Assembly, 0x02000001);
        var value = CliTypeIdentity.Named(Assembly, "Conversions", "Value", true);
        var other = CliTypeIdentity.Named(Assembly, "Conversions", "Other", true);
        var toInt = Operator(0x06000001, holder, value, CliTypeIdentity.FromStackKind(CliValueKind.I4));
        var toLong = Operator(0x06000002, holder, value, CliTypeIdentity.FromStackKind(CliValueKind.I8));
        var fromLong = Operator(0x06000004, holder, CliTypeIdentity.FromStackKind(CliValueKind.I8), value);
        var declaration = Operator(0x06000003, new(Assembly, 0x02000002), other, CliTypeIdentity.FromStackKind(CliValueKind.I4));
        var concrete = new TypeDefinitionModel(holder, "Conversions", "Value", true, [], [fromLong.Key, toLong.Key, toInt.Key]);
        var resolver = new ConstrainedCallTargetResolver(null!, new Methods([toInt, toLong, fromLong]), new NoImplementations());

        var target = resolver.Resolve(new(value, concrete, declaration, declaration.Signature, [], []));

        Assert.Same(toInt, target);
    }

    private static MethodDefinitionModel Operator(int token, EntityKey owner, CliTypeIdentity parameter, CliTypeIdentity result) =>
        new(new(Assembly, token), owner, "op_Explicit", true, new MethodSignatureModel(result, [parameter]), 0);

    private sealed class Methods(MethodDefinitionModel[] methods) : IMethodRepository
    {
        public MethodDefinitionModel GetMethod(EntityKey key) => methods.Single(method => method.Key == key);
    }

    private sealed class NoImplementations : IMethodImplementationResolver
    {
        public ImmutableArray<MethodImplementationInstanceModel> GetMethodImplementations(CliTypeIdentity type) => [];
    }
}
