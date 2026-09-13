namespace NetWasm.Sdk.Pack.Policies;

public sealed class NetWasmPackException : InvalidOperationException
{
    public NetWasmPackException(NetWasmPackErrorCode code, string message)
        : base($"{code}: {message}")
    {
        Code = code;
        SafeMessage = message;
    }

    public NetWasmPackErrorCode Code { get; }

    public string SafeMessage { get; }
}
