namespace System.Runtime.InteropServices.TimeZones
{
    internal interface IEnvironmentVariableReader
    {
        string? Read(string name);
    }
}
