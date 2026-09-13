using System;
using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class StructuredExceptionGroupKeyFactoryTests
{
    public static TheoryData<object> Factories =>
        new() { new StructuredExceptionGroupKeyFactory() };

    [Theory]
    [MemberData(nameof(Factories))]
    public void CreateScopesLocalGroupIdentifiersByMethod(object candidate)
    {
        var factory = Assert.IsAssignableFrom<IStructuredExceptionGroupKeyFactory>(candidate);
        var program = new FakeProgram();
        var first = Structure(
            program,
            program.GetMethod(EntryKey),
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(1, CilOperation.Return));
        var second = first with
        {
            Header = first.Header with
            {
                Method = first.Header.Method with { Key = Key(0x06000002) },
            },
        };
        var constructed = first with
        {
            Header = first.Header with
            {
                MethodInstance = new MethodInstanceModel(
                    first.Header.Method,
                    CliTypeIdentity.FromStackKind(CliValueKind.ManagedReference),
                    ImmutableArray<CliTypeIdentity>.Empty,
                    first.Header.Method.Signature),
            },
        };
        var group = new StructuredExceptionGroupId(0);

        var firstKey = factory.Create(first, group);

        Assert.Equal(first.Header.Method.Key, firstKey.Method);
        Assert.Null(firstKey.MethodInstanceCanonicalName);
        Assert.Equal(group, firstKey.Group);
        Assert.Equal(firstKey, factory.Create(first, group));
        Assert.NotEqual(firstKey, factory.Create(second, group));
        Assert.NotEqual(firstKey, factory.Create(constructed, group));
    }

    [Theory]
    [MemberData(nameof(Factories))]
    public void CreateRejectsMissingMethod(object candidate)
    {
        var factory = Assert.IsAssignableFrom<IStructuredExceptionGroupKeyFactory>(candidate);
        Assert.Throws<ArgumentNullException>(() =>
            factory.Create(null!, new StructuredExceptionGroupId(0)));
    }
}
