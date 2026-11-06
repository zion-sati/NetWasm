using System;

namespace NetWasm.Compiler.ComponentModel.Merging;

// A diagnostic twin, built with NETWASM_KEEP_NAMES set and never timed, keeps its functions'
// names through the merge so profiles and listings can name them; a product module keeps none.
internal static class DiagnosticNames
{
    public static bool Kept => KeptFor(Environment.GetEnvironmentVariable("NETWASM_KEEP_NAMES"));

    // Whether a NETWASM_KEEP_NAMES value asks for names: any value but an empty one.
    internal static bool KeptFor(string? value) => value is { Length: > 0 };
}
