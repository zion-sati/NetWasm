// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace System.Runtime.InteropServices.TimeZones
{
    internal sealed class TimeZoneAssetFile(
        int handle,
        IReadOnlyFileReader reader) : ITimeZoneAssetFile
    {
        private readonly IReadOnlyFileReader _reader =
            reader ?? throw new ArgumentNullException();

        public byte[] Read(long offset, int length) =>
            _reader.Read(handle, offset, length);
    }
}
