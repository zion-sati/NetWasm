using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Instructions.Calls;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class InitializingCallEmitterTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InitializesOnlyRequiredOwnersBeforeForwardingTheUnchangedCall(bool required)
    {
        var fixture = new Fixture(required);
        var program = new FakeProgram();
        var method = program.GetMethod(EntryKey);
        var instruction = CreateInstructionRequest(CilOperation.Call, [CliValueKind.I4]);
        var request = new CallEmissionRequest(instruction,
            new(method, CliTypeIdentity.Named(Assembly, "Tests", "Owner", false), [], method.Signature), 0, 1);
        var code = new RecordingInstructionWriter();
        var indices = CreateFunctionIndexResolver(program);
        var emitter = Assert.IsAssignableFrom<ICallEmitter>(new InitializingCallEmitter(fixture, fixture, fixture));

        emitter.Emit(request, code, indices);

        Assert.Equal<string>(required ? ["classify", "initialize", "call"] : ["classify", "call"], fixture.Events);
        Assert.Same(method, fixture.Classified);
        Assert.Same(request, fixture.Call);
        Assert.Same(code, fixture.Code);
        Assert.Same(indices, fixture.Indices);
        if (required)
        {
            Assert.NotNull(fixture.Initialization);
            Assert.Equal(method.DeclaringType, fixture.Initialization.TypeDefinition);
            Assert.Equal(request.Method.DeclaringType, fixture.Initialization.DeclaringType);
            Assert.Same(instruction.Target.StaticInitializerGuards, fixture.Initialization.Guards);
            Assert.True(fixture.Initialization.IsMethodInvocation);
            Assert.Same(code, fixture.InitializationCode);
            Assert.Same(indices, fixture.InitializationIndices);
        }
        else
            Assert.Null(fixture.Initialization);
    }

    [Fact]
    public void RejectsMissingDependenciesAndCallInputsBeforeDelegating()
    {
        var fixture = new Fixture(false);
        Assert.Throws<ArgumentNullException>(() => new InitializingCallEmitter(null!, fixture, fixture));
        Assert.Throws<ArgumentNullException>(() => new InitializingCallEmitter(fixture, null!, fixture));
        Assert.Throws<ArgumentNullException>(() => new InitializingCallEmitter(fixture, fixture, null!));
        var emitter = Assert.IsAssignableFrom<ICallEmitter>(new InitializingCallEmitter(fixture, fixture, fixture));
        var program = new FakeProgram();
        var method = program.GetMethod(EntryKey);
        var request = new CallEmissionRequest(CreateInstructionRequest(CilOperation.Call),
            new(method, CliTypeIdentity.Named(Assembly, "Tests", "Owner", false), [], method.Signature), 0, 0);
        var code = new RecordingInstructionWriter();
        var indices = CreateFunctionIndexResolver(program);

        Assert.Throws<ArgumentNullException>(() => emitter.Emit(null!, code, indices));
        Assert.Throws<ArgumentNullException>(() => emitter.Emit(request, null!, indices));
        Assert.Throws<ArgumentNullException>(() => emitter.Emit(request, code, null!));
        Assert.Empty(fixture.Events);
    }

    private sealed class Fixture(bool required) : ICallEmitter,
        IMethodInitializationTriggerClassifier, IStaticInitializationEmitter
    {
        public List<string> Events { get; } = [];
        public MethodDefinitionModel? Classified { get; private set; }
        public CallEmissionRequest? Call { get; private set; }
        public StaticInitializationEmissionRequest? Initialization { get; private set; }
        public IWasmInstructionWriter? Code { get; private set; }
        public IWasmInstructionWriter? InitializationCode { get; private set; }
        public IFunctionIndexResolver? Indices { get; private set; }
        public IFunctionIndexResolver? InitializationIndices { get; private set; }

        public bool Classify(MethodDefinitionModel method)
        {
            Classified = method;
            Events.Add("classify");
            return required;
        }

        public void Emit(StaticInitializationEmissionRequest request, IWasmInstructionWriter code,
            IFunctionIndexResolver functionIndices)
        {
            Initialization = request;
            InitializationCode = code;
            InitializationIndices = functionIndices;
            Events.Add("initialize");
        }

        public void Emit(CallEmissionRequest request, IWasmInstructionWriter code,
            IFunctionIndexResolver functionIndices)
        {
            Call = request;
            Code = code;
            Indices = functionIndices;
            Events.Add("call");
        }
    }
}
