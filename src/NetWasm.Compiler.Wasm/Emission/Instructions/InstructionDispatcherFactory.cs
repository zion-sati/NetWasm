using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

namespace NetWasm.Compiler.Wasm.Emission.Instructions;

internal interface IInstructionDispatcherFactory
{
    ICilInstructionDispatcher Create();
}

internal sealed class InstructionDispatcherFactory(
    IEnumerable<IInstructionCommandProvider> providers,
    IRootPublicationEmitter roots) : IInstructionDispatcherFactory
{
    public ICilInstructionDispatcher Create() => new CilInstructionDispatcher(new InstructionCommandRegistry(
        providers.SelectMany(provider => provider.Commands)), roots);
}
