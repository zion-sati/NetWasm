using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;
using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Methods;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class MethodEmissionContextFactoryTests
{
    [Fact]
    public void AllocatesStableNonOverlappingMethodState()
    {
        var body = CreateBody();
        var group = new StructuredExceptionGroupId(0);
        var roots = new MethodRootMap(
            EntryKey,
            [],
            []);
        var values = new ValueFrameLayout(
            8,
            ImmutableDictionary<int, int>.Empty.Add(0, 0),
            [],
            [],
            []);

        var result = MethodEmissionContextFactory.Create(
            Header(body),
            roots,
            2,
            1,
            [group],
            values,
            FilterEnvironmentLayout.Empty,
            new ManagedMethodIdentity("Tests.Caller"));

        var context = result.Context;
        Assert.Equal(2, context.LocalBase);
        Assert.Equal(4, context.StackBase);
        Assert.Equal(22, context.ObjectTemporary);
        Assert.Equal(23, context.RootFrame);
        Assert.Equal(24, context.ExceptionTemporary);
        Assert.Equal(25, context.ExceptionFrameLocals[group]);
        Assert.Equal(26, context.ExceptionContinuationLocals[group]);
        Assert.Equal(27, context.ValueFrame);
        Assert.Equal(28, context.FilterRootFrame);
        Assert.Equal(29, context.InteropDescriptor);
        Assert.Equal(30, context.InteropResult);
        Assert.Equal(31, context.InteropHandle);
        Assert.Equal(32, context.NumericTemporaryI4);
        Assert.Equal(33, context.NumericTemporaryI8);
        Assert.Equal(34, context.DispatcherProgramCounter);
        Assert.Equal(35, context.NumericTemporaryI4Second);
        Assert.Equal(36, context.NumericTemporaryI4Third);
        Assert.Equal(37, context.NumericTemporaryI4Fourth);
        Assert.Equal(38, context.NumericTemporaryI4Fifth);
        Assert.Equal(37, result.LocalTypes.Length);
    }

    private static CilMethodBody CreateBody()
    {
        var method = new MethodDefinitionModel(
            EntryKey,
            TypeKey,
            "Run",
            true,
            MethodSignatureModel.Create(CliValueKind.Void),
            1);
        return new CilMethodBody(
            method,
            3,
            [CliValueKind.I4, CliValueKind.ManagedReference],
            [])
        {
            LocalSignatureTypes =
            [
                CliTypeIdentity.FromStackKind(CliValueKind.I4),
                CliTypeIdentity.FromStackKind(CliValueKind.ManagedReference),
            ],
        };
    }
}
