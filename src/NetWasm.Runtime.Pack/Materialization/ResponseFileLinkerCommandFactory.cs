using System;
using System.Collections.Immutable;
using System.IO;

namespace NetWasm.Runtime.Pack.Materialization;

// wasm-ld reads its arguments from a response file next to its log, so a link with hundreds of native bindings (two
// arguments each) stays under Windows' 32,767-character command line (zion-sati/NetWasm#236). --rsp-quoting=posix
// selects the LLVM tokenizer the writer's quoting is for, on every host.
internal sealed class ResponseFileLinkerCommandFactory(IResponseFileWriter responseFiles) : ILinkerCommandFactory
{
    private readonly IResponseFileWriter _responseFiles = responseFiles ?? throw new ArgumentNullException(nameof(responseFiles));

    public RuntimeCommand Create(string linkerPath, ImmutableArray<string> arguments, string logPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(linkerPath);
        ArgumentException.ThrowIfNullOrEmpty(logPath);
        var responseFile = Path.ChangeExtension(Path.GetFullPath(logPath), ".rsp");
        _responseFiles.Write(responseFile, arguments);
        return new(linkerPath, ["--rsp-quoting=posix", "@" + responseFile], logPath);
    }
}
