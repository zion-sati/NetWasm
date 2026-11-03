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
            default,
            ImmutableDictionary<EntityKey, ImmutableArray<WasmSourceLocation>>.Empty);

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
            [new("Example.Generic<System.Int32>.Run()", directMethod)],
            ImmutableDictionary<EntityKey, ImmutableArray<WasmSourceLocation>>.Empty);

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

    // .NET's StackTrace.ShowInStackTrace leaves out a method marked
    // StackTraceHidden, or whose type is, and an AggressiveInlining method.
    [Fact]
    public void HiddenMethodsRecordNoFrames()
    {
        var program = new FakeProgram();
        var shown = EmitterTestSupport.EntryKey;
        var entry = program.GetMethod(shown);
        var hidden = new EntityKey(shown.Assembly, 0x06000101);
        var inlined = new EntityKey(shown.Assembly, 0x06000102);
        var genericHidden = new EntityKey(shown.Assembly, 0x06000103);
        var methods = new MethodTable(new()
        {
            [shown] = entry,
            [hidden] = entry with { Key = hidden, Name = "Throw", IsStackTraceHidden = true },
            [inlined] = entry with { Key = inlined, Name = "Inlined", IsAggressiveInlining = true },
            [genericHidden] = entry with { Key = genericHidden, Name = "ThrowFor", IsStackTraceHidden = true },
        });

        var plan = Create(methods, program, new FixedExceptionFieldLayoutResolver(29), new RecordingLayoutProvider()).Build(
            true,
            [hidden, shown, inlined],
            [new("Example.Helpers.ThrowFor<System.Int32>()", genericHidden), new("Example.Generic<System.Int32>.Run()", shown)],
            ImmutableDictionary<EntityKey, ImmutableArray<WasmSourceLocation>>.Empty);

        Assert.Equal([shown], plan.DirectMethodIds.Keys);
        Assert.Equal(1, plan.DirectMethodIds[shown]);
        Assert.Equal(["Example.Generic<System.Int32>.Run()"], plan.ConstructedMethodIds.Keys);
        Assert.Equal([1, 2], plan.Symbols.Select(static symbol => symbol.Id));
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
                    [],
                    ImmutableDictionary<EntityKey, ImmutableArray<WasmSourceLocation>>.Empty));

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

    private sealed class MethodTable(Dictionary<EntityKey, MethodDefinitionModel> methods) : IMethodRepository
    {
        public MethodDefinitionModel GetMethod(EntityKey key) => methods[key];
    }

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
