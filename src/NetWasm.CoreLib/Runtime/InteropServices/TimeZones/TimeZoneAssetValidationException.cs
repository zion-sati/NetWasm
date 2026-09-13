namespace System.Runtime.InteropServices.TimeZones
{
    internal sealed class TimeZoneAssetValidationException :
        PlatformNotSupportedException
    {
        internal TimeZoneAssetValidationException(string reason) :
            base("Timezone asset validation failed: " + reason + ".")
        {
            Reason = reason ?? throw new ArgumentNullException();
        }

        internal string Reason { get; }
    }
}
