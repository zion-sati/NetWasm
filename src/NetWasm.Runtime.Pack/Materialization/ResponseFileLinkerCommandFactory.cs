using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;

namespace NetWasm.Runtime.Pack.Materialization;

// wasm-ld reads its arguments from a response file next to its log, so a link with hundreds of native bindings (two
// arguments each) stays under Windows' 32,767-character command line (zion-sati/NetWasm#236). The file is quoted the
// POSIX way, which wasm-ld is told to expect on every host: inside single quotes every character is literal, Windows
// path separators included, and a single quote closes the quoting, is escaped and reopens it.
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

    private static string Quote(string argument) => "'" + argument.Replace("'", @"'\''", StringComparison.Ordinal) + "'";
}
