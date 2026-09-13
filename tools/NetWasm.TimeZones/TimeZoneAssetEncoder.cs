using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace NetWasm.TimeZones;

internal sealed class TimeZoneAssetEncoder : ITimeZoneAssetEncoder
{
    private static readonly byte[] Magic = [0x4e, 0x57, 0x54, 0x5a, 1, 0, 0, 0];

    public TimeZoneAssetArtifact Encode(
        string dataVersion,
        ImmutableArray<TimeZoneDefinition> zones)
    {
        ValidateText(dataVersion, "IANA version");
        if (zones.IsDefaultOrEmpty)
        {
            throw new ArgumentException("Timezone asset requires at least one zone.");
        }
        using var payload = new MemoryStream();
        using (var writer = new BinaryWriter(payload, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(Magic);
            WriteText(writer, dataVersion, "IANA version");
            writer.Write(zones.Length);
            string? previousName = null;
            foreach (var zone in zones)
            {
                ArgumentNullException.ThrowIfNull(zone);
                ValidateText(zone.Name, "zone name");
                if (previousName is not null &&
                    StringComparer.Ordinal.Compare(previousName, zone.Name) >= 0)
                {
                    throw new ArgumentException(
                        "Timezone definitions must be uniquely ordered by name.");
                }
                previousName = zone.Name;
                WriteText(writer, zone.Name, "zone name");
                writer.Write(zone.InitialOffsetSeconds);
                writer.Write(zone.InitialDaylightSavingTime);
                writer.Write(zone.Transitions.Length);
                long previousTransition = long.MinValue;
                foreach (var transition in zone.Transitions)
                {
                    if (transition.UnixSeconds <= previousTransition)
                    {
                        throw new ArgumentException(
                            "Timezone transitions must be strictly ordered.");
                    }
                    previousTransition = transition.UnixSeconds;
                    writer.Write(transition.UnixSeconds);
                    writer.Write(transition.OffsetSeconds);
                    writer.Write(transition.IsDaylightSavingTime);
                }
            }
        }
        var bytes = payload.ToArray();
        var digest = SHA256.HashData(bytes);
        return new TimeZoneAssetArtifact(
            [.. bytes, .. digest],
            Convert.ToHexStringLower(digest));
    }

    private static void WriteText(BinaryWriter writer, string value, string field)
    {
        ValidateText(value, field);
        var bytes = Encoding.ASCII.GetBytes(value);
        writer.Write(checked((ushort)bytes.Length));
        writer.Write(bytes);
    }

    private static void ValidateText(string value, string field)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length == 0 || value.Length > ushort.MaxValue ||
            value.Any(character => character is < (char)0x21 or > (char)0x7e))
        {
            throw new ArgumentException($"{field} must be printable ASCII.");
        }
    }
}
