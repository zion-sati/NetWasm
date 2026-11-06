using System;

namespace NetWasm.Compiler.ComponentModel.Merging;

// A diagnostic twin, built with NETWASM_KEEP_NAMES set and never timed, keeps its functions'
// names through the merge so profiles and listings can name them; a product module keeps none.
internal static class DiagnosticNames
{
    public static bool Kept => Environment.GetEnvironmentVariable("NETWASM_KEEP_NAMES") is { Length: > 0 };
}
