using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;

namespace NetWasm.Runtime.Pack.Materialization;

// wasm-ld reads its arguments from a response file next to its log, so a link with hundreds of native bindings (two
// arguments each) stays under Windows' 32,767-character command line (zion-sati/NetWasm#236). wasm-ld is told to read
// the file with LLVM's GNU tokenizer on every host (--rsp-quoting=posix), which, unlike a POSIX shell, takes a backslash
// inside quotes as an escape: each argument is single-quoted, and its backslashes (Windows path separators) and single
// quotes are escaped with one.
internal sealed class ResponseFileLinkerCommandFactory : ILinkerCommandFactory
{
    public RuntimeCommand Create(string linkerPath, ImmutableArray<string> arguments, string logPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(linkerPath);
        ArgumentException.ThrowIfNullOrEmpty(logPath);
        var responseFile = Path.ChangeExtension(Path.GetFullPath(logPath), ".rsp");
        Directory.CreateDirectory(Path.GetDirectoryName(responseFile)!);
        File.WriteAllLines(responseFile, arguments.Select(Quote));
        return new(linkerPath, ["--rsp-quoting=posix", "@" + responseFile], logPath);
    }

    private static string Quote(string argument) =>
        "'" + argument.Replace(@"\", @"\\", StringComparison.Ordinal).Replace("'", @"\'", StringComparison.Ordinal) + "'";
}
