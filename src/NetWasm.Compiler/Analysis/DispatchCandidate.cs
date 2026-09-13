using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

internal readonly record struct DispatchCandidate(
    string DispatchKey,
    DispatchDeclaration Declaration,
    CliTypeIdentity Receiver);

internal readonly record struct DispatchDeclarationCandidate(
    string DispatchKey,
    DispatchDeclaration Declaration);

internal readonly record struct DispatchReceiverCandidate(CliTypeIdentity Receiver);
