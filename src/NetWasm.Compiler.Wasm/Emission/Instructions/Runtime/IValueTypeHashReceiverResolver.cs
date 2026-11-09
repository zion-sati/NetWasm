using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal readonly record struct ValueTypeHashReceiver(CliTypeIdentity Type, CliValueKind Kind);

internal interface IValueTypeHashReceiverResolver
{
    ValueTypeHashReceiver Resolve(RuntimeIntrinsicEmissionRequest request);
}
