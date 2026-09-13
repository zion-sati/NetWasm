using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class InstructionDispatcherFactoryTests
{
    [Fact]
    public void CreatesDispatcherFromExplicitProviderCommands()
    {
        var provider = new RecordingProvider();
        var factory = new InstructionDispatcherFactory(
            [provider],
            new RecordingRootPublicationEmitter(_ => { }));
        var dispatcher = factory.Create();

        var request = EmitterTestSupport.CreateInstructionRequest(CilOperation.Nop);
        dispatcher.Emit(
            request,
            EmitterTestSupport.GetCodeWriter(request),
            EmitterTestSupport.CreateFunctionIndexResolver());

        Assert.True(provider.Emitted);
    }

    private sealed class RecordingProvider : IInstructionCommandProvider
    {
        public bool Emitted { get; private set; }

        public ImmutableArray<InstructionCommand> Commands =>
            [.. SupportedCil.Operations.Select(operation => new InstructionCommand(
                operation,
                InstructionFamily.ConstantsStackLocalsArguments,
                (_, _) => Emitted = operation == CilOperation.Nop))];
    }
}
