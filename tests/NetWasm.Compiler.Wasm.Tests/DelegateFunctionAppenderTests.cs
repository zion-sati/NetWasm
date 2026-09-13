using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class DelegateFunctionAppenderTests
{
    [Fact]
    public void AppendsInvokesWithoutTraversalHelpersWhenEqualityIsAbsent()
    {
        var fixture = new Fixture();

        fixture.Appender.Append(fixture.Functions, [fixture.Invoke], [], default,
            default, default, fixture.Target, fixture.FunctionIndices);

        Assert.Equal($"delegate.invoke<{fixture.Invoke.DeclaringType.CanonicalName}>",
            Assert.Single(fixture.Functions).Name);
        Assert.Equal(1, fixture.Invokes.Count);
        Assert.Equal(0, fixture.Helpers.Count);
    }

    [Fact]
    public void AppendsEveryTraversalHelperWhenEqualityIsPresent()
    {
        var fixture = new Fixture();
        var delegateType = CliTypeIdentity.Named(EmitterTestSupport.Assembly,
            "Tests", "Callback", isValueType: false);

        fixture.Appender.Append(fixture.Functions, [], [delegateType],
            OptionalFunctionIndex.At(7), OptionalFunctionIndex.At(8),
            OptionalFunctionIndex.At(9), fixture.Target, fixture.FunctionIndices);

        Assert.Equal(["delegate.count", "delegate.leaf", "delegate.equals",
            "delegate.remove"], fixture.Functions.Select(function => function.Name));
        Assert.Equal(4, fixture.Helpers.Count);
        Assert.All(fixture.Helpers.Targets,
            target => Assert.Equal([delegateType], target.DelegateTypes.ToArray()));
    }

    [Fact]
    public void RejectsMissingFunctionsInvokeTargetAndResolver()
    {
        var fixture = new Fixture();

        Assert.Throws<ArgumentNullException>(() => fixture.Appender.Append(null!, [], [],
            default, default, default, fixture.Target, fixture.FunctionIndices));
        Assert.Throws<ArgumentNullException>(() => fixture.Appender.Append(fixture.Functions,
            [], [], default, default, default, null!, fixture.FunctionIndices));
        Assert.Throws<ArgumentNullException>(() => fixture.Appender.Append(fixture.Functions,
            [], [], default, default, default, fixture.Target, null!));
    }

    private sealed class Fixture
    {
        public List<WasmFunctionDefinition> Functions { get; } = [];
        public RecordingDelegateHelperEmitter Helpers { get; } = new();
        public RecordingDelegateInvokeEmitter Invokes { get; } = new();
        public IDelegateFunctionAppender Appender { get; }
        public MethodInstanceModel Invoke { get; }
        public DelegateInvokeTarget Target { get; }
        public IFunctionIndexResolver FunctionIndices { get; }

        public Fixture()
        {
            var program = new FakeProgram();
            var definition = program.GetMethod(EmitterTestSupport.EntryKey);
            Invoke = new(definition,
                CliTypeIdentity.Named(EmitterTestSupport.Assembly, "Tests", "Callback",
                    isValueType: false), [], definition.Signature);
            var instructionTarget = EmitterTestSupport.CreateInstructionModuleTarget(program);
            Target = new(instructionTarget.FunctionIndices, []);
            FunctionIndices = EmitterTestSupport.CreateFunctionIndexResolver(program);
            Appender = new[]
            {
                new DelegateFunctionAppender(Helpers, Helpers, Helpers, Helpers,
                    Invokes, new FixedFunctionTypeResolver()),
            }.Cast<IDelegateFunctionAppender>().Single();
        }
    }

    private sealed class RecordingDelegateHelperEmitter :
        IDelegateCountFunctionEmitter, IDelegateLeafFunctionEmitter,
        IDelegateEqualityFunctionEmitter, IDelegateRemoveFunctionEmitter
    {
        public List<DelegateHelperTarget> Targets { get; } = [];
        public int Count => Targets.Count;

        public byte[] Emit(DelegateHelperTarget target)
        {
            Targets.Add(target);
            return [(byte)Targets.Count];
        }
    }

    private sealed class RecordingDelegateInvokeEmitter : IDelegateInvokeFunctionEmitter
    {
        public int Count { get; private set; }

        public byte[] Emit(MethodInstanceModel invoke, DelegateInvokeTarget targetProgram,
            IFunctionIndexResolver functionIndices)
        {
            Count++;
            return [1];
        }
    }

    private sealed class FixedFunctionTypeResolver : IManagedMethodFunctionTypeResolver
    {
        public WasmFunctionType Resolve(MethodDefinitionModel method) =>
            WasmFunctionType.Create(CliValueKind.I4);

        public WasmFunctionType Resolve(MethodInstanceModel method) =>
            WasmFunctionType.Create(CliValueKind.I4);
    }
}
