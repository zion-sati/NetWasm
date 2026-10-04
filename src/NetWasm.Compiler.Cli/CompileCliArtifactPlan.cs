using System.Collections.Immutable;

namespace NetWasm.Compiler.Cli;

internal sealed record BinaryCliArtifact(string Path, byte[] Content);

internal sealed record TextCliArtifact(string Path, string Content);

internal sealed record CompileCliArtifactPlan(
    string OutputPath,
    byte[] ApplicationModule,
    string NativeCallbackObjectPath,
    bool DeleteNativeCallbackObject,
    ImmutableArray<BinaryCliArtifact> BinarySidecars,
    ImmutableArray<TextCliArtifact> TextSidecars);
