namespace NetWasm.Hosting.Execution;

/// <summary>One explicitly supplied case-sensitive guest environment value.</summary>
public sealed record NetWasmEnvironmentVariable(string Name, string Value);
