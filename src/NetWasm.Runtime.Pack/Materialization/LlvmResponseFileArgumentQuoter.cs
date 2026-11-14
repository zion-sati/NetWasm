using System;

namespace NetWasm.Runtime.Pack.Materialization;

// Quotes for LLVM's GNU tokenizer, which wasm-ld reads response files with under --rsp-quoting=posix on every host:
// unlike a POSIX shell it takes a backslash inside quotes as an escape, so each argument is single-quoted and its
// backslashes (Windows path separators) and single quotes are escaped with one.
internal sealed class LlvmResponseFileArgumentQuoter : IResponseFileArgumentQuoter
{
    public string Quote(string argument)
    {
        ArgumentNullException.ThrowIfNull(argument);
        return "'" + argument.Replace(@"\", @"\\", StringComparison.Ordinal).Replace("'", @"\'", StringComparison.Ordinal) + "'";
    }
}
