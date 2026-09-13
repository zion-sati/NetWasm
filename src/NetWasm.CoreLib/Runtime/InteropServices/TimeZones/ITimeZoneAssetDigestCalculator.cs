namespace System.Runtime.InteropServices.TimeZones
{
    internal interface ITimeZoneAssetDigestCalculator
    {
        byte[] Calculate(byte[] contents, int length);
    }
}
