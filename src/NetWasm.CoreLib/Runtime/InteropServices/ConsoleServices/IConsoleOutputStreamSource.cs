namespace System.Runtime.InteropServices.ConsoleServices;

internal interface IConsoleOutputStreamSource
{
    IPlatformOutputStream Open();
}
