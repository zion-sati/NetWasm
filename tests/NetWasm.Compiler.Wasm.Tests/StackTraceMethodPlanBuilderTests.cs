using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Exceptions;
using NetWasm.Compiler.Wasm.Emission.Planning;
using Xunit;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class StackTraceMethodPlanBuilderTests
{
    [Fact]
    public void DisabledBuildDoesNotResolveMetadata()
    {
        var plan = Create(null!, null!, null!, null!).Build(
            false,
            default,
            default);

        Assert.Same(StackTraceMethodPlan.Disabled, plan);
    }

    [Fact]
    public void AssignsDeterministicIdsAndSidecarNames()
    {
        var program = new FakeProgram();
        var layouts = new RecordingLayoutProvider();
        var fields = new FixedExceptionFieldLayoutResolver(29);
        var directMethod = EmitterTestSupport.EntryKey;

        var plan = Create(program, program, fields, layouts).Build(
            true,
            [directMethod],
            ["Example.Generic<System.Int32>.Run()"]);

        Assert.Equal(1, plan.DirectMethodIds[directMethod]);
        Assert.Equal(2, plan.ConstructedMethodIds[
            "Example.Generic<System.Int32>.Run()"]);
        Assert.Collection(plan.Symbols,
            symbol =>
            {
                Assert.Equal(1, symbol.Id);
                Assert.Equal(
                    program.Format(program.GetMethod(directMethod)),
                    symbol.Name);
            },
            symbol =>
            {
                Assert.Equal(2, symbol.Id);
                Assert.Equal(
                    "Example.Generic<System.Int32>.Run()",
                    symbol.Name);
            });
        Assert.Equal(29, plan.ExceptionTraceOffset);
        Assert.Equal(layouts.StringTypeId, plan.StringTypeId);
        Assert.Equal("_stackTrace", fields.RequestedField);
    }

    [Fact]
    public void RejectsEnabledPlanWithoutReachableExceptionField()
    {
        var program = new FakeProgram();
        var layouts = new RecordingLayoutProvider();

        var error = Assert.Throws<InvalidOperationException>(() =>
            Create(program, program,
                new FixedExceptionFieldLayoutResolver(null), layouts).Build(
                    true,
                    [EmitterTestSupport.EntryKey],
                    []));

        Assert.Contains("System.Exception._stackTrace", error.Message,
            StringComparison.Ordinal);
    }

    private static readonly Func<
        IMethodRepository,
        ISymbolFormatter,
        IExceptionFieldLayoutResolver,
        ITypeLayoutProvider,
        IStackTraceMethodPlanBuilder> Create =
        static (methods, symbols, fields, layouts) =>
            new StackTraceMethodPlanBuilder(methods, symbols, fields, layouts);

    private sealed class FixedExceptionFieldLayoutResolver(int? offset) :
        IExceptionFieldLayoutResolver
    {
        public string? RequestedField { get; private set; }

        public int? Resolve(string fieldName)
        {
            RequestedField = fieldName;
            return offset;
        }
    }
}
