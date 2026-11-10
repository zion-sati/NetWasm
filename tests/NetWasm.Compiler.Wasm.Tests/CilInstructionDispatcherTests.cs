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
            new RecordingRootPublicationEmitter(_ => calls.Add("roots")),
            new RecordingStackStorageAnchorEmitter(_ => calls.Add("anchors")));
        var request = CreateInstructionRequest(
            CilOperation.Nop,
            maxStack: 0);

        dispatcher.Emit(request, GetCodeWriter(request), CreateFunctionIndexResolver());

        Assert.Equal(["roots", "command", "anchors"], calls);
    }

    // A command that publishes its own roots, as newobj does, gets no publication
    // from the dispatcher.
    [Fact]
    public void CommandsThatPublishTheirOwnRootsAreNotPublishedFor()
    {
        var calls = new List<string>();
        var command = new InstructionCommand(
            CilOperation.Nop,
            InstructionFamily.ConstantsStackLocalsArguments,
            (_, _) => calls.Add("command"))
        {
            PublishesRoots = true,
        };
        var dispatcher = new CilInstructionDispatcher(
            new InstructionCommandRegistry([command], [CilOperation.Nop]),
            new RecordingRootPublicationEmitter(_ => calls.Add("roots")),
            new RecordingStackStorageAnchorEmitter(_ => calls.Add("anchors")));
        var request = CreateInstructionRequest(CilOperation.Nop, maxStack: 0);

        dispatcher.Emit(request, GetCodeWriter(request), CreateFunctionIndexResolver());

        Assert.Equal(["command", "anchors"], calls);
    }

    [Fact]
    public void FailedCommandsDoNotUpdateBackingAnchors()
    {
        var calls = new List<string>();
        var command = new InstructionCommand(CilOperation.Nop,
            InstructionFamily.ConstantsStackLocalsArguments, (_, _) =>
            {
                calls.Add("command");
                throw new InvalidOperationException();
            });
        var dispatcher = new CilInstructionDispatcher(
            new InstructionCommandRegistry([command], [CilOperation.Nop]),
            new RecordingRootPublicationEmitter(_ => calls.Add("roots")),
            new RecordingStackStorageAnchorEmitter(_ => calls.Add("anchors")));
        var request = CreateInstructionRequest(CilOperation.Nop, maxStack: 0);
        Assert.Throws<InvalidOperationException>(() => dispatcher.Emit(request, GetCodeWriter(request), CreateFunctionIndexResolver()));
        Assert.Equal(["roots", "command"], calls);
        calls.Clear();
        Assert.Throws<ArgumentNullException>(() => dispatcher.Emit(null!, GetCodeWriter(request), CreateFunctionIndexResolver()));
        Assert.Empty(calls);
    }
}
