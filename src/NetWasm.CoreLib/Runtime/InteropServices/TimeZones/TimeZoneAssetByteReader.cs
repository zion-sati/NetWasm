namespace System.Runtime.InteropServices.TimeZones
{
    internal sealed class TimeZoneAssetByteReader(
        IPreopenedDirectorySource preopens,
        IReadOnlyFileOpener opener,
        IReadOnlyFileReader reader,
        IDescriptorReleaser releaser) : ITimeZoneAssetByteReader
    {
        internal const string MountPath = "/netwasm-timezones";
        private readonly IPreopenedDirectorySource _preopens =
            preopens ?? throw new ArgumentNullException();
        private readonly IReadOnlyFileOpener _opener =
            opener ?? throw new ArgumentNullException();
        private readonly IReadOnlyFileReader _reader =
            reader ?? throw new ArgumentNullException();
        private readonly IDescriptorReleaser _releaser =
            releaser ?? throw new ArgumentNullException();

        public byte[] Read(string assetName)
        {
            if (assetName == null)
            {
                throw new ArgumentNullException();
            }
            if (assetName.Length == 0 || assetName[0] == '/' || assetName.Contains(".."))
            {
                throw new PlatformNotSupportedException(
                    "Timezone asset name is not a normalized relative path.");
            }
            foreach (var directory in _preopens.Read())
            {
                if (directory.Path != MountPath)
                {
                    continue;
                }
                var handle = _opener.Open(directory.Handle, assetName);
                try
                {
                    return _reader.Read(handle);
                }
                finally
                {
                    _releaser.Release(handle);
                }
            }
            throw new PlatformNotSupportedException(
                "Timezone asset mount '" + MountPath + "' is unavailable.");
        }
    }
}
