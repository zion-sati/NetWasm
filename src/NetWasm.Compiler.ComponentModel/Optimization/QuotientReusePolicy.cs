namespace NetWasm.Compiler.ComponentModel.Optimization;

public enum QuotientReusePolicy
{
    Disabled,
    // A remainder of two values an earlier division divided is computed from that division's quotient.
    AvailableQuotients,
}
