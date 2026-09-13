namespace System.Runtime.InteropServices.ConsoleServices;

internal interface IConsoleStringLineFormatter
{
    byte[] Format(string? value);
}
