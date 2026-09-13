namespace System.Runtime.InteropServices.TimeZones
{
    internal sealed class TimeZoneAsset
    {
        internal TimeZoneAsset(string dataVersion, string identity, TimeZoneDefinition[] zones)
        {
            DataVersion = dataVersion ?? throw new ArgumentNullException();
            Identity = identity ?? throw new ArgumentNullException();
            Zones = zones ?? throw new ArgumentNullException();
        }

        internal string DataVersion { get; }
        internal string Identity { get; }
        internal TimeZoneDefinition[] Zones { get; }
    }
}
