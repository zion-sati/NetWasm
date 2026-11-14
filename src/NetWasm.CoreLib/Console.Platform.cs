namespace System;

public static partial class Console
{
    public static Text.Encoding InputEncoding
    {
        get => throw UnsupportedConsolePlatform();
        set { ArgumentNullException.ThrowIfNull(value); throw UnsupportedConsolePlatform(); }
    }

    // The existing standard output writer encodes bytes as UTF-8.
    public static Text.Encoding OutputEncoding
    {
        get => Text.Encoding.UTF8;
        set { ArgumentNullException.ThrowIfNull(value); throw UnsupportedConsolePlatform(); }
    }

    private static PlatformNotSupportedException UnsupportedConsolePlatform() =>
        new("Console encoding changes are not available on NetWasm.");
}
