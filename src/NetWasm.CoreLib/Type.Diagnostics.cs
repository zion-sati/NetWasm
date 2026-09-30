namespace System;

public sealed partial class Type
{
    public override string Name => GetRuntimeName();

    public string? Namespace => GetRuntimeNamespace();

    public string? FullName => GetRuntimeFullName();

    public override string ToString() => GetRuntimeDisplayName();
}
