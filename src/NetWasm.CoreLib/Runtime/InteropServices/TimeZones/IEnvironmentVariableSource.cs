namespace System.Runtime.InteropServices.TimeZones
{
    internal interface IEnvironmentVariableSource
    {
        EnvironmentVariable[] Read();
    }
}
