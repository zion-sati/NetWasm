// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace System.Runtime.InteropServices.TimeZones
{
    using System.Collections.Generic;

    // Reads the zone index only. Hosts verify the asset digest before mounting it, and each
    // zone's transition table is read when that zone is first used.
    internal sealed class TimeZoneAssetParser : ITimeZoneAssetParser
    {
        private const int DigestLength = 32;
        private static readonly byte[] Magic = { 0x4e, 0x57, 0x54, 0x5a, 1, 0, 0, 0 };

        public TimeZoneAsset Parse(ITimeZoneAssetFile file)
        {
            if (file == null)
            {
                throw new ArgumentNullException();
            }
            var cursor = new TimeZoneAssetCursor(file, 0);
            for (var index = 0; index < Magic.Length; index++)
            {
                if (cursor.ReadByte() != Magic[index])
                {
                    throw TimeZoneAssetCursor.Invalid("asset format version is incompatible");
                }
            }
            var dataVersion = cursor.ReadText(cursor.ReadUInt16(), "IANA version");
            if (dataVersion.Length == 0)
            {
                throw TimeZoneAssetCursor.Invalid("IANA version is empty");
            }
            var zoneCount = cursor.ReadCount("zone count");
            if (zoneCount == 0)
            {
                throw TimeZoneAssetCursor.Invalid("asset contains no zones");
            }
            var zones = new List<TimeZoneAssetEntry>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            for (var zoneIndex = 0; zoneIndex < zoneCount; zoneIndex++)
            {
                var name = cursor.ReadText(cursor.ReadUInt16(), "zone name");
                if (name.Length == 0 || name[0] == '/' || name.Contains(".."))
                {
                    throw TimeZoneAssetCursor.Invalid("zone name is not normalized");
                }
                if (!names.Add(name))
                {
                    throw TimeZoneAssetCursor.Invalid("asset contains a duplicate zone");
                }
                var initialOffset = cursor.ReadOffset();
                var initialDaylight = cursor.ReadBoolean();
                var transitionCount = cursor.ReadCount("transition count");
                zones.Add(new TimeZoneAssetEntry(
                    name,
                    initialOffset,
                    initialDaylight,
                    transitionCount,
                    cursor.Position));
                cursor.Skip((long)transitionCount * TimeZoneAssetCursor.TransitionLength);
            }
            var digest = file.Read(cursor.Position, DigestLength + 1);
            if (digest.Length < DigestLength)
            {
                throw TimeZoneAssetCursor.Invalid("asset is truncated");
            }
            if (digest.Length > DigestLength)
            {
                throw TimeZoneAssetCursor.Invalid("asset contains trailing payload data");
            }
            return new TimeZoneAsset(dataVersion, ToHex(digest), zones.ToArray(), file);
        }

        private static string ToHex(byte[] bytes)
        {
            const string alphabet = "0123456789abcdef";
            var characters = new char[bytes.Length * 2];
            for (var index = 0; index < bytes.Length; index++)
            {
                characters[index * 2] = alphabet[bytes[index] >> 4];
                characters[index * 2 + 1] = alphabet[bytes[index] & 15];
            }
            return new string(characters);
        }
    }
}
