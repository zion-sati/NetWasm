using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class CilInstructionDispatcherTests
{
    [Fact]
    public void DispatchesDirectlyToCommandRegisteredForOperation()
    {
        var calls = new List<string>();
        var command = new InstructionCommand(
            CilOperation.Nop,
            InstructionFamily.ConstantsStackLocalsArguments,
            (_, _) => calls.Add("command"));
        var dispatcher = new CilInstructionDispatcher(
            new InstructionCommandRegistry([command], [CilOperation.Nop]),
            new RecordingRootPublicationEmitter(_ => calls.Add("roots")));
        var request = CreateInstructionRequest(
            CilOperation.Nop,
            maxStack: 0);

        dispatcher.Emit(request, GetCodeWriter(request), CreateFunctionIndexResolver());

        Assert.Equal(["roots", "command"], calls);
    }
}
