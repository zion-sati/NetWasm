namespace System;

public static partial class Console
{
    public static ConsoleColor BackgroundColor { get => throw UnsupportedTerminal(); set => throw UnsupportedTerminal(); }
    public static ConsoleColor ForegroundColor { get => throw UnsupportedTerminal(); set => throw UnsupportedTerminal(); }
    public static int BufferHeight { get => throw UnsupportedTerminal(); set => throw UnsupportedTerminal(); }
    public static int BufferWidth { get => throw UnsupportedTerminal(); set => throw UnsupportedTerminal(); }
    public static int CursorLeft { get => throw UnsupportedTerminal(); set => throw UnsupportedTerminal(); }
    public static int CursorTop { get => throw UnsupportedTerminal(); set => throw UnsupportedTerminal(); }
    public static int CursorSize { get => throw UnsupportedTerminal(); set => throw UnsupportedTerminal(); }
    public static bool CursorVisible { get => throw UnsupportedTerminal(); set => throw UnsupportedTerminal(); }
    public static string Title { get => throw UnsupportedTerminal(); set => throw UnsupportedTerminal(); }
    public static bool TreatControlCAsInput { get => throw UnsupportedTerminal(); set => throw UnsupportedTerminal(); }
    public static int WindowHeight { get => throw UnsupportedTerminal(); set => throw UnsupportedTerminal(); }
    public static int WindowWidth { get => throw UnsupportedTerminal(); set => throw UnsupportedTerminal(); }
    public static int WindowLeft { get => throw UnsupportedTerminal(); set => throw UnsupportedTerminal(); }
    public static int WindowTop { get => throw UnsupportedTerminal(); set => throw UnsupportedTerminal(); }
    public static bool CapsLock => throw UnsupportedTerminal();
    public static bool NumberLock => throw UnsupportedTerminal();
    public static bool KeyAvailable => throw UnsupportedTerminal();
    public static int LargestWindowHeight => throw UnsupportedTerminal();
    public static int LargestWindowWidth => throw UnsupportedTerminal();
    public static void Beep() => throw UnsupportedTerminal();
    public static void Beep(int frequency, int duration) => throw UnsupportedTerminal();
    public static void Clear() => throw UnsupportedTerminal();
    public static void ResetColor() => throw UnsupportedTerminal();
    public static void SetBufferSize(int width, int height) => throw UnsupportedTerminal();
    public static void SetCursorPosition(int left, int top) => throw UnsupportedTerminal();
    public static void SetWindowPosition(int left, int top) => throw UnsupportedTerminal();
    public static void SetWindowSize(int width, int height) => throw UnsupportedTerminal();
    public static void MoveBufferArea(int sourceLeft, int sourceTop, int sourceWidth, int sourceHeight, int targetLeft, int targetTop) => throw UnsupportedTerminal();
    public static void MoveBufferArea(int sourceLeft, int sourceTop, int sourceWidth, int sourceHeight, int targetLeft, int targetTop, char sourceChar, ConsoleColor sourceForeColor, ConsoleColor sourceBackColor) => throw UnsupportedTerminal();
    public static ConsoleKeyInfo ReadKey() => throw UnsupportedTerminal();
    public static ConsoleKeyInfo ReadKey(bool intercept) => throw UnsupportedTerminal();
    public static event ConsoleCancelEventHandler? CancelKeyPress
    {
        add => throw UnsupportedTerminal();
        remove => throw UnsupportedTerminal();
    }

    private static PlatformNotSupportedException UnsupportedTerminal() =>
        new("Interactive terminal control and keyboard events are not available on NetWasm.");
}
