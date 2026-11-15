// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Analysis.Attributes;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests;

public sealed class AttributeSetterBindingResolverTests
{
    [Fact]
    public void BindsDelegateTypeAndHelperMethodArgumentsIndependently()
    {
        var methods = new RecordingMethods();
        var constructor = new EntityKey(new("Delegates"), 0x06000001);
        var invoke = new EntityKey(new("Helpers"), 0x06000002);
        var resolver = new AttributeSetterBindingResolver(methods, constructor, invoke);
        var value = CliTypeIdentity.SzArray(CliTypeIdentity.FromStackKind(CliValueKind.I4));

        var result = ((IAttributeSetterBindingResolver)resolver).Resolve(value);

        Assert.Same(methods.Results[0], result.DelegateConstructor);
        Assert.Same(methods.Results[1], result.Invoke);
        Assert.Collection(methods.Requests,
            request =>
            {
                Assert.Equal(constructor, request.Key);
                Assert.Same(value, Assert.Single(request.Context.TypeArguments));
                Assert.Empty(request.Context.MethodArguments);
            },
            request =>
            {
                Assert.Equal(invoke, request.Key);
                Assert.Empty(request.Context.TypeArguments);
                Assert.Same(value, Assert.Single(request.Context.MethodArguments));
            });
    }

    [Fact]
    public void RejectsMissingValueTypeBeforeResolvingMethods()
    {
        var methods = new RecordingMethods();
        var resolver = new AttributeSetterBindingResolver(methods, default, default);
        Assert.Throws<ArgumentNullException>(() => ((IAttributeSetterBindingResolver)resolver).Resolve(null!));
        Assert.Empty(methods.Requests);
    }

    private sealed class RecordingMethods : IMethodInstanceResolver
    {
        internal List<(EntityKey Key, CliGenericContext Context)> Requests { get; } = [];
        internal List<MethodInstanceModel> Results { get; } = [];

        public MethodInstanceModel ResolveMethodInstance(AssemblyIdentity source, int metadataToken,
            string methodDisplayName, int ilOffset, CliGenericContext? genericContext = null)
        {
            Requests.Add((new(source, metadataToken), genericContext!.Value));
            var signature = MethodSignatureModel.Create(CliTypeIdentity.FromStackKind(CliValueKind.Void), []);
            var result = new MethodInstanceModel(new(new(source, metadataToken), new(source, 0x02000001),
                methodDisplayName, false, signature, 1),
                CliTypeIdentity.Named(source, "Example", "MethodOwner", false), [], signature);
            Results.Add(result);
            return result;
        }
    }
}
