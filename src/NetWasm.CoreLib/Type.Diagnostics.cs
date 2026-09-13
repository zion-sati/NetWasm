namespace System;

public sealed partial class Type
{
    public override string Name => throw new PlatformNotSupportedException("Runtime type names are unavailable because reflection metadata is not deployed.");

    public string? Namespace => throw new PlatformNotSupportedException("Runtime type namespaces are unavailable because reflection metadata is not deployed.");

    public string? FullName => throw new PlatformNotSupportedException("Runtime full type names are unavailable because reflection metadata is not deployed.");

    public override string ToString() => "<runtime type #" + GetHashCode().ToString() + ">";
}
