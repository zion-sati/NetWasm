namespace NetWasm.Hosting.Execution;

/// <summary>An ephemeral host-directory binding and its guest-visible authority.</summary>
public sealed record NetWasmPreopenGrant(
    string HostPath,
    string GuestPath,
    NetWasmPreopenAccess Access);
